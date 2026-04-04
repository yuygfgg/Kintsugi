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
            _systemMode = value;
            if (_streamHandle != 0 && value != MidiSystem.Default)
            {
                BassMidi.StreamEvent(_streamHandle, 0, MidiEventType.System, (int)value);
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

        if (_systemMode != MidiSystem.Default)
        {
            BassMidi.StreamEvent(_streamHandle, 0, MidiEventType.System, (int)_systemMode);
        }

        RegisterMidiSyncs();
        BuildTempoMap();
    }

    private void RegisterMidiSyncs()
    {
        _syncProcs = new SyncProcedure[16];
        for (int ch = 0; ch < 16; ch++)
        {
            int chIndex = ch;
            int param = (int)MidiEventType.Note | (chIndex << 16);
            _syncProcs[chIndex] = (handle, channel, data, user) =>
            {
                int note = data & 0xFF;
                int velocity = (data >> 8) & 0xFF;
                if (note < 128)
                {
                    ActiveNotes[chIndex, note] = velocity > 0;
                    NotesChanged?.Invoke();
                }
            };
            Bass.ChannelSetSync(_streamHandle, SyncFlags.MidiEvent, param, _syncProcs[chIndex]);
        }

        _loopSyncProc = (_, _, _, _) =>
        {
            if (!_isLooping || _streamHandle == 0)
            {
                return;
            }

            Bass.ChannelSetPosition(_streamHandle, 0, PositionFlags.Bytes);
            ClearNotes();
        };

        if (Bass.ChannelSetSync(_streamHandle, SyncFlags.End | SyncFlags.Mixtime, 0, _loopSyncProc) == 0)
        {
            throw CreateBassException("Failed to register playback loop");
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

        ExportMidiToWav(MidiPath, SoundFontPath, options, _systemMode);
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

        if (!Bass.ChannelSetPosition(_streamHandle, bytePosition, PositionFlags.Bytes))
        {
            throw CreateBassException("Failed to seek");
        }
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
        MidiSystem systemMode)
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

            if (systemMode != MidiSystem.Default)
            {
                BassMidi.StreamEvent(exportStreamHandle, 0, MidiEventType.System, (int)systemMode);
            }

            WriteWaveFile(exportStreamHandle, options);
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

    private readonly record struct TempoPoint(long Tick, double Bpm);
}
