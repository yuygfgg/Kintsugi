using System;
using System.Collections.Generic;
using System.IO;
using ManagedBass;
using ManagedBass.Midi;

namespace MidiPlayer.App.Services;

public sealed class BassMidiPlayer : IDisposable
{
    private const int DefaultTempoMicrosecondsPerQuarterNote = 500000;

    private int _streamHandle;
    private int _soundFontHandle;
    private bool _bassInitialized;
    private bool _isLooping;
    private MidiSystem _systemMode = MidiSystem.Default;
    private int _sampleRate = 44100;
    private TempoPoint[] _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
    private MidiFilterProcedure? _midiFilter;
    private readonly bool[] _channelMuted = new bool[16];
    private readonly bool[] _soloRestoreMuted = new bool[16];
    private readonly ChannelMixerState _channelMixer = new();
    private int _soloChannel = -1;
    private bool _hasSoloState;

    public bool IsChannelMuted(int channel)
    {
        if ((uint)channel >= 16) return false;
        return _channelMuted[channel];
    }

    public void ToggleChannelMute(int channel)
    {
        if ((uint)channel >= 16) return;

        ClearSoloState();
        SetChannelMuted(channel, !_channelMuted[channel]);
        NotesChanged?.Invoke();
    }

    public void ToggleChannelSolo(int channel)
    {
        if ((uint)channel >= 16) return;

        if (_hasSoloState && _soloChannel == channel)
        {
            for (int i = 0; i < _channelMuted.Length; i++)
            {
                SetChannelMuted(i, _soloRestoreMuted[i]);
            }

            ClearSoloState();
            NotesChanged?.Invoke();
            return;
        }

        if (!_hasSoloState)
        {
            Array.Copy(_channelMuted, _soloRestoreMuted, _channelMuted.Length);
            _hasSoloState = true;
        }

        _soloChannel = channel;

        for (int i = 0; i < _channelMuted.Length; i++)
        {
            SetChannelMuted(i, i != channel);
        }

        NotesChanged?.Invoke();
    }

    private bool FilterMidiEvent(int handle, int track, MidiEvent midiEvent, bool hirhythm, IntPtr user)
    {
        if (_systemMode != MidiSystem.Default && midiEvent.EventType is MidiEventType.System or MidiEventType.SystemEx)
        {
            return false;
        }

        if (_channelMixer.TryFilterMixEvent(handle, midiEvent))
        {
            return false;
        }

        if (midiEvent.Channel >= 0 && midiEvent.Channel < 16 && _channelMuted[midiEvent.Channel])
        {
            if (midiEvent.EventType == MidiEventType.Note)
            {
                int velocity = (midiEvent.Parameter >> 8) & 0xFF;
                if (velocity > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    public const int DefaultChannelReverb = 40;
    public const int DefaultChannelChorus = 0;
    public const int DefaultChannelVolume = 100;
    public const int DefaultMixPercent = 100;
    public const int MinMixPercent = 0;
    public const int MaxMixPercent = 200;
    public const int DefaultEffectScalePercent = DefaultMixPercent;
    public const int MinEffectScalePercent = MinMixPercent;
    public const int MaxEffectScalePercent = MaxMixPercent;

    private int _masterVolumePercent = DefaultMixPercent;
    private int _reverbReturnPercent = DefaultMixPercent;
    private int _chorusReturnPercent = DefaultMixPercent;

    public int MasterVolumePercent
    {
        get => _masterVolumePercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_masterVolumePercent == clampedValue) return;
            _masterVolumePercent = clampedValue;
            _channelMixer.SetGlobalPercents(_masterVolumePercent, _reverbReturnPercent, _chorusReturnPercent);
            ReapplyChannelMix();
        }
    }

    public int ReverbReturnPercent
    {
        get => _reverbReturnPercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_reverbReturnPercent == clampedValue) return;
            _reverbReturnPercent = clampedValue;
            _channelMixer.SetGlobalPercents(_masterVolumePercent, _reverbReturnPercent, _chorusReturnPercent);
            ReapplyChannelMix();
        }
    }

    public int ChorusReturnPercent
    {
        get => _chorusReturnPercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_chorusReturnPercent == clampedValue) return;
            _chorusReturnPercent = clampedValue;
            _channelMixer.SetGlobalPercents(_masterVolumePercent, _reverbReturnPercent, _chorusReturnPercent);
            ReapplyChannelMix();
        }
    }

    public int GetChannelVolumePercent(int channel)
        => _channelMixer.GetChannelVolumePercent(channel);

    public int GetChannelReverbSendPercent(int channel)
        => _channelMixer.GetChannelReverbSendPercent(channel);

    public int GetChannelChorusSendPercent(int channel)
        => _channelMixer.GetChannelChorusSendPercent(channel);

    public void SetChannelVolumePercent(int channel, int value)
    {
        if (!_channelMixer.SetChannelVolumePercent(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelReverbSendPercent(int channel, int value)
    {
        if (!_channelMixer.SetChannelReverbSendPercent(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelChorusSendPercent(int channel, int value)
    {
        if (!_channelMixer.SetChannelChorusSendPercent(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    private void ReapplyChannelMix()
        => _channelMixer.ApplyAll(_streamHandle);

    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            if (_sampleRate == value) return;
            _sampleRate = value;
            if (_bassInitialized)
            {
                Reinitialize();
            }
        }
    }

    private void Reinitialize()
    {
        string? currentMidi = MidiPath;
        string? currentSf = SoundFontPath;
        bool wasPlaying = IsPlaying;
        double currentPos = GetPositionSeconds();

        Dispose();

        EnsureInitialized();

        if (!string.IsNullOrEmpty(currentSf))
        {
            try { LoadSoundFont(currentSf); } catch { }
        }

        if (!string.IsNullOrEmpty(currentMidi))
        {
            try
            {
                LoadMidi(currentMidi);
                if (currentPos > 0)
                {
                    Seek(currentPos);
                }
                if (wasPlaying)
                {
                    Play();
                }
            }
            catch { }
        }
    }

    private SyncProcedure[]? _syncProcs;
    private SyncProcedure? _loopSyncProc;

    public string? MidiPath { get; private set; }

    public string? SoundFontPath { get; private set; }

    public bool HasStream => _streamHandle != 0;

    public bool IsPlaying => HasStream && Bass.ChannelIsActive(_streamHandle) == PlaybackState.Playing;

    public bool IsLooping
    {
        get => _isLooping;
        set => _isLooping = value;
    }

    public bool[,] ActiveNotes { get; } = new bool[16, 128];

    public event Action? NotesChanged;

    public MidiSystem SystemMode
    {
        get => _systemMode;
        set
        {
            if (_systemMode == value)
            {
                return;
            }

            _systemMode = value;

            if (_streamHandle != 0)
            {
                Reinitialize();
            }
        }
    }

    public void LoadMidi(string path)
    {
        EnsureInitialized();
        EnsureFileExists(path, "MIDI");

        FreeStream();
        ClearNotes();

        _streamHandle = BassMidi.CreateStream(path, 0, 0, BassFlags.Prescan, _sampleRate);
        if (_streamHandle == 0)
        {
            throw CreateBassException("Failed to load MIDI file");
        }

        MidiPath = path;

        if (_soundFontHandle != 0)
        {
            ApplyLoadedSoundFont();
        }

        ConfigureSystemModeBehavior();

        RegisterMidiSyncs();
        BuildTempoMap();

        _channelMixer.ResetTrackedValues();
        ReapplyChannelMix();
    }

    private void RegisterMidiSyncs()
    {
        _syncProcs = new SyncProcedure[4];
        _syncProcs[0] = (_, _, data, _) =>
        {
            int note = data & 0xFF;
            int velocity = (data >> 8) & 0xFF;
            int midiChannel = GetMidiSyncChannel(data);
            if ((uint)midiChannel >= ActiveNotes.GetLength(0) || note >= ActiveNotes.GetLength(1))
            {
                return;
            }

            ActiveNotes[midiChannel, note] = velocity > 0;
            NotesChanged?.Invoke();
        };
        RegisterMidiEventSync(MidiEventType.Note, _syncProcs[0]);

        _syncProcs[1] = (_, _, data, _) => ClearChannelNotes(GetMidiSyncChannel(data));
        RegisterMidiEventSync(MidiEventType.NotesOff, _syncProcs[1]);

        _syncProcs[2] = (_, _, data, _) => ClearChannelNotes(GetMidiSyncChannel(data));
        RegisterMidiEventSync(MidiEventType.SoundOff, _syncProcs[2]);

        _syncProcs[3] = (_, _, data, _) =>
        {
            int channel = GetMidiSyncChannel(data);
            ClearChannelNotes(channel);
            _channelMixer.HandleResetEvent(_streamHandle, channel);
        };
        RegisterMidiEventSync(MidiEventType.Reset, _syncProcs[3]);

        _loopSyncProc = (_, _, _, _) =>
        {
            if (!_isLooping || _streamHandle == 0)
            {
                return;
            }

            _channelMixer.ResetTrackedValues();
            Bass.ChannelSetPosition(_streamHandle, 0, PositionFlags.Bytes);
            ReapplySystemModeOverride();
            ReapplyChannelMix();
            ClearNotes();
        };

        if (Bass.ChannelSetSync(_streamHandle, SyncFlags.End | SyncFlags.Mixtime, 0, _loopSyncProc) == 0)
        {
            throw CreateBassException("Failed to register playback loop");
        }
    }

    private void RegisterMidiEventSync(MidiEventType eventType, SyncProcedure syncProc)
    {
        if (Bass.ChannelSetSync(_streamHandle, SyncFlags.MidiEvent, (long)eventType, syncProc) == 0)
        {
            throw CreateBassException($"Failed to register {eventType} sync");
        }
    }

    private void BuildTempoMap()
    {
        if (_streamHandle == 0)
        {
            _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
            return;
        }

        var tempoEvents = BassMidi.StreamGetEvents(_streamHandle, 0, MidiEventType.Tempo);
        if (tempoEvents is null || tempoEvents.Length == 0)
        {
            _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
            return;
        }

        var points = new List<TempoPoint>(tempoEvents.Length + 1)
        {
            new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)
        };

        foreach (var tempoEvent in tempoEvents)
        {
            if (tempoEvent.Parameter <= 0)
            {
                continue;
            }

            points.Add(new TempoPoint(tempoEvent.Ticks, 60_000_000d / tempoEvent.Parameter));
        }

        points.Sort(static (left, right) => left.Tick.CompareTo(right.Tick));

        var normalizedPoints = new List<TempoPoint>(points.Count);
        foreach (var point in points)
        {
            if (normalizedPoints.Count > 0 && normalizedPoints[^1].Tick == point.Tick)
            {
                normalizedPoints[^1] = point;
                continue;
            }

            normalizedPoints.Add(point);
        }

        _tempoMap = normalizedPoints.Count == 0
            ? [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)]
            : [.. normalizedPoints];
    }

    private void ClearNotes()
    {
        Array.Clear(ActiveNotes, 0, ActiveNotes.Length);
        NotesChanged?.Invoke();
    }

    private void ClearChannelNotes(int channel)
    {
        if ((uint)channel >= ActiveNotes.GetLength(0))
        {
            return;
        }

        bool changed = false;
        for (int note = 0; note < ActiveNotes.GetLength(1); note++)
        {
            if (!ActiveNotes[channel, note])
            {
                continue;
            }

            ActiveNotes[channel, note] = false;
            changed = true;
        }

        if (changed)
        {
            NotesChanged?.Invoke();
        }
    }

    private static int GetMidiSyncChannel(int data)
        => (data >> 16) & 0xFFFF;

    private void ClearSoloState()
    {
        _soloChannel = -1;
        _hasSoloState = false;
    }

    private void SetChannelMuted(int channel, bool muted)
    {
        if ((uint)channel >= 16 || _channelMuted[channel] == muted)
        {
            return;
        }

        _channelMuted[channel] = muted;
        if (!muted)
        {
            return;
        }

        if (_streamHandle != 0)
        {
            BassMidi.StreamEvent(_streamHandle, channel, MidiEventType.NotesOff, 0);
            BassMidi.StreamEvent(_streamHandle, channel, MidiEventType.SoundOff, 0);
        }

        ClearChannelNotes(channel);
    }

    public void LoadSoundFont(string path)
    {
        EnsureInitialized();
        EnsureFileExists(path, "SoundFont");

        var newHandle = BassMidi.FontInit(path, FontInitFlags.Unicode | FontInitFlags.MemoryMap);
        if (newHandle == 0)
        {
            throw CreateBassException("Failed to load SoundFont");
        }

        var oldHandle = _soundFontHandle;
        _soundFontHandle = newHandle;
        SoundFontPath = path;

        if (_streamHandle != 0)
        {
            ApplyLoadedSoundFont();
        }

        if (oldHandle != 0)
        {
            BassMidi.FontFree(oldHandle);
        }
    }

    public void ExportCurrentMidiToWav(WavExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        EnsureInitialized();
        EnsureStreamLoaded();

        if (string.IsNullOrWhiteSpace(MidiPath))
        {
            throw new InvalidOperationException("Load a MIDI file first.");
        }

        if (string.IsNullOrWhiteSpace(SoundFontPath))
        {
            throw new InvalidOperationException("Please load a SoundFont before exporting.");
        }

        if (options.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Sample rate must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("An output path is required.", nameof(options));
        }

        ExportMidiToWav(
            MidiPath,
            SoundFontPath,
            options,
            _systemMode,
            _channelMuted,
            _masterVolumePercent,
            _reverbReturnPercent,
            _chorusReturnPercent,
            _channelMixer.GetChannelVolumePercents(),
            _channelMixer.GetChannelReverbSendPercents(),
            _channelMixer.GetChannelChorusSendPercents());
    }

    public void Play()
    {
        EnsureStreamLoaded();

        if (_soundFontHandle == 0)
        {
            throw new InvalidOperationException("Please load a SoundFont before playing.");
        }

        if (!Bass.ChannelPlay(_streamHandle, false))
        {
            throw CreateBassException("Failed to start playback");
        }
    }

    public void Pause()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        if (!Bass.ChannelPause(_streamHandle))
        {
            throw CreateBassException("Failed to pause playback");
        }
        ClearNotes();
    }

    public void Seek(double seconds)
    {
        if (_streamHandle == 0)
        {
            return;
        }

        var boundedSeconds = Math.Clamp(seconds, 0, GetDurationSeconds());
        var bytePosition = Bass.ChannelSeconds2Bytes(_streamHandle, boundedSeconds);

        _channelMixer.ResetTrackedValues();
        if (!Bass.ChannelSetPosition(_streamHandle, bytePosition, PositionFlags.Bytes))
        {
            throw CreateBassException("Failed to seek");
        }

        ReapplySystemModeOverride();
        ReapplyChannelMix();
        ClearNotes();
    }

    public double GetDurationSeconds()
    {
        if (_streamHandle == 0)
        {
            return 0;
        }

        var lengthInBytes = Bass.ChannelGetLength(_streamHandle, PositionFlags.Bytes);
        return lengthInBytes <= 0
            ? 0
            : Bass.ChannelBytes2Seconds(_streamHandle, lengthInBytes);
    }

    public double GetPositionSeconds()
    {
        if (_streamHandle == 0)
        {
            return 0;
        }

        var positionInBytes = Bass.ChannelGetPosition(_streamHandle, PositionFlags.Bytes);
        return positionInBytes <= 0
            ? 0
            : Bass.ChannelBytes2Seconds(_streamHandle, positionInBytes);
    }

    public double GetCurrentBpm()
    {
        if (_streamHandle == 0 || _tempoMap.Length == 0)
        {
            return 0;
        }

        var tickPosition = Bass.ChannelGetPosition(_streamHandle, PositionFlags.MIDITick);
        if (tickPosition < 0)
        {
            return _tempoMap[0].Bpm;
        }

        int low = 0;
        int high = _tempoMap.Length - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_tempoMap[mid].Tick <= tickPosition)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        return _tempoMap[low].Bpm;
    }

    public int GetFFTData(float[] buffer)
    {
        if (_streamHandle == 0)
        {
            Array.Clear(buffer, 0, buffer.Length);
            return 0;
        }

        DataFlags flag = buffer.Length switch
        {
            <= 128 => DataFlags.FFT256,
            <= 256 => DataFlags.FFT512,
            <= 512 => DataFlags.FFT1024,
            <= 1024 => DataFlags.FFT2048,
            <= 2048 => DataFlags.FFT4096,
            <= 4096 => DataFlags.FFT8192,
            <= 8192 => DataFlags.FFT16384,
            _ => DataFlags.FFT32768
        };

        return Bass.ChannelGetData(_streamHandle, buffer, (int)flag);
    }

    public void Dispose()
    {
        FreeStream();

        if (_soundFontHandle != 0)
        {
            BassMidi.FontFree(_soundFontHandle);
            _soundFontHandle = 0;
        }

        if (_bassInitialized)
        {
            Bass.Free();
            _bassInitialized = false;
        }
    }

    private void EnsureInitialized()
    {
        if (_bassInitialized)
        {
            return;
        }

        if (!Bass.Init(-1, SampleRate, DeviceInitFlags.Default, IntPtr.Zero, IntPtr.Zero))
        {
            throw CreateBassException("Failed to initialize BASS");
        }

        BassMidi.AutoFont = 0;
        _bassInitialized = true;
    }

    private void ApplyLoadedSoundFont()
        => ApplySoundFont(_streamHandle, _soundFontHandle);

    private void ConfigureSystemModeBehavior()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        _midiFilter ??= FilterMidiEvent;

        if (!BassMidi.StreamSetFilter(_streamHandle, true, _midiFilter, IntPtr.Zero))
        {
            throw CreateBassException("Failed to install MIDI filter");
        }

        if (_systemMode != MidiSystem.Default)
        {
            ApplySystemMode(_streamHandle, _systemMode);
        }
    }

    private void ReapplySystemModeOverride()
    {
        if (_streamHandle == 0 || _systemMode == MidiSystem.Default)
        {
            return;
        }

        ApplySystemMode(_streamHandle, _systemMode);
    }

    private static void ApplySystemMode(int streamHandle, MidiSystem systemMode)
    {
        if (!BassMidi.StreamEvent(streamHandle, 0, MidiEventType.SystemEx, (int)systemMode))
        {
            throw CreateBassException("Failed to apply system mode");
        }
    }

    private static void ApplySoundFont(int streamHandle, int fontHandle)
    {
        var fonts = new[]
        {
            new MidiFont
            {
                Handle = fontHandle,
                Preset = -1,
                Bank = 0
            }
        };

        if (BassMidi.StreamSetFonts(streamHandle, fonts, fonts.Length) == 0)
        {
            throw CreateBassException("Failed to apply SoundFont");
        }
    }

    private static void ConfigureExportFilter(
        int streamHandle,
        MidiSystem systemMode,
        bool[]? channelMuted,
        ChannelMixerState mixerState,
        out MidiFilterProcedure filter,
        out SyncProcedure resetSync)
    {
        filter = (handle, track, midiEvent, hirhythm, user) =>
        {
            if (systemMode != MidiSystem.Default && midiEvent.EventType is MidiEventType.System or MidiEventType.SystemEx)
            {
                return false;
            }

            if (mixerState.TryFilterMixEvent(handle, midiEvent))
            {
                return false;
            }

            if (channelMuted != null && midiEvent.Channel >= 0 && midiEvent.Channel < 16 && channelMuted[midiEvent.Channel])
            {
                if (midiEvent.EventType == MidiEventType.Note)
                {
                    int velocity = (midiEvent.Parameter >> 8) & 0xFF;
                    if (velocity > 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        };

        if (!BassMidi.StreamSetFilter(streamHandle, true, filter, IntPtr.Zero))
        {
            throw CreateBassException("Failed to install export MIDI filter");
        }

        resetSync = (_, _, data, _) => mixerState.HandleResetEvent(streamHandle, GetMidiSyncChannel(data));
        if (Bass.ChannelSetSync(streamHandle, SyncFlags.MidiEvent, (long)MidiEventType.Reset, resetSync) == 0)
        {
            throw CreateBassException("Failed to register export reset sync");
        }

        if (systemMode != MidiSystem.Default)
        {
            ApplySystemMode(streamHandle, systemMode);
        }
    }

    private void EnsureStreamLoaded()
    {
        if (_streamHandle == 0)
        {
            throw new InvalidOperationException("Load a MIDI file first.");
        }
    }

    private void FreeStream()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        _syncProcs = null;
        _loopSyncProc = null;
        _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
        _channelMixer.ResetTrackedValues();
        Bass.StreamFree(_streamHandle);
        _streamHandle = 0;
        MidiPath = null;
        ClearNotes();
    }

    private static void EnsureFileExists(string path, string fileType)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{fileType} file was not found.", path);
        }
    }

    private static Exception CreateBassException(string message)
    {
        return new InvalidOperationException($"{message} ({Bass.LastError}).");
    }

    private static void ExportMidiToWav(
        string midiPath,
        string soundFontPath,
        WavExportOptions options,
        MidiSystem systemMode,
        bool[]? channelMuted,
        int masterVolumePercent,
        int reverbReturnPercent,
        int chorusReturnPercent,
        int[] channelVolumePercents,
        int[] channelReverbSendPercents,
        int[] channelChorusSendPercents)
    {
        int exportStreamHandle = 0;
        int exportFontHandle = 0;

        try
        {
            exportStreamHandle = BassMidi.CreateStream(
                midiPath,
                0,
                0,
                BassFlags.Decode | BassFlags.Prescan,
                options.SampleRate);

            if (exportStreamHandle == 0)
            {
                throw CreateBassException("Failed to create export stream");
            }

            exportFontHandle = BassMidi.FontInit(soundFontPath, FontInitFlags.Unicode | FontInitFlags.MemoryMap);
            if (exportFontHandle == 0)
            {
                throw CreateBassException("Failed to load export SoundFont");
            }

            ApplySoundFont(exportStreamHandle, exportFontHandle);

            var mixerState = new ChannelMixerState(masterVolumePercent, reverbReturnPercent, chorusReturnPercent);
            mixerState.SetChannelMixPercents(channelVolumePercents, channelReverbSendPercents, channelChorusSendPercents);
            ConfigureExportFilter(exportStreamHandle, systemMode, channelMuted, mixerState, out MidiFilterProcedure filter, out SyncProcedure resetSync);
            mixerState.ApplyAll(exportStreamHandle);

            WriteWaveFile(exportStreamHandle, options);
            GC.KeepAlive(resetSync);
            GC.KeepAlive(filter);
        }
        finally
        {
            if (exportStreamHandle != 0)
            {
                Bass.StreamFree(exportStreamHandle);
            }

            if (exportFontHandle != 0)
            {
                BassMidi.FontFree(exportFontHandle);
            }
        }
    }

    private static void WriteWaveFile(int streamHandle, WavExportOptions options)
    {
        const int ChannelCount = 2;
        const int FramesPerChunk = 16384;

        var directory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var floatBuffer = new float[FramesPerChunk * ChannelCount];
        var floatBytes = options.BitDepth == WavBitDepth.Float32 ? new byte[floatBuffer.Length * sizeof(float)] : null;
        var pcm16Bytes = options.BitDepth == WavBitDepth.Pcm16 ? new byte[floatBuffer.Length * sizeof(short)] : null;
        var pcm24Bytes = options.BitDepth == WavBitDepth.Pcm24 ? new byte[floatBuffer.Length * 3] : null;

        using var fileStream = File.Create(options.OutputPath);
        using var writer = new BinaryWriter(fileStream);

        WriteWaveHeader(writer, options.SampleRate, ChannelCount, options.BitDepth, 0);

        long dataLength = 0;

        while (true)
        {
            var bytesRequested = (floatBuffer.Length * sizeof(float)) | (int)DataFlags.Float;
            var bytesRead = Bass.ChannelGetData(streamHandle, floatBuffer, bytesRequested);

            if (bytesRead == -1)
            {
                if (Bass.LastError == Errors.Ended)
                {
                    break;
                }

                throw CreateBassException("Failed while rendering WAV data");
            }

            if (bytesRead == 0)
            {
                break;
            }

            var sampleCount = bytesRead / sizeof(float);
            if (sampleCount == 0)
            {
                break;
            }

            dataLength += WriteSamples(writer, floatBuffer, sampleCount, options.BitDepth, floatBytes, pcm16Bytes, pcm24Bytes);
        }

        FinalizeWaveHeader(writer, dataLength);
    }

    private static long WriteSamples(
        BinaryWriter writer,
        float[] source,
        int sampleCount,
        WavBitDepth bitDepth,
        byte[]? floatBytes,
        byte[]? pcm16Bytes,
        byte[]? pcm24Bytes)
    {
        switch (bitDepth)
        {
            case WavBitDepth.Pcm16:
            {
                if (pcm16Bytes is null)
                {
                    throw new InvalidOperationException("PCM 16-bit buffer was not initialized.");
                }

                int targetIndex = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    var sample = QuantizePcm16(source[i]);
                    pcm16Bytes[targetIndex++] = (byte)(sample & 0xFF);
                    pcm16Bytes[targetIndex++] = (byte)((sample >> 8) & 0xFF);
                }

                writer.Write(pcm16Bytes, 0, targetIndex);
                return targetIndex;
            }
            case WavBitDepth.Pcm24:
            {
                if (pcm24Bytes is null)
                {
                    throw new InvalidOperationException("PCM 24-bit buffer was not initialized.");
                }

                int targetIndex = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    var sample = QuantizePcm24(source[i]);
                    pcm24Bytes[targetIndex++] = (byte)(sample & 0xFF);
                    pcm24Bytes[targetIndex++] = (byte)((sample >> 8) & 0xFF);
                    pcm24Bytes[targetIndex++] = (byte)((sample >> 16) & 0xFF);
                }

                writer.Write(pcm24Bytes, 0, targetIndex);
                return targetIndex;
            }
            case WavBitDepth.Float32:
            {
                if (floatBytes is null)
                {
                    throw new InvalidOperationException("Float buffer was not initialized.");
                }

                var byteCount = sampleCount * sizeof(float);
                Buffer.BlockCopy(source, 0, floatBytes, 0, byteCount);
                writer.Write(floatBytes, 0, byteCount);
                return byteCount;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "Unsupported WAV bit depth.");
        }
    }

    private static void WriteWaveHeader(
        BinaryWriter writer,
        int sampleRate,
        int channelCount,
        WavBitDepth bitDepth,
        long dataLength)
    {
        var bitsPerSample = GetBitsPerSample(bitDepth);
        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = (short)(channelCount * bytesPerSample);
        var byteRate = sampleRate * blockAlign;
        var formatTag = bitDepth == WavBitDepth.Float32 ? 3 : 1;

        writer.Write("RIFF"u8.ToArray());
        writer.Write((int)(36 + dataLength));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)formatTag);
        writer.Write((short)channelCount);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write((int)dataLength);
    }

    private static void FinalizeWaveHeader(BinaryWriter writer, long dataLength)
    {
        writer.Flush();
        writer.Seek(4, SeekOrigin.Begin);
        writer.Write((int)(36 + dataLength));
        writer.Seek(40, SeekOrigin.Begin);
        writer.Write((int)dataLength);
        writer.Flush();
    }

    private static int GetBitsPerSample(WavBitDepth bitDepth)
        => bitDepth switch
        {
            WavBitDepth.Pcm16 => 16,
            WavBitDepth.Pcm24 => 24,
            WavBitDepth.Float32 => 32,
            _ => throw new ArgumentOutOfRangeException(nameof(bitDepth), bitDepth, "Unsupported WAV bit depth.")
        };

    private static short QuantizePcm16(float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        return clamped >= 0
            ? (short)Math.Round(clamped * short.MaxValue)
            : (short)Math.Round(clamped * 32768f);
    }

    private static int QuantizePcm24(float sample)
    {
        var clamped = Math.Clamp(sample, -1f, 1f);
        return clamped >= 0
            ? (int)Math.Round(clamped * 8388607f)
            : (int)Math.Round(clamped * 8388608f);
    }

    private sealed class ChannelMixerState
    {
        private readonly object _syncRoot = new();
        private readonly int[] _rawVolumeValues = new int[16];
        private readonly int[] _rawReverbValues = new int[16];
        private readonly int[] _rawChorusValues = new int[16];
        private readonly int[] _channelVolumePercents = new int[16];
        private readonly int[] _channelReverbSendPercents = new int[16];
        private readonly int[] _channelChorusSendPercents = new int[16];
        private int _masterVolumePercent;
        private int _reverbReturnPercent;
        private int _chorusReturnPercent;

        public ChannelMixerState()
            : this(DefaultMixPercent, DefaultMixPercent, DefaultMixPercent)
        {
        }

        public ChannelMixerState(int masterVolumePercent, int reverbReturnPercent, int chorusReturnPercent)
        {
            _masterVolumePercent = ClampMixPercent(masterVolumePercent);
            _reverbReturnPercent = ClampMixPercent(reverbReturnPercent);
            _chorusReturnPercent = ClampMixPercent(chorusReturnPercent);
            ResetTrackedValues();
            ResetUserMixSettings();
        }

        public void SetGlobalPercents(int masterVolumePercent, int reverbReturnPercent, int chorusReturnPercent)
        {
            lock (_syncRoot)
            {
                _masterVolumePercent = ClampMixPercent(masterVolumePercent);
                _reverbReturnPercent = ClampMixPercent(reverbReturnPercent);
                _chorusReturnPercent = ClampMixPercent(chorusReturnPercent);
            }
        }

        public void ResetTrackedValues()
        {
            lock (_syncRoot)
            {
                Array.Fill(_rawVolumeValues, DefaultChannelVolume);
                Array.Fill(_rawReverbValues, DefaultChannelReverb);
                Array.Fill(_rawChorusValues, DefaultChannelChorus);
            }
        }

        public int GetChannelVolumePercent(int channel)
        {
            if ((uint)channel >= 16)
            {
                return DefaultMixPercent;
            }

            lock (_syncRoot)
            {
                return _channelVolumePercents[channel];
            }
        }

        public int GetChannelReverbSendPercent(int channel)
        {
            if ((uint)channel >= 16)
            {
                return DefaultMixPercent;
            }

            lock (_syncRoot)
            {
                return _channelReverbSendPercents[channel];
            }
        }

        public int GetChannelChorusSendPercent(int channel)
        {
            if ((uint)channel >= 16)
            {
                return DefaultMixPercent;
            }

            lock (_syncRoot)
            {
                return _channelChorusSendPercents[channel];
            }
        }

        public int[] GetChannelVolumePercents()
        {
            lock (_syncRoot)
            {
                return [.. _channelVolumePercents];
            }
        }

        public int[] GetChannelReverbSendPercents()
        {
            lock (_syncRoot)
            {
                return [.. _channelReverbSendPercents];
            }
        }

        public int[] GetChannelChorusSendPercents()
        {
            lock (_syncRoot)
            {
                return [.. _channelChorusSendPercents];
            }
        }

        public bool SetChannelVolumePercent(int channel, int value)
            => SetChannelPercent(_channelVolumePercents, channel, value);

        public bool SetChannelReverbSendPercent(int channel, int value)
            => SetChannelPercent(_channelReverbSendPercents, channel, value);

        public bool SetChannelChorusSendPercent(int channel, int value)
            => SetChannelPercent(_channelChorusSendPercents, channel, value);

        public void SetChannelMixPercents(int[] channelVolumePercents, int[] channelReverbSendPercents, int[] channelChorusSendPercents)
        {
            lock (_syncRoot)
            {
                CopyPercents(channelVolumePercents, _channelVolumePercents);
                CopyPercents(channelReverbSendPercents, _channelReverbSendPercents);
                CopyPercents(channelChorusSendPercents, _channelChorusSendPercents);
            }
        }

        public bool TryFilterMixEvent(int streamHandle, MidiEvent midiEvent)
        {
            if ((uint)midiEvent.Channel >= 16)
            {
                return false;
            }

            ChannelMixState? pendingState = null;
            bool handled = false;
            lock (_syncRoot)
            {
                switch (midiEvent.EventType)
                {
                    case MidiEventType.Volume:
                        _rawVolumeValues[midiEvent.Channel] = ClampMidiValue(midiEvent.Parameter);
                        pendingState = CreateChannelMixState(midiEvent.Channel);
                        handled = true;
                        break;
                    case MidiEventType.Reverb:
                        _rawReverbValues[midiEvent.Channel] = ClampMidiValue(midiEvent.Parameter);
                        pendingState = CreateChannelMixState(midiEvent.Channel);
                        handled = true;
                        break;
                    case MidiEventType.Chorus:
                        _rawChorusValues[midiEvent.Channel] = ClampMidiValue(midiEvent.Parameter);
                        pendingState = CreateChannelMixState(midiEvent.Channel);
                        handled = true;
                        break;
                    case MidiEventType.Reset:
                        ResetTrackedChannel(midiEvent.Channel);
                        break;
                    case MidiEventType.Control:
                    {
                        int controller = midiEvent.Parameter & 0xFF;
                        int value = ClampMidiValue((midiEvent.Parameter >> 8) & 0xFF);
                        if (controller == 7)
                        {
                            _rawVolumeValues[midiEvent.Channel] = value;
                            pendingState = CreateChannelMixState(midiEvent.Channel);
                            handled = true;
                            break;
                        }

                        if (controller == 91)
                        {
                            _rawReverbValues[midiEvent.Channel] = value;
                            pendingState = CreateChannelMixState(midiEvent.Channel);
                            handled = true;
                            break;
                        }

                        if (controller == 93)
                        {
                            _rawChorusValues[midiEvent.Channel] = value;
                            pendingState = CreateChannelMixState(midiEvent.Channel);
                            handled = true;
                        }
                        break;
                    }
                    default:
                        break;
                }
            }

            ApplyChannel(streamHandle, pendingState);
            return handled;
        }

        public void HandleResetEvent(int streamHandle, int channel)
        {
            if ((uint)channel >= 16)
            {
                return;
            }

            ChannelMixState mixState;
            lock (_syncRoot)
            {
                ResetTrackedChannel(channel);
                mixState = CreateChannelMixState(channel);
            }

            ApplyChannel(streamHandle, mixState);
        }

        public void ApplyAll(int streamHandle)
        {
            if (streamHandle == 0)
            {
                return;
            }

            var mixStates = new ChannelMixState[16];
            lock (_syncRoot)
            {
                for (int channel = 0; channel < 16; channel++)
                {
                    mixStates[channel] = CreateChannelMixState(channel);
                }
            }

            for (int channel = 0; channel < mixStates.Length; channel++)
            {
                ApplyChannel(streamHandle, mixStates[channel]);
            }
        }

        public void ApplyChannel(int streamHandle, int channel)
        {
            if ((uint)channel >= 16)
            {
                return;
            }

            ChannelMixState mixState;
            lock (_syncRoot)
            {
                mixState = CreateChannelMixState(channel);
            }

            ApplyChannel(streamHandle, mixState);
        }

        private static void ApplyChannel(int streamHandle, ChannelMixState? mixState)
        {
            if (streamHandle == 0 || !mixState.HasValue)
            {
                return;
            }

            var value = mixState.Value;
            BassMidi.StreamEvent(streamHandle, value.Channel, MidiEventType.Volume, value.VolumeValue);
            BassMidi.StreamEvent(streamHandle, value.Channel, MidiEventType.Reverb, value.ReverbValue);
            BassMidi.StreamEvent(streamHandle, value.Channel, MidiEventType.Chorus, value.ChorusValue);
        }

        private void ResetTrackedChannel(int channel)
        {
            _rawVolumeValues[channel] = DefaultChannelVolume;
            _rawReverbValues[channel] = DefaultChannelReverb;
            _rawChorusValues[channel] = DefaultChannelChorus;
        }

        private void ResetUserMixSettings()
        {
            Array.Fill(_channelVolumePercents, DefaultMixPercent);
            Array.Fill(_channelReverbSendPercents, DefaultMixPercent);
            Array.Fill(_channelChorusSendPercents, DefaultMixPercent);
        }

        private static void CopyPercents(int[] source, int[] target)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = i < source.Length ? ClampMixPercent(source[i]) : DefaultMixPercent;
            }
        }

        private bool SetChannelPercent(int[] values, int channel, int value)
        {
            if ((uint)channel >= 16)
            {
                return false;
            }

            lock (_syncRoot)
            {
                int clampedValue = ClampMixPercent(value);
                if (values[channel] == clampedValue)
                {
                    return false;
                }

                values[channel] = clampedValue;
                return true;
            }
        }

        private ChannelMixState CreateChannelMixState(int channel)
            => new(
                channel,
                ScaleMidiValue(_rawVolumeValues[channel], _channelVolumePercents[channel], _masterVolumePercent),
                ScaleMidiValue(_rawReverbValues[channel], _channelReverbSendPercents[channel], _reverbReturnPercent),
                ScaleMidiValue(_rawChorusValues[channel], _channelChorusSendPercents[channel], _chorusReturnPercent));

        private static int ClampMidiValue(int value)
            => Math.Clamp(value, 0, 127);

        private static int ClampMixPercent(int value)
            => Math.Clamp(value, MinMixPercent, MaxMixPercent);

        private static int ScaleMidiValue(int value, int channelPercent, int globalPercent)
            => Math.Clamp((int)Math.Round(ClampMidiValue(value) * (channelPercent / 100d) * (globalPercent / 100d)), 0, 127);

        private readonly record struct ChannelMixState(int Channel, int VolumeValue, int ReverbValue, int ChorusValue);
    }

    private readonly record struct TempoPoint(long Tick, double Bpm);
}
