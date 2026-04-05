using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Enc;
using ManagedBass.Midi;

namespace MidiPlayer.App.Services;

public sealed class BassMidiPlayer : IDisposable
{
    private const int DefaultTempoMicrosecondsPerQuarterNote = 500000;
    private const double EqMinFrequency = 20.0;
    private const double EqMaxFrequency = 20000.0;
    private const int EqPeakBandOffset = 1;
    private const int EqPeakBandCount = 6;
    private const int EqMaxCutStages = 4;
    private const int EqChannelCount = 2;

    private int _streamHandle;
    private int _soundFontHandle;
    private int _eqDspHandle;
    private bool _bassInitialized;
    private bool _isLooping;
    private bool _isEqEnabled = true;
    private MidiSystem _systemMode = MidiSystem.Default;
    private int _sampleRate = 44100;
    private TempoPoint[] _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
    private MidiFilterProcedure? _midiFilter;
    private DSPProcedure? _eqDspProc;
    private readonly bool[] _channelMuted = new bool[16];
    private readonly bool[] _soloRestoreMuted = new bool[16];
    private readonly ChannelMixerState _channelMixer = new();
    private readonly object _eqLock = new();
    private readonly EqBandState[] _eqBands = CreateDefaultEqBands();
    private readonly StereoBiquadFilter[] _eqPeakFilters = CreateFilterArray(EqPeakBandCount);
    private readonly StereoBiquadFilter[] _eqLowCutFilters = CreateFilterArray(EqMaxCutStages);
    private readonly StereoBiquadFilter[] _eqHighCutFilters = CreateFilterArray(EqMaxCutStages);
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
    public const int MaxMidiControllerValue = 127;
    public const int DefaultMixPercent = 100;
    public const int MinMixPercent = 0;
    public const int MaxMixPercent = 200;
    public const int DefaultEffectScalePercent = DefaultMixPercent;
    public const int MinEffectScalePercent = MinMixPercent;
    public const int MaxEffectScalePercent = MaxMixPercent;
    public const int DefaultPlaybackSpeedPercent = 100;
    public const int MinPlaybackSpeedPercent = 25;
    public const int MaxPlaybackSpeedPercent = 400;
    public const int DefaultTransposeSemitones = 0;
    public const int MinTransposeSemitones = -24;
    public const int MaxTransposeSemitones = 24;
    public const int DefaultOpusBitrateKbps = 160;
    public const int MinOpusBitrateKbps = 6;
    public const int MaxOpusBitrateKbps = 510;
    public const int OpusExportSampleRate = 48000;
    public static bool SupportsCompressedAudioExport => !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64;

    public static bool IsExportFormatAvailable(AudioExportFormat format)
        => format == AudioExportFormat.Wav || SupportsCompressedAudioExport;

    private int _masterVolumePercent = DefaultMixPercent;
    private int _reverbReturnPercent = DefaultMixPercent;
    private int _chorusReturnPercent = DefaultMixPercent;
    private int _reverbReturnScalePercent = DefaultMixPercent;
    private int _chorusReturnScalePercent = DefaultMixPercent;
    private int _reverbReturnBiasValue = MidiMixSettings.DefaultBiasValue;
    private int _chorusReturnBiasValue = MidiMixSettings.DefaultBiasValue;
    private ChannelSendMode _reverbReturnMode = ChannelSendMode.Absolute;
    private ChannelSendMode _chorusReturnMode = ChannelSendMode.Absolute;
    private int _playbackSpeedPercent = DefaultPlaybackSpeedPercent;
    private int _transposeSemitones = DefaultTransposeSemitones;
    private MidiTimeBase _timeBase = MidiTimeBase.Default;
    private long _streamLengthTicks;

    public int PlaybackSpeedPercent
    {
        get => _playbackSpeedPercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinPlaybackSpeedPercent, MaxPlaybackSpeedPercent);
            if (_playbackSpeedPercent == clampedValue)
            {
                return;
            }

            _playbackSpeedPercent = clampedValue;
            ApplyPlaybackSpeed();
        }
    }

    public int TransposeSemitones
    {
        get => _transposeSemitones;
        set
        {
            var clampedValue = Math.Clamp(value, MinTransposeSemitones, MaxTransposeSemitones);
            if (_transposeSemitones == clampedValue)
            {
                return;
            }

            _transposeSemitones = clampedValue;
            ApplyTranspose();
        }
    }

    public int MasterVolumePercent
    {
        get => _masterVolumePercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_masterVolumePercent == clampedValue) return;
            _masterVolumePercent = clampedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
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
            ApplyGlobalMixSettings();
            ReapplyMixerState();
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
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public int ReverbReturnScalePercent
    {
        get => _reverbReturnScalePercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_reverbReturnScalePercent == clampedValue) return;
            _reverbReturnScalePercent = clampedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public int ChorusReturnScalePercent
    {
        get => _chorusReturnScalePercent;
        set
        {
            var clampedValue = Math.Clamp(value, MinMixPercent, MaxMixPercent);
            if (_chorusReturnScalePercent == clampedValue) return;
            _chorusReturnScalePercent = clampedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public int ReverbReturnBiasValue
    {
        get => _reverbReturnBiasValue;
        set
        {
            var clampedValue = Math.Clamp(value, -MaxMixPercent, MaxMixPercent);
            if (_reverbReturnBiasValue == clampedValue) return;
            _reverbReturnBiasValue = clampedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public int ChorusReturnBiasValue
    {
        get => _chorusReturnBiasValue;
        set
        {
            var clampedValue = Math.Clamp(value, -MaxMixPercent, MaxMixPercent);
            if (_chorusReturnBiasValue == clampedValue) return;
            _chorusReturnBiasValue = clampedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public ChannelSendMode ReverbReturnMode
    {
        get => _reverbReturnMode;
        set
        {
            var normalizedValue = NormalizeMode(value, ChannelSendMode.Absolute);
            if (_reverbReturnMode == normalizedValue) return;
            _reverbReturnMode = normalizedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public ChannelSendMode ChorusReturnMode
    {
        get => _chorusReturnMode;
        set
        {
            var normalizedValue = NormalizeMode(value, ChannelSendMode.Absolute);
            if (_chorusReturnMode == normalizedValue) return;
            _chorusReturnMode = normalizedValue;
            ApplyGlobalMixSettings();
            ReapplyMixerState();
        }
    }

    public int GetChannelVolumePercent(int channel)
        => _channelMixer.GetChannelVolumePercent(channel);

    public int GetChannelReverbSendPercent(int channel)
        => _channelMixer.GetChannelReverbSendPercent(channel);

    public int GetChannelChorusSendPercent(int channel)
        => _channelMixer.GetChannelChorusSendPercent(channel);

    public int GetChannelReverbSendAbsoluteValue(int channel)
        => _channelMixer.GetChannelReverbSendAbsoluteValue(channel);

    public int GetChannelChorusSendAbsoluteValue(int channel)
        => _channelMixer.GetChannelChorusSendAbsoluteValue(channel);

    public int GetChannelReverbSendBiasValue(int channel)
        => _channelMixer.GetChannelReverbSendBiasValue(channel);

    public int GetChannelChorusSendBiasValue(int channel)
        => _channelMixer.GetChannelChorusSendBiasValue(channel);

    public ChannelSendMode GetChannelReverbSendMode(int channel)
        => _channelMixer.GetChannelReverbSendMode(channel);

    public ChannelSendMode GetChannelChorusSendMode(int channel)
        => _channelMixer.GetChannelChorusSendMode(channel);

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

    public void SetChannelReverbSendAbsoluteValue(int channel, int value)
    {
        if (!_channelMixer.SetChannelReverbSendAbsoluteValue(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelChorusSendAbsoluteValue(int channel, int value)
    {
        if (!_channelMixer.SetChannelChorusSendAbsoluteValue(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelReverbSendBiasValue(int channel, int value)
    {
        if (!_channelMixer.SetChannelReverbSendBiasValue(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelChorusSendBiasValue(int channel, int value)
    {
        if (!_channelMixer.SetChannelChorusSendBiasValue(channel, value))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelReverbSendMode(int channel, ChannelSendMode mode)
    {
        if (!_channelMixer.SetChannelReverbSendMode(channel, mode))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public void SetChannelChorusSendMode(int channel, ChannelSendMode mode)
    {
        if (!_channelMixer.SetChannelChorusSendMode(channel, mode))
        {
            return;
        }

        _channelMixer.ApplyChannel(_streamHandle, channel);
    }

    public MidiMixSettings CaptureMixSettings()
        => _channelMixer.CreateMixSettings(
            _playbackSpeedPercent,
            _transposeSemitones,
            _masterVolumePercent,
            _reverbReturnMode,
            _reverbReturnScalePercent,
            _reverbReturnPercent,
            _reverbReturnBiasValue,
            _chorusReturnMode,
            _chorusReturnScalePercent,
            _chorusReturnPercent,
            _chorusReturnBiasValue);

    public void ApplyMixSettings(MidiMixSettings? settings)
    {
        var mixSettings = settings?.Clone() ?? new MidiMixSettings();
        mixSettings.Normalize();

        _playbackSpeedPercent = Math.Clamp(mixSettings.PlaybackSpeedPercent, MinPlaybackSpeedPercent, MaxPlaybackSpeedPercent);
        _transposeSemitones = Math.Clamp(mixSettings.PlaybackTransposeSemitones, MinTransposeSemitones, MaxTransposeSemitones);
        _masterVolumePercent = Math.Clamp(mixSettings.MasterVolumePercent, MinMixPercent, MaxMixPercent);
        _reverbReturnPercent = Math.Clamp(mixSettings.ReverbReturnPercent, MinMixPercent, MaxMixPercent);
        _chorusReturnPercent = Math.Clamp(mixSettings.ChorusReturnPercent, MinMixPercent, MaxMixPercent);
        _reverbReturnScalePercent = Math.Clamp(mixSettings.ReverbReturnScalePercent, MinMixPercent, MaxMixPercent);
        _chorusReturnScalePercent = Math.Clamp(mixSettings.ChorusReturnScalePercent, MinMixPercent, MaxMixPercent);
        _reverbReturnBiasValue = Math.Clamp(mixSettings.ReverbReturnBiasValue, -MaxMixPercent, MaxMixPercent);
        _chorusReturnBiasValue = Math.Clamp(mixSettings.ChorusReturnBiasValue, -MaxMixPercent, MaxMixPercent);
        _reverbReturnMode = NormalizeMode(mixSettings.ReverbReturnMode, ChannelSendMode.Absolute);
        _chorusReturnMode = NormalizeMode(mixSettings.ChorusReturnMode, ChannelSendMode.Absolute);

        ApplyGlobalMixSettings();
        ApplyPlaybackModifiers();
        _channelMixer.SetChannelMixSettings(
            mixSettings.ChannelVolumePercents,
            mixSettings.ChannelReverbSendPercents,
            mixSettings.ChannelChorusSendPercents,
            mixSettings.ChannelReverbSendModes,
            mixSettings.ChannelChorusSendModes,
            mixSettings.ChannelReverbSendAbsoluteValues,
            mixSettings.ChannelChorusSendAbsoluteValues,
            mixSettings.ChannelReverbSendBiasValues,
            mixSettings.ChannelChorusSendBiasValues);

        ReapplyMixerState();
    }

    private void ReapplyMixerState()
        => _channelMixer.ApplyAll(_streamHandle);

    private void ApplyGlobalMixSettings()
        => _channelMixer.SetGlobalMixSettings(
            _masterVolumePercent,
            _reverbReturnMode,
            _reverbReturnScalePercent,
            _reverbReturnPercent,
            _reverbReturnBiasValue,
            _chorusReturnMode,
            _chorusReturnScalePercent,
            _chorusReturnPercent,
            _chorusReturnBiasValue);

    private void ApplyPlaybackModifiers()
    {
        ApplyPlaybackSpeed();
        ApplyTranspose();
    }

    private void ApplyPlaybackSpeed()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        if (!BassMidi.StreamEvent(_streamHandle, 0, MidiEventType.Speed, _playbackSpeedPercent * 100))
        {
            throw CreateBassException("Failed to apply playback speed");
        }

        UpdateLoopRangeSync();
    }

    private void ApplyTranspose()
    {
        if (_streamHandle == 0)
        {
            return;
        }

        ApplyTranspose(_streamHandle, _transposeSemitones);
    }

    private static void ApplyPlaybackModifiers(int streamHandle, int playbackSpeedPercent, int transposeSemitones)
    {
        if (!BassMidi.StreamEvent(
            streamHandle,
            0,
            MidiEventType.Speed,
            Math.Clamp(playbackSpeedPercent, MinPlaybackSpeedPercent, MaxPlaybackSpeedPercent) * 100))
        {
            throw CreateBassException("Failed to apply export playback speed");
        }

        ApplyTranspose(streamHandle, transposeSemitones);
    }

    private static void ApplyTranspose(int streamHandle, int transposeSemitones)
    {
        int transposeValue = Math.Clamp(transposeSemitones, MinTransposeSemitones, MaxTransposeSemitones) + 100;
        for (int channel = 0; channel < 16; channel++)
        {
            if (!BassMidi.StreamEvent(streamHandle, channel, MidiEventType.Transpose, transposeValue))
            {
                throw CreateBassException("Failed to apply transpose");
            }
        }
    }

    private double GetPlaybackSpeedFactor()
        => Math.Max(_playbackSpeedPercent, MinPlaybackSpeedPercent) / 100d;

    private double TicksToSeconds(long ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }

        double speedFactor = GetPlaybackSpeedFactor();
        return _timeBase.Kind switch
        {
            MidiTimeBaseKind.Ppq => ConvertPpqTicksToSeconds(ticks, speedFactor),
            MidiTimeBaseKind.Smpte => ticks / (_timeBase.TicksPerSecond * speedFactor),
            _ => 0
        };
    }

    private long SecondsToTicks(double seconds)
    {
        if (seconds <= 0)
        {
            return 0;
        }

        double speedFactor = GetPlaybackSpeedFactor();
        long ticks = _timeBase.Kind switch
        {
            MidiTimeBaseKind.Ppq => ConvertSecondsToPpqTicks(seconds, speedFactor),
            MidiTimeBaseKind.Smpte => (long)Math.Round(seconds * _timeBase.TicksPerSecond * speedFactor),
            _ => 0
        };

        return Math.Clamp(ticks, 0, _streamLengthTicks);
    }

    private double ConvertPpqTicksToSeconds(long ticks, double speedFactor)
    {
        if (_timeBase.TicksPerQuarterNote <= 0)
        {
            return 0;
        }

        long boundedTicks = Math.Clamp(ticks, 0, _streamLengthTicks);
        double seconds = 0;
        for (int i = 0; i < _tempoMap.Length; i++)
        {
            long segmentStart = _tempoMap[i].Tick;
            if (boundedTicks <= segmentStart)
            {
                break;
            }

            long segmentEnd = i + 1 < _tempoMap.Length
                ? _tempoMap[i + 1].Tick
                : boundedTicks;
            segmentEnd = Math.Min(segmentEnd, boundedTicks);

            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            seconds += GetTempoSegmentSeconds(segmentEnd - segmentStart, _tempoMap[i].Bpm, speedFactor);
        }

        return seconds;
    }

    private long ConvertSecondsToPpqTicks(double seconds, double speedFactor)
    {
        if (_timeBase.TicksPerQuarterNote <= 0 || seconds <= 0)
        {
            return 0;
        }

        double remainingSeconds = seconds;
        for (int i = 0; i < _tempoMap.Length; i++)
        {
            long segmentStart = _tempoMap[i].Tick;
            long segmentEnd = i + 1 < _tempoMap.Length
                ? _tempoMap[i + 1].Tick
                : _streamLengthTicks;

            if (segmentEnd <= segmentStart)
            {
                continue;
            }

            long deltaTicks = segmentEnd - segmentStart;
            double segmentSeconds = GetTempoSegmentSeconds(deltaTicks, _tempoMap[i].Bpm, speedFactor);
            if (remainingSeconds <= segmentSeconds)
            {
                double ticksPerSecond = (_timeBase.TicksPerQuarterNote * _tempoMap[i].Bpm * speedFactor) / 60d;
                return segmentStart + (long)Math.Round(remainingSeconds * ticksPerSecond);
            }

            remainingSeconds -= segmentSeconds;
        }

        return _streamLengthTicks;
    }

    private double GetTempoSegmentSeconds(long deltaTicks, double bpm, double speedFactor)
        => deltaTicks <= 0 || bpm <= 0 || _timeBase.TicksPerQuarterNote <= 0
            ? 0
            : deltaTicks * 60d / (_timeBase.TicksPerQuarterNote * bpm * speedFactor);

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
    private SyncProcedure? _loopRangeSyncProc;
    private int _loopRangeSyncHandle;
    private bool _hasCustomLoopRange;
    private long _loopStartTicks;
    private long _loopEndTicks;
    private double _loopStartSeconds;
    private double _loopEndSeconds;

    public string? MidiPath { get; private set; }

    public string? SoundFontPath { get; private set; }

    public bool HasStream => _streamHandle != 0;

    public bool IsEqEnabled
    {
        get => _isEqEnabled;
        set
        {
            if (_isEqEnabled == value)
            {
                return;
            }

            _isEqEnabled = value;
            RebuildEqFilters(resetState: true);
        }
    }

    public void SetEqBand(int index, double frequency, double gainDb, double q, int slopeDbPerOct)
    {
        if ((uint)index >= _eqBands.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var band = _eqBands[index];
        band.Frequency = Math.Clamp(frequency, EqMinFrequency, EqMaxFrequency);
        band.GainDb = Math.Clamp(gainDb, -24.0, 24.0);
        band.Q = Math.Clamp(q, 0.0, 2.5);
        band.SlopeDbPerOct = Math.Clamp(slopeDbPerOct, 0, 48);
        RebuildEqFilters(resetState: false);
    }

    public bool IsPlaying => HasStream && Bass.ChannelIsActive(_streamHandle) == PlaybackState.Playing;

    public string GetChannelInstrumentName(int channel)
    {
        if ((uint)channel >= 16 || _streamHandle == 0)
        {
            return string.Empty;
        }

        int program = Math.Max(0, GetCurrentChannelEventValue(channel, MidiEventType.Program, 0));
        int bank = Math.Max(0, GetCurrentChannelEventValue(channel, MidiEventType.Bank, 0));
        int bankLsb = Math.Max(0, GetCurrentChannelEventValue(channel, MidiEventType.BankLSB, 0));
        bool drums = GetCurrentChannelEventValue(channel, MidiEventType.Drums, channel == 9 ? 1 : 0) != 0;

        return drums
            ? GetDrumKitName(program, bank, bankLsb)
            : GetMelodicInstrumentName(program, bank, bankLsb);
    }

    public bool IsLooping
    {
        get => _isLooping;
        set => _isLooping = value;
    }

    public bool HasCustomLoopRange => _hasCustomLoopRange;

    public double GetLoopStartSeconds()
        => _hasCustomLoopRange
            ? GetStoredLoopStartSeconds()
            : 0;

    public double GetLoopEndSeconds()
        => _hasCustomLoopRange
            ? GetStoredLoopEndSeconds()
            : GetDurationSeconds();

    public void SetLoopRange(double startSeconds, double endSeconds)
    {
        double duration = GetDurationSeconds();
        if (_streamHandle == 0 || duration <= 0)
        {
            ClearLoopRange();
            return;
        }

        double boundedStart = Math.Clamp(Math.Min(startSeconds, endSeconds), 0, duration);
        double boundedEnd = Math.Clamp(Math.Max(startSeconds, endSeconds), 0, duration);
        if (boundedEnd - boundedStart <= 0)
        {
            ClearLoopRange();
            return;
        }

        _hasCustomLoopRange = true;
        _loopStartSeconds = boundedStart;
        _loopEndSeconds = boundedEnd;
        _loopStartTicks = _streamLengthTicks > 0 ? SecondsToTicks(boundedStart) : 0;
        _loopEndTicks = _streamLengthTicks > 0 ? SecondsToTicks(boundedEnd) : 0;

        if (_streamLengthTicks > 0 && _loopEndTicks <= _loopStartTicks)
        {
            _loopEndTicks = Math.Min(_streamLengthTicks, _loopStartTicks + 1);
        }

        UpdateLoopRangeSync();
    }

    public void ClearLoopRange()
    {
        _hasCustomLoopRange = false;
        _loopStartTicks = 0;
        _loopEndTicks = 0;
        _loopStartSeconds = 0;
        _loopEndSeconds = 0;
        UpdateLoopRangeSync();
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
        _timeBase = MidiTimeBase.Load(path);
        BuildTempoMap();
        _streamLengthTicks = Math.Max(0, Bass.ChannelGetLength(_streamHandle, PositionFlags.MIDITick));

        _channelMixer.ResetTrackedValues();
        ApplyPlaybackModifiers();
        ReapplyMixerState();
        ConfigureEqDsp();
    }

    private void RegisterMidiSyncs()
    {
        _syncProcs = new SyncProcedure[8];
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
            NotesChanged?.Invoke();
        };
        RegisterMidiEventSync(MidiEventType.Reset, _syncProcs[3]);

        _syncProcs[4] = (_, _, _, _) => NotesChanged?.Invoke();
        RegisterMidiEventSync(MidiEventType.Program, _syncProcs[4]);

        _syncProcs[5] = (_, _, _, _) => NotesChanged?.Invoke();
        RegisterMidiEventSync(MidiEventType.Bank, _syncProcs[5]);

        _syncProcs[6] = (_, _, _, _) => NotesChanged?.Invoke();
        RegisterMidiEventSync(MidiEventType.BankLSB, _syncProcs[6]);

        _syncProcs[7] = (_, _, _, _) => NotesChanged?.Invoke();
        RegisterMidiEventSync(MidiEventType.Drums, _syncProcs[7]);

        _loopSyncProc = (_, _, _, _) =>
        {
            if (!_isLooping || _streamHandle == 0)
            {
                return;
            }

            SeekToLoopStartInternal();
        };

        if (Bass.ChannelSetSync(_streamHandle, SyncFlags.End | SyncFlags.Mixtime, 0, _loopSyncProc) == 0)
        {
            throw CreateBassException("Failed to register playback loop");
        }

        _loopRangeSyncProc = (_, _, _, _) =>
        {
            if (!_isLooping || _streamHandle == 0 || !_hasCustomLoopRange)
            {
                return;
            }

            SeekToLoopStartInternal();
        };

        UpdateLoopRangeSync();
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

    private int GetCurrentChannelEventValue(int channel, MidiEventType eventType, int fallbackValue)
    {
        int value = BassMidi.StreamGetEvent(_streamHandle, channel, eventType);
        return value >= 0 ? value : fallbackValue;
    }

    private static string GetMelodicInstrumentName(int program, int bank, int bankLsb)
    {
        string instrumentName = MidiStandardNames.GetProgramName(program);

        return AppendBankSuffix(instrumentName, bank, bankLsb);
    }

    private static string GetDrumKitName(int program, int bank, int bankLsb)
    {
        string instrumentName = program switch
        {
            0 => "Standard Drum Kit",
            8 => "Room Drum Kit",
            16 => "Power Drum Kit",
            24 => "Electronic Drum Kit",
            25 => "TR-808 Drum Kit",
            32 => "Jazz Drum Kit",
            40 => "Brush Drum Kit",
            48 => "Orchestra Drum Kit",
            56 => "Sound FX Drum Kit",
            _ => $"Drum Kit {program}"
        };

        return AppendBankSuffix(instrumentName, bank, bankLsb);
    }

    private static string AppendBankSuffix(string instrumentName, int bank, int bankLsb)
    {
        if (bank <= 0 && bankLsb <= 0)
        {
            return instrumentName;
        }

        return bankLsb <= 0
            ? $"{instrumentName} (Bank {bank})"
            : $"{instrumentName} (Bank {bank}:{bankLsb})";
    }

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

    public void ExportCurrentMidiToAudio(AudioExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options = NormalizeExportOptions(options);
        var eqSettings = CaptureEqExportSettings();
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

        if (!IsExportFormatAvailable(options.Format))
        {
            throw new ArgumentException("Selected export format is unavailable.", nameof(options));
        }

        if (options.Format == AudioExportFormat.Flac && options.BitDepth == AudioBitDepth.Float32)
        {
            throw new ArgumentException("FLAC export only supports 16-bit PCM or 24-bit PCM.", nameof(options));
        }

        if (options.Format == AudioExportFormat.Opus &&
            (options.OpusBitrateKbps < MinOpusBitrateKbps || options.OpusBitrateKbps > MaxOpusBitrateKbps))
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"Opus bitrate must be between {MinOpusBitrateKbps} and {MaxOpusBitrateKbps} kbps.");
        }

        ExportMidiToAudio(
            MidiPath,
            SoundFontPath,
            options,
            _systemMode,
            _channelMuted,
            CaptureMixSettings(),
            eqSettings);
    }

    private static AudioExportOptions NormalizeExportOptions(AudioExportOptions options)
        => options.Format == AudioExportFormat.Opus && options.SampleRate != OpusExportSampleRate
            ? options with { SampleRate = OpusExportSampleRate }
            : options;

    private EqExportSettings CaptureEqExportSettings()
    {
        lock (_eqLock)
        {
            return new EqExportSettings(
                _isEqEnabled,
                _eqBands.Select(band => new EqBandState(band.Frequency, band.GainDb, band.Q, band.SlopeDbPerOct)).ToArray());
        }
    }

    public void Play()
    {
        EnsureStreamLoaded();

        if (_soundFontHandle == 0)
        {
            throw new InvalidOperationException("Please load a SoundFont before playing.");
        }

        if (_isLooping && _hasCustomLoopRange)
        {
            double loopStart = GetLoopStartSeconds();
            double loopEnd = GetLoopEndSeconds();
            double currentPosition = GetPositionSeconds();
            if (currentPosition < loopStart || currentPosition >= Math.Max(loopStart, loopEnd - 0.01))
            {
                Seek(loopStart);
            }
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

        SetPlaybackPosition(seconds);
        ReapplySeekState();
    }

    public double GetDurationSeconds()
    {
        if (_streamHandle == 0)
        {
            return 0;
        }

        if (_streamLengthTicks > 0)
        {
            return TicksToSeconds(_streamLengthTicks);
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

        var positionInTicks = Bass.ChannelGetPosition(_streamHandle, PositionFlags.MIDITick);
        if (positionInTicks > 0)
        {
            return TicksToSeconds(positionInTicks);
        }

        var positionInBytes = Bass.ChannelGetPosition(_streamHandle, PositionFlags.Bytes);
        return positionInBytes <= 0
            ? 0
            : Bass.ChannelBytes2Seconds(_streamHandle, positionInBytes);
    }

    public long GetPositionTicks()
    {
        if (_streamHandle == 0)
        {
            return 0;
        }

        long positionInTicks = Bass.ChannelGetPosition(_streamHandle, PositionFlags.MIDITick);
        if (positionInTicks >= 0)
        {
            return Math.Clamp(positionInTicks, 0, _streamLengthTicks);
        }

        long positionInBytes = Bass.ChannelGetPosition(_streamHandle, PositionFlags.Bytes);
        if (positionInBytes <= 0)
        {
            return 0;
        }

        double positionInSeconds = Bass.ChannelBytes2Seconds(_streamHandle, positionInBytes);
        return SecondsToTicks(positionInSeconds);
    }

    public double GetCurrentBpm()
    {
        if (_streamHandle == 0 || _tempoMap.Length == 0 || _timeBase.Kind != MidiTimeBaseKind.Ppq)
        {
            return 0;
        }

        var tickPosition = Bass.ChannelGetPosition(_streamHandle, PositionFlags.MIDITick);
        if (tickPosition < 0)
        {
            return _tempoMap[0].Bpm * GetPlaybackSpeedFactor();
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

        return _tempoMap[low].Bpm * GetPlaybackSpeedFactor();
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

        Bass.FloatingPointDSP = true;
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

    private void ConfigureEqDsp()
    {
        RemoveEqDsp();
        if (_streamHandle == 0)
        {
            return;
        }

        _eqDspProc ??= ProcessEqDsp;
        _eqDspHandle = Bass.ChannelSetDSP(_streamHandle, _eqDspProc, IntPtr.Zero, 0);
        if (_eqDspHandle == 0)
        {
            throw CreateBassException("Failed to install EQ DSP");
        }

        RebuildEqFilters(resetState: true);
    }

    private void RemoveEqDsp()
    {
        if (_streamHandle != 0 && _eqDspHandle != 0)
        {
            Bass.ChannelRemoveDSP(_streamHandle, _eqDspHandle);
        }

        _eqDspHandle = 0;
        ResetEqFilterStates();
    }

    private void RebuildEqFilters(bool resetState)
    {
        lock (_eqLock)
        {
            foreach (var filter in _eqPeakFilters)
            {
                filter.Disable();
            }

            foreach (var filter in _eqLowCutFilters)
            {
                filter.Disable();
            }

            foreach (var filter in _eqHighCutFilters)
            {
                filter.Disable();
            }

            if (_isEqEnabled)
            {
                for (var i = 0; i < EqPeakBandCount; i++)
                {
                    var band = _eqBands[EqPeakBandOffset + i];
                    if (Math.Abs(band.GainDb) < 0.01)
                    {
                        continue;
                    }

                    _eqPeakFilters[i].SetPeak(
                        _sampleRate,
                        band.Frequency,
                        band.GainDb,
                        MapUiQToFilterQ(band.Q));
                }

                ConfigureCutFilters(_eqBands[0], isHighPass: true, _eqLowCutFilters);
                ConfigureCutFilters(_eqBands[^1], isHighPass: false, _eqHighCutFilters);
            }

            if (resetState)
            {
                ResetEqFilterStatesUnsafe();
            }
        }
    }

    private void ConfigureCutFilters(EqBandState band, bool isHighPass, StereoBiquadFilter[] filters)
    {
        var stageCount = GetCutStageCount(band.SlopeDbPerOct);
        for (var stage = 0; stage < stageCount && stage < filters.Length; stage++)
        {
            if (isHighPass)
            {
                filters[stage].SetHighPass(_sampleRate, band.Frequency, 0.7071067811865476);
            }
            else
            {
                filters[stage].SetLowPass(_sampleRate, band.Frequency, 0.7071067811865476);
            }
        }
    }

    private void ResetEqFilterStates()
    {
        lock (_eqLock)
        {
            ResetEqFilterStatesUnsafe();
        }
    }

    private void ResetEqFilterStatesUnsafe()
    {
        foreach (var filter in _eqPeakFilters)
        {
            filter.Reset();
        }

        foreach (var filter in _eqLowCutFilters)
        {
            filter.Reset();
        }

        foreach (var filter in _eqHighCutFilters)
        {
            filter.Reset();
        }
    }

    private unsafe void ProcessEqDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (!_isEqEnabled || length <= 0)
        {
            return;
        }

        var sampleCount = length / sizeof(float);
        if (sampleCount < EqChannelCount)
        {
            return;
        }

        var samples = (float*)buffer;
        lock (_eqLock)
        {
            for (var index = 0; index + 1 < sampleCount; index += EqChannelCount)
            {
                var left = samples[index];
                var right = samples[index + 1];

                foreach (var filter in _eqLowCutFilters)
                {
                    filter.Process(ref left, ref right);
                }

                foreach (var filter in _eqPeakFilters)
                {
                    filter.Process(ref left, ref right);
                }

                foreach (var filter in _eqHighCutFilters)
                {
                    filter.Process(ref left, ref right);
                }

                samples[index] = left;
                samples[index + 1] = right;
            }
        }
    }

    private static double MapUiQToFilterQ(double value)
        => Math.Clamp(0.5 + value * 3.5, 0.5, 10.0);

    private static int GetCutStageCount(int slopeDbPerOct)
        => slopeDbPerOct switch
        {
            <= 0 => 0,
            <= 12 => 1,
            <= 24 => 2,
            <= 36 => 3,
            _ => 4
        };

    private static void ConfigureCutFilters(int sampleRate, EqBandState band, bool isHighPass, StereoBiquadFilter[] filters)
    {
        var stageCount = GetCutStageCount(band.SlopeDbPerOct);
        for (var stage = 0; stage < stageCount && stage < filters.Length; stage++)
        {
            if (isHighPass)
            {
                filters[stage].SetHighPass(sampleRate, band.Frequency, 0.7071067811865476);
            }
            else
            {
                filters[stage].SetLowPass(sampleRate, band.Frequency, 0.7071067811865476);
            }
        }
    }

    private static EqBandState[] CreateDefaultEqBands()
    {
        return
        [
            new EqBandState(EqMinFrequency, 0.0, 0.0, 0),
            new EqBandState(75.0, 0.0, 0.0, 0),
            new EqBandState(100.0, 0.0, 0.0, 0),
            new EqBandState(250.0, 0.0, 0.0, 0),
            new EqBandState(1040.0, 0.0, 0.0, 0),
            new EqBandState(2460.0, 0.0, 0.0, 0),
            new EqBandState(7500.0, 0.0, 0.0, 0),
            new EqBandState(EqMaxFrequency, 0.0, 0.0, 0)
        ];
    }

    private static StereoBiquadFilter[] CreateFilterArray(int count)
    {
        var filters = new StereoBiquadFilter[count];
        for (var i = 0; i < count; i++)
        {
            filters[i] = new StereoBiquadFilter();
        }

        return filters;
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

        RemoveEqDsp();
        _syncProcs = null;
        _loopSyncProc = null;
        RemoveLoopRangeSync();
        _loopRangeSyncProc = null;
        _hasCustomLoopRange = false;
        _loopStartTicks = 0;
        _loopEndTicks = 0;
        _loopStartSeconds = 0;
        _loopEndSeconds = 0;
        _tempoMap = [new(0, 60_000_000d / DefaultTempoMicrosecondsPerQuarterNote)];
        _timeBase = MidiTimeBase.Default;
        _streamLengthTicks = 0;
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

    private double GetStoredLoopStartSeconds()
        => _streamLengthTicks > 0
            ? TicksToSeconds(_loopStartTicks)
            : _loopStartSeconds;

    private double GetStoredLoopEndSeconds()
        => _streamLengthTicks > 0
            ? TicksToSeconds(_loopEndTicks)
            : _loopEndSeconds;

    private void SeekToLoopStartInternal()
    {
        SetPlaybackPosition(_hasCustomLoopRange ? GetStoredLoopStartSeconds() : 0);
        ReapplySeekState();
    }

    private void SetPlaybackPosition(double seconds)
    {
        var boundedSeconds = Math.Clamp(seconds, 0, GetDurationSeconds());
        _channelMixer.ResetTrackedValues();
        bool positioned = false;
        if (_streamLengthTicks > 0)
        {
            var tickPosition = SecondsToTicks(boundedSeconds);
            positioned = Bass.ChannelSetPosition(_streamHandle, tickPosition, PositionFlags.MIDITick);
        }

        if (!positioned)
        {
            var bytePosition = Bass.ChannelSeconds2Bytes(_streamHandle, boundedSeconds);
            if (!Bass.ChannelSetPosition(_streamHandle, bytePosition, PositionFlags.Bytes))
            {
                throw CreateBassException("Failed to seek");
            }
        }
    }

    private void ReapplySeekState()
    {
        ReapplySystemModeOverride();
        ApplyPlaybackModifiers();
        ReapplyMixerState();
        ResetEqFilterStates();
        ClearNotes();
    }

    private void UpdateLoopRangeSync()
    {
        if (_streamHandle == 0)
        {
            _loopRangeSyncHandle = 0;
            return;
        }

        RemoveLoopRangeSync();
        if (!_hasCustomLoopRange || _loopRangeSyncProc is null)
        {
            return;
        }

        long loopEndBytes = Bass.ChannelSeconds2Bytes(_streamHandle, GetStoredLoopEndSeconds());
        if (loopEndBytes <= 0)
        {
            return;
        }

        _loopRangeSyncHandle = Bass.ChannelSetSync(
            _streamHandle,
            SyncFlags.Position | SyncFlags.Mixtime,
            loopEndBytes,
            _loopRangeSyncProc);
        if (_loopRangeSyncHandle == 0)
        {
            throw CreateBassException("Failed to register loop range");
        }
    }

    private void RemoveLoopRangeSync()
    {
        if (_streamHandle == 0 || _loopRangeSyncHandle == 0)
        {
            _loopRangeSyncHandle = 0;
            return;
        }

        Bass.ChannelRemoveSync(_streamHandle, _loopRangeSyncHandle);
        _loopRangeSyncHandle = 0;
    }

    private static ChannelSendMode NormalizeMode(ChannelSendMode mode, ChannelSendMode defaultMode)
        => mode switch
        {
            ChannelSendMode.Scale => ChannelSendMode.Scale,
            ChannelSendMode.Absolute => ChannelSendMode.Absolute,
            ChannelSendMode.Bias => ChannelSendMode.Bias,
            _ => defaultMode
        };

    private static void ExportMidiToAudio(
        string midiPath,
        string soundFontPath,
        AudioExportOptions options,
        MidiSystem systemMode,
        bool[]? channelMuted,
        MidiMixSettings mixSettings,
        EqExportSettings eqSettings)
    {
        int exportStreamHandle = 0;
        int exportFontHandle = 0;
        ExportEqProcessor? exportEq = null;

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

            mixSettings.Normalize();
            var mixerState = new ChannelMixerState();
            mixerState.SetGlobalMixSettings(
                mixSettings.MasterVolumePercent,
                mixSettings.ReverbReturnMode,
                mixSettings.ReverbReturnScalePercent,
                mixSettings.ReverbReturnPercent,
                mixSettings.ReverbReturnBiasValue,
                mixSettings.ChorusReturnMode,
                mixSettings.ChorusReturnScalePercent,
                mixSettings.ChorusReturnPercent,
                mixSettings.ChorusReturnBiasValue);
            mixerState.SetChannelMixSettings(
                mixSettings.ChannelVolumePercents,
                mixSettings.ChannelReverbSendPercents,
                mixSettings.ChannelChorusSendPercents,
                mixSettings.ChannelReverbSendModes,
                mixSettings.ChannelChorusSendModes,
                mixSettings.ChannelReverbSendAbsoluteValues,
                mixSettings.ChannelChorusSendAbsoluteValues,
                mixSettings.ChannelReverbSendBiasValues,
                mixSettings.ChannelChorusSendBiasValues);
            ConfigureExportFilter(exportStreamHandle, systemMode, channelMuted, mixerState, out MidiFilterProcedure filter, out SyncProcedure resetSync);
            ApplyPlaybackModifiers(exportStreamHandle, mixSettings.PlaybackSpeedPercent, mixSettings.PlaybackTransposeSemitones);
            mixerState.ApplyAll(exportStreamHandle);
            exportEq = new ExportEqProcessor(eqSettings, options.SampleRate, exportStreamHandle);

            WriteExportFile(exportStreamHandle, options);
            GC.KeepAlive(resetSync);
            GC.KeepAlive(filter);
        }
        finally
        {
            exportEq?.Dispose();

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

    private static void WriteExportFile(int streamHandle, AudioExportOptions options)
    {
        switch (options.Format)
        {
            case AudioExportFormat.Wav:
                WriteWaveFile(streamHandle, options);
                return;
            case AudioExportFormat.Flac:
                WriteEncodedFile(streamHandle, options, "FLAC", StartFlacEncoder);
                return;
            case AudioExportFormat.Opus:
                WriteEncodedFile(streamHandle, options, "Opus", StartOpusEncoder);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Format, "Unsupported export format.");
        }
    }

    private static void WriteWaveFile(int streamHandle, AudioExportOptions options)
    {
        const int ChannelCount = 2;
        const int FramesPerChunk = 16384;

        var directory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var floatBuffer = new float[FramesPerChunk * ChannelCount];
        var floatBytes = options.BitDepth == AudioBitDepth.Float32 ? new byte[floatBuffer.Length * sizeof(float)] : null;
        var pcm16Bytes = options.BitDepth == AudioBitDepth.Pcm16 ? new byte[floatBuffer.Length * sizeof(short)] : null;
        var pcm24Bytes = options.BitDepth == AudioBitDepth.Pcm24 ? new byte[floatBuffer.Length * 3] : null;

        using var fileStream = File.Create(options.OutputPath);
        using var writer = new BinaryWriter(fileStream);

        WriteWaveHeader(writer, options.SampleRate, ChannelCount, options.BitDepth, 0);

        long dataLength = 0;

        DrainDecodedStream(
            streamHandle,
            floatBuffer,
            bytesRead =>
            {
                var sampleCount = bytesRead / sizeof(float);
                if (sampleCount == 0)
                {
                    return;
                }

                dataLength += WriteSamples(writer, floatBuffer, sampleCount, options.BitDepth, floatBytes, pcm16Bytes, pcm24Bytes);
            },
            "Failed while rendering WAV data");

        FinalizeWaveHeader(writer, dataLength);
    }

    private static void WriteEncodedFile(
        int streamHandle,
        AudioExportOptions options,
        string formatLabel,
        Func<int, AudioExportOptions, int> startEncoder)
    {
        const int ChannelCount = 2;
        const int FramesPerChunk = 16384;

        var directory = Path.GetDirectoryName(options.OutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var floatBuffer = new float[FramesPerChunk * ChannelCount];
        int encoderHandle = 0;

        try
        {
            try
            {
                encoderHandle = startEncoder(streamHandle, options);
            }
            catch (DllNotFoundException ex)
            {
                throw new InvalidOperationException("Selected export format is unavailable.", ex);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new InvalidOperationException("Selected export format is unavailable.", ex);
            }

            if (encoderHandle == 0)
            {
                throw CreateBassException($"Failed to start {formatLabel} encoder");
            }

            DrainDecodedStream(streamHandle, floatBuffer, _ => { }, $"Failed while rendering {formatLabel} data");

            if (!BassEnc.EncodeStop(encoderHandle))
            {
                throw CreateBassException($"Failed to finalize {formatLabel} file");
            }

            encoderHandle = 0;
        }
        finally
        {
            if (encoderHandle != 0)
            {
                BassEnc.EncodeStop(encoderHandle);
            }
        }
    }

    private static int StartFlacEncoder(int streamHandle, AudioExportOptions options)
    {
        var flags = options.BitDepth switch
        {
            AudioBitDepth.Pcm16 => EncodeFlags.ConvertFloatTo16BitInt,
            AudioBitDepth.Pcm24 => EncodeFlags.ConvertFloatTo24Bit,
            _ => throw new ArgumentOutOfRangeException(nameof(options), options.BitDepth, "FLAC export only supports 16-bit PCM or 24-bit PCM.")
        };

        return BassEnc_Flac.Start(streamHandle, string.Empty, flags, options.OutputPath);
    }

    private static int StartOpusEncoder(int streamHandle, AudioExportOptions options)
    {
        var flags = EncodeFlags.ConvertFloatTo16BitInt;
        var encoderOptions = $"--bitrate {options.OpusBitrateKbps}";
        return BassEnc_Opus.Start(streamHandle, encoderOptions, flags, options.OutputPath);
    }

    private static void DrainDecodedStream(
        int streamHandle,
        float[] floatBuffer,
        Action<int> onChunkDecoded,
        string errorMessage)
    {
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

                throw CreateBassException(errorMessage);
            }

            if (bytesRead == 0)
            {
                break;
            }

            onChunkDecoded(bytesRead);
        }
    }

    private static long WriteSamples(
        BinaryWriter writer,
        float[] source,
        int sampleCount,
        AudioBitDepth bitDepth,
        byte[]? floatBytes,
        byte[]? pcm16Bytes,
        byte[]? pcm24Bytes)
    {
        switch (bitDepth)
        {
            case AudioBitDepth.Pcm16:
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
            case AudioBitDepth.Pcm24:
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
            case AudioBitDepth.Float32:
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
        AudioBitDepth bitDepth,
        long dataLength)
    {
        var bitsPerSample = GetBitsPerSample(bitDepth);
        var bytesPerSample = bitsPerSample / 8;
        var blockAlign = (short)(channelCount * bytesPerSample);
        var byteRate = sampleRate * blockAlign;
        var formatTag = bitDepth == AudioBitDepth.Float32 ? 3 : 1;

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

    private static int GetBitsPerSample(AudioBitDepth bitDepth)
        => bitDepth switch
        {
            AudioBitDepth.Pcm16 => 16,
            AudioBitDepth.Pcm24 => 24,
            AudioBitDepth.Float32 => 32,
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
        private int _rawReverbReturnValue;
        private int _rawChorusReturnValue;
        private readonly int[] _channelVolumePercents = new int[16];
        private readonly int[] _channelReverbSendPercents = new int[16];
        private readonly int[] _channelChorusSendPercents = new int[16];
        private readonly int[] _channelReverbSendAbsoluteValues = new int[16];
        private readonly int[] _channelChorusSendAbsoluteValues = new int[16];
        private readonly int[] _channelReverbSendBiasValues = new int[16];
        private readonly int[] _channelChorusSendBiasValues = new int[16];
        private readonly ChannelSendMode[] _channelReverbSendModes = new ChannelSendMode[16];
        private readonly ChannelSendMode[] _channelChorusSendModes = new ChannelSendMode[16];
        private int _masterVolumePercent;
        private int _reverbReturnScalePercent;
        private int _chorusReturnScalePercent;
        private int _reverbReturnAbsoluteValue;
        private int _chorusReturnAbsoluteValue;
        private int _reverbReturnBiasValue;
        private int _chorusReturnBiasValue;
        private ChannelSendMode _reverbReturnMode;
        private ChannelSendMode _chorusReturnMode;

        public ChannelMixerState()
            : this(DefaultMixPercent)
        {
        }

        public ChannelMixerState(int masterVolumePercent)
        {
            _masterVolumePercent = ClampMixPercent(masterVolumePercent);
            _reverbReturnScalePercent = DefaultMixPercent;
            _chorusReturnScalePercent = DefaultMixPercent;
            _reverbReturnAbsoluteValue = DefaultMixPercent;
            _chorusReturnAbsoluteValue = DefaultMixPercent;
            _reverbReturnBiasValue = MidiMixSettings.DefaultBiasValue;
            _chorusReturnBiasValue = MidiMixSettings.DefaultBiasValue;
            _reverbReturnMode = ChannelSendMode.Absolute;
            _chorusReturnMode = ChannelSendMode.Absolute;
            ResetTrackedValues();
            ResetUserMixSettings();
        }

        public void SetGlobalMixSettings(
            int masterVolumePercent,
            ChannelSendMode reverbReturnMode,
            int reverbReturnScalePercent,
            int reverbReturnAbsoluteValue,
            int reverbReturnBiasValue,
            ChannelSendMode chorusReturnMode,
            int chorusReturnScalePercent,
            int chorusReturnAbsoluteValue,
            int chorusReturnBiasValue)
        {
            lock (_syncRoot)
            {
                _masterVolumePercent = ClampMixPercent(masterVolumePercent);
                _reverbReturnMode = NormalizeMode(reverbReturnMode, ChannelSendMode.Absolute);
                _chorusReturnMode = NormalizeMode(chorusReturnMode, ChannelSendMode.Absolute);
                _reverbReturnScalePercent = ClampMixPercent(reverbReturnScalePercent);
                _chorusReturnScalePercent = ClampMixPercent(chorusReturnScalePercent);
                _reverbReturnAbsoluteValue = ClampMixPercent(reverbReturnAbsoluteValue);
                _chorusReturnAbsoluteValue = ClampMixPercent(chorusReturnAbsoluteValue);
                _reverbReturnBiasValue = ClampSignedValue(reverbReturnBiasValue, MaxMixPercent);
                _chorusReturnBiasValue = ClampSignedValue(chorusReturnBiasValue, MaxMixPercent);
            }
        }

        public void ResetTrackedValues()
        {
            lock (_syncRoot)
            {
                Array.Fill(_rawVolumeValues, DefaultChannelVolume);
                Array.Fill(_rawReverbValues, DefaultChannelReverb);
                Array.Fill(_rawChorusValues, DefaultChannelChorus);
                _rawReverbReturnValue = DefaultMixPercent;
                _rawChorusReturnValue = DefaultMixPercent;
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

        public int GetChannelReverbSendAbsoluteValue(int channel)
        {
            if ((uint)channel >= 16)
            {
                return DefaultChannelReverb;
            }

            lock (_syncRoot)
            {
                return _channelReverbSendAbsoluteValues[channel];
            }
        }

        public int GetChannelChorusSendAbsoluteValue(int channel)
        {
            if ((uint)channel >= 16)
            {
                return DefaultChannelChorus;
            }

            lock (_syncRoot)
            {
                return _channelChorusSendAbsoluteValues[channel];
            }
        }

        public int GetChannelReverbSendBiasValue(int channel)
        {
            if ((uint)channel >= 16)
            {
                return MidiMixSettings.DefaultBiasValue;
            }

            lock (_syncRoot)
            {
                return _channelReverbSendBiasValues[channel];
            }
        }

        public int GetChannelChorusSendBiasValue(int channel)
        {
            if ((uint)channel >= 16)
            {
                return MidiMixSettings.DefaultBiasValue;
            }

            lock (_syncRoot)
            {
                return _channelChorusSendBiasValues[channel];
            }
        }

        public ChannelSendMode GetChannelReverbSendMode(int channel)
        {
            if ((uint)channel >= 16)
            {
                return ChannelSendMode.Scale;
            }

            lock (_syncRoot)
            {
                return _channelReverbSendModes[channel];
            }
        }

        public ChannelSendMode GetChannelChorusSendMode(int channel)
        {
            if ((uint)channel >= 16)
            {
                return ChannelSendMode.Scale;
            }

            lock (_syncRoot)
            {
                return _channelChorusSendModes[channel];
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

        public int[] GetChannelReverbSendAbsoluteValues()
        {
            lock (_syncRoot)
            {
                return [.. _channelReverbSendAbsoluteValues];
            }
        }

        public int[] GetChannelChorusSendAbsoluteValues()
        {
            lock (_syncRoot)
            {
                return [.. _channelChorusSendAbsoluteValues];
            }
        }

        public int[] GetChannelReverbSendBiasValues()
        {
            lock (_syncRoot)
            {
                return [.. _channelReverbSendBiasValues];
            }
        }

        public int[] GetChannelChorusSendBiasValues()
        {
            lock (_syncRoot)
            {
                return [.. _channelChorusSendBiasValues];
            }
        }

        public ChannelSendMode[] GetChannelReverbSendModes()
        {
            lock (_syncRoot)
            {
                return [.. _channelReverbSendModes];
            }
        }

        public ChannelSendMode[] GetChannelChorusSendModes()
        {
            lock (_syncRoot)
            {
                return [.. _channelChorusSendModes];
            }
        }

        public bool SetChannelVolumePercent(int channel, int value)
            => SetChannelPercent(_channelVolumePercents, channel, value);

        public bool SetChannelReverbSendPercent(int channel, int value)
            => SetChannelPercent(_channelReverbSendPercents, channel, value);

        public bool SetChannelChorusSendPercent(int channel, int value)
            => SetChannelPercent(_channelChorusSendPercents, channel, value);

        public bool SetChannelReverbSendAbsoluteValue(int channel, int value)
            => SetChannelSendValue(_channelReverbSendAbsoluteValues, channel, value);

        public bool SetChannelChorusSendAbsoluteValue(int channel, int value)
            => SetChannelSendValue(_channelChorusSendAbsoluteValues, channel, value);

        public bool SetChannelReverbSendBiasValue(int channel, int value)
            => SetChannelBiasValue(_channelReverbSendBiasValues, channel, value, MaxMidiControllerValue);

        public bool SetChannelChorusSendBiasValue(int channel, int value)
            => SetChannelBiasValue(_channelChorusSendBiasValues, channel, value, MaxMidiControllerValue);

        public bool SetChannelReverbSendMode(int channel, ChannelSendMode mode)
            => SetChannelMode(_channelReverbSendModes, channel, mode);

        public bool SetChannelChorusSendMode(int channel, ChannelSendMode mode)
            => SetChannelMode(_channelChorusSendModes, channel, mode);

        public void SetChannelMixSettings(
            int[] channelVolumePercents,
            int[] channelReverbSendPercents,
            int[] channelChorusSendPercents,
            ChannelSendMode[] channelReverbSendModes,
            ChannelSendMode[] channelChorusSendModes,
            int[] channelReverbSendAbsoluteValues,
            int[] channelChorusSendAbsoluteValues,
            int[] channelReverbSendBiasValues,
            int[] channelChorusSendBiasValues)
        {
            lock (_syncRoot)
            {
                CopyPercents(channelVolumePercents, _channelVolumePercents);
                CopyPercents(channelReverbSendPercents, _channelReverbSendPercents);
                CopyPercents(channelChorusSendPercents, _channelChorusSendPercents);
                CopyModes(channelReverbSendModes, _channelReverbSendModes);
                CopyModes(channelChorusSendModes, _channelChorusSendModes);
                CopySendValues(channelReverbSendAbsoluteValues, _channelReverbSendAbsoluteValues, DefaultChannelReverb);
                CopySendValues(channelChorusSendAbsoluteValues, _channelChorusSendAbsoluteValues, DefaultChannelChorus);
                CopyBiasValues(channelReverbSendBiasValues, _channelReverbSendBiasValues, MaxMidiControllerValue);
                CopyBiasValues(channelChorusSendBiasValues, _channelChorusSendBiasValues, MaxMidiControllerValue);
            }
        }

        public MidiMixSettings CreateMixSettings(
            int playbackSpeedPercent,
            int playbackTransposeSemitones,
            int masterVolumePercent,
            ChannelSendMode reverbReturnMode,
            int reverbReturnScalePercent,
            int reverbReturnAbsoluteValue,
            int reverbReturnBiasValue,
            ChannelSendMode chorusReturnMode,
            int chorusReturnScalePercent,
            int chorusReturnAbsoluteValue,
            int chorusReturnBiasValue)
        {
            lock (_syncRoot)
            {
                return new MidiMixSettings
                {
                    PlaybackSpeedPercent = Math.Clamp(playbackSpeedPercent, MinPlaybackSpeedPercent, MaxPlaybackSpeedPercent),
                    PlaybackTransposeSemitones = Math.Clamp(playbackTransposeSemitones, MinTransposeSemitones, MaxTransposeSemitones),
                    MasterVolumePercent = ClampMixPercent(masterVolumePercent),
                    ReverbReturnPercent = ClampMixPercent(reverbReturnAbsoluteValue),
                    ChorusReturnPercent = ClampMixPercent(chorusReturnAbsoluteValue),
                    ReverbReturnScalePercent = ClampMixPercent(reverbReturnScalePercent),
                    ChorusReturnScalePercent = ClampMixPercent(chorusReturnScalePercent),
                    ReverbReturnBiasValue = ClampSignedValue(reverbReturnBiasValue, MaxMixPercent),
                    ChorusReturnBiasValue = ClampSignedValue(chorusReturnBiasValue, MaxMixPercent),
                    ReverbReturnMode = NormalizeMode(reverbReturnMode, ChannelSendMode.Absolute),
                    ChorusReturnMode = NormalizeMode(chorusReturnMode, ChannelSendMode.Absolute),
                    ChannelVolumePercents = [.. _channelVolumePercents],
                    ChannelReverbSendPercents = [.. _channelReverbSendPercents],
                    ChannelChorusSendPercents = [.. _channelChorusSendPercents],
                    ChannelReverbSendModes = [.. _channelReverbSendModes],
                    ChannelChorusSendModes = [.. _channelChorusSendModes],
                    ChannelReverbSendAbsoluteValues = [.. _channelReverbSendAbsoluteValues],
                    ChannelChorusSendAbsoluteValues = [.. _channelChorusSendAbsoluteValues],
                    ChannelReverbSendBiasValues = [.. _channelReverbSendBiasValues],
                    ChannelChorusSendBiasValues = [.. _channelChorusSendBiasValues]
                };
            }
        }

        public bool TryFilterMixEvent(int streamHandle, MidiEvent midiEvent)
        {
            if (midiEvent.EventType is MidiEventType.ReverbLevel or MidiEventType.ChorusLevel)
            {
                lock (_syncRoot)
                {
                    if (midiEvent.EventType == MidiEventType.ReverbLevel)
                    {
                        _rawReverbReturnValue = ClampMixPercent(midiEvent.Parameter);
                    }
                    else
                    {
                        _rawChorusReturnValue = ClampMixPercent(midiEvent.Parameter);
                    }
                }

                ApplyGlobalReturns(streamHandle);
                return true;
            }

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

            ApplyGlobalReturns(streamHandle);
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

        public void ApplyGlobalReturns(int streamHandle)
        {
            if (streamHandle == 0)
            {
                return;
            }

            int reverbReturnValue;
            int chorusReturnValue;
            lock (_syncRoot)
            {
                reverbReturnValue = ResolveReturnValue(_rawReverbReturnValue, _reverbReturnScalePercent, _reverbReturnAbsoluteValue, _reverbReturnBiasValue, _reverbReturnMode);
                chorusReturnValue = ResolveReturnValue(_rawChorusReturnValue, _chorusReturnScalePercent, _chorusReturnAbsoluteValue, _chorusReturnBiasValue, _chorusReturnMode);
            }

            ApplyGlobalReturns(streamHandle, reverbReturnValue, chorusReturnValue);
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

        private static void ApplyGlobalReturns(int streamHandle, int reverbReturnPercent, int chorusReturnPercent)
        {
            if (streamHandle == 0)
            {
                return;
            }

            BassMidi.StreamEvent(streamHandle, 0, MidiEventType.ReverbLevel, ClampMixPercent(reverbReturnPercent));
            BassMidi.StreamEvent(streamHandle, 0, MidiEventType.ChorusLevel, ClampMixPercent(chorusReturnPercent));
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
            Array.Fill(_channelReverbSendAbsoluteValues, DefaultChannelReverb);
            Array.Fill(_channelChorusSendAbsoluteValues, DefaultChannelChorus);
            Array.Fill(_channelReverbSendBiasValues, MidiMixSettings.DefaultBiasValue);
            Array.Fill(_channelChorusSendBiasValues, MidiMixSettings.DefaultBiasValue);
            Array.Fill(_channelReverbSendModes, ChannelSendMode.Scale);
            Array.Fill(_channelChorusSendModes, ChannelSendMode.Scale);
        }

        private static void CopyPercents(int[] source, int[] target)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = i < source.Length ? ClampMixPercent(source[i]) : DefaultMixPercent;
            }
        }

        private static void CopySendValues(int[] source, int[] target, int defaultValue)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = i < source.Length ? ClampMidiValue(source[i]) : defaultValue;
            }
        }

        private static void CopyModes(ChannelSendMode[] source, ChannelSendMode[] target)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = i < source.Length ? NormalizeMode(source[i], ChannelSendMode.Scale) : ChannelSendMode.Scale;
            }
        }

        private static void CopyBiasValues(int[] source, int[] target, int limit)
        {
            for (int i = 0; i < target.Length; i++)
            {
                target[i] = i < source.Length ? ClampSignedValue(source[i], limit) : MidiMixSettings.DefaultBiasValue;
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

        private bool SetChannelSendValue(int[] values, int channel, int value)
        {
            if ((uint)channel >= 16)
            {
                return false;
            }

            lock (_syncRoot)
            {
                int clampedValue = ClampMidiValue(value);
                if (values[channel] == clampedValue)
                {
                    return false;
                }

                values[channel] = clampedValue;
                return true;
            }
        }

        private bool SetChannelBiasValue(int[] values, int channel, int value, int limit)
        {
            if ((uint)channel >= 16)
            {
                return false;
            }

            lock (_syncRoot)
            {
                int clampedValue = ClampSignedValue(value, limit);
                if (values[channel] == clampedValue)
                {
                    return false;
                }

                values[channel] = clampedValue;
                return true;
            }
        }

        private bool SetChannelMode(ChannelSendMode[] values, int channel, ChannelSendMode value)
        {
            if ((uint)channel >= 16)
            {
                return false;
            }

            lock (_syncRoot)
            {
                var normalizedValue = NormalizeMode(value, ChannelSendMode.Scale);
                if (values[channel] == normalizedValue)
                {
                    return false;
                }

                values[channel] = normalizedValue;
                return true;
            }
        }

        private ChannelMixState CreateChannelMixState(int channel)
            => new(
                channel,
                ScaleMidiValue(_rawVolumeValues[channel], _channelVolumePercents[channel], _masterVolumePercent),
                ResolveSendValue(_rawReverbValues[channel], _channelReverbSendPercents[channel], _channelReverbSendAbsoluteValues[channel], _channelReverbSendBiasValues[channel], _channelReverbSendModes[channel]),
                ResolveSendValue(_rawChorusValues[channel], _channelChorusSendPercents[channel], _channelChorusSendAbsoluteValues[channel], _channelChorusSendBiasValues[channel], _channelChorusSendModes[channel]));

        private static int ClampMidiValue(int value)
            => Math.Clamp(value, 0, 127);

        private static int ClampMixPercent(int value)
            => Math.Clamp(value, MinMixPercent, MaxMixPercent);

        private static int ClampSignedValue(int value, int limit)
            => Math.Clamp(value, -limit, limit);

        private static int ScaleMidiValue(int value, int channelPercent, int globalPercent)
            => Math.Clamp((int)Math.Round(ClampMidiValue(value) * (channelPercent / 100d) * (globalPercent / 100d)), 0, MaxMidiControllerValue);

        private static int ScaleMidiValue(int value, int percent)
            => Math.Clamp((int)Math.Round(ClampMidiValue(value) * (percent / 100d)), 0, MaxMidiControllerValue);

        private static int ResolveSendValue(int rawValue, int percentValue, int absoluteValue, int biasValue, ChannelSendMode mode)
            => NormalizeMode(mode, ChannelSendMode.Scale) switch
            {
                ChannelSendMode.Absolute => ClampMidiValue(absoluteValue),
                ChannelSendMode.Bias => ClampMidiValue(ClampMidiValue(rawValue) + ClampSignedValue(biasValue, MaxMidiControllerValue)),
                _ => ScaleMidiValue(rawValue, percentValue)
            };

        private static int ResolveReturnValue(int rawValue, int scalePercent, int absoluteValue, int biasValue, ChannelSendMode mode)
            => NormalizeMode(mode, ChannelSendMode.Absolute) switch
            {
                ChannelSendMode.Scale => ClampMixPercent((int)Math.Round(ClampMixPercent(rawValue) * (scalePercent / 100d))),
                ChannelSendMode.Bias => ClampMixPercent(ClampMixPercent(rawValue) + ClampSignedValue(biasValue, MaxMixPercent)),
                _ => ClampMixPercent(absoluteValue)
            };

        private static ChannelSendMode NormalizeMode(ChannelSendMode mode, ChannelSendMode defaultMode)
            => mode switch
            {
                ChannelSendMode.Scale => ChannelSendMode.Scale,
                ChannelSendMode.Absolute => ChannelSendMode.Absolute,
                ChannelSendMode.Bias => ChannelSendMode.Bias,
                _ => defaultMode
            };

        private readonly record struct ChannelMixState(int Channel, int VolumeValue, int ReverbValue, int ChorusValue);
    }

    private sealed record EqExportSettings(bool IsEnabled, EqBandState[] Bands);

    private sealed class ExportEqProcessor : IDisposable
    {
        private readonly bool _isEnabled;
        private readonly StereoBiquadFilter[] _peakFilters = CreateFilterArray(EqPeakBandCount);
        private readonly StereoBiquadFilter[] _lowCutFilters = CreateFilterArray(EqMaxCutStages);
        private readonly StereoBiquadFilter[] _highCutFilters = CreateFilterArray(EqMaxCutStages);
        private readonly object _syncRoot = new();
        private readonly DSPProcedure _dspProc;
        private readonly int _streamHandle;
        private int _dspHandle;

        public ExportEqProcessor(EqExportSettings settings, int sampleRate, int streamHandle)
        {
            _isEnabled = settings.IsEnabled;
            _streamHandle = streamHandle;
            _dspProc = ProcessDsp;

            if (!_isEnabled)
            {
                return;
            }

            for (var i = 0; i < EqPeakBandCount; i++)
            {
                var band = settings.Bands[EqPeakBandOffset + i];
                if (Math.Abs(band.GainDb) < 0.01)
                {
                    continue;
                }

                _peakFilters[i].SetPeak(
                    sampleRate,
                    band.Frequency,
                    band.GainDb,
                    MapUiQToFilterQ(band.Q));
            }

            ConfigureCutFilters(sampleRate, settings.Bands[0], isHighPass: true, _lowCutFilters);
            ConfigureCutFilters(sampleRate, settings.Bands[^1], isHighPass: false, _highCutFilters);

            _dspHandle = Bass.ChannelSetDSP(streamHandle, _dspProc, IntPtr.Zero, 0);
            if (_dspHandle == 0)
            {
                throw CreateBassException("Failed to install export EQ DSP");
            }
        }

        public void Dispose()
        {
            if (_streamHandle != 0 && _dspHandle != 0)
            {
                Bass.ChannelRemoveDSP(_streamHandle, _dspHandle);
                _dspHandle = 0;
            }
        }

        private unsafe void ProcessDsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
        {
            if (!_isEnabled || length <= 0)
            {
                return;
            }

            var sampleCount = length / sizeof(float);
            if (sampleCount < EqChannelCount)
            {
                return;
            }

            var samples = (float*)buffer;
            lock (_syncRoot)
            {
                for (var index = 0; index + 1 < sampleCount; index += EqChannelCount)
                {
                    var left = samples[index];
                    var right = samples[index + 1];

                    foreach (var filter in _lowCutFilters)
                    {
                        filter.Process(ref left, ref right);
                    }

                    foreach (var filter in _peakFilters)
                    {
                        filter.Process(ref left, ref right);
                    }

                    foreach (var filter in _highCutFilters)
                    {
                        filter.Process(ref left, ref right);
                    }

                    samples[index] = left;
                    samples[index + 1] = right;
                }
            }
        }
    }

    private sealed class StereoBiquadFilter
    {
        private double _b0;
        private double _b1;
        private double _b2;
        private double _a1;
        private double _a2;
        private double _z1Left;
        private double _z2Left;
        private double _z1Right;
        private double _z2Right;

        public bool IsActive { get; private set; }

        public void Disable()
        {
            IsActive = false;
            Reset();
        }

        public void Reset()
        {
            _z1Left = 0;
            _z2Left = 0;
            _z1Right = 0;
            _z2Right = 0;
        }

        public void SetPeak(int sampleRate, double frequency, double gainDb, double q)
        {
            if (Math.Abs(gainDb) < 0.001 || frequency <= 0 || sampleRate <= 0)
            {
                Disable();
                return;
            }

            var w0 = 2.0 * Math.PI * Math.Clamp(frequency, EqMinFrequency, sampleRate * 0.48) / sampleRate;
            var cos = Math.Cos(w0);
            var sin = Math.Sin(w0);
            var a = Math.Pow(10.0, gainDb / 40.0);
            var alpha = sin / (2.0 * Math.Max(0.05, q));

            var b0 = 1.0 + alpha * a;
            var b1 = -2.0 * cos;
            var b2 = 1.0 - alpha * a;
            var a0 = 1.0 + alpha / a;
            var a1 = -2.0 * cos;
            var a2 = 1.0 - alpha / a;

            SetNormalizedCoefficients(b0, b1, b2, a0, a1, a2);
        }

        public void SetLowPass(int sampleRate, double frequency, double q)
        {
            SetPass(sampleRate, frequency, q, highPass: false);
        }

        public void SetHighPass(int sampleRate, double frequency, double q)
        {
            SetPass(sampleRate, frequency, q, highPass: true);
        }

        public void Process(ref float left, ref float right)
        {
            if (!IsActive)
            {
                return;
            }

            var processedLeft = _b0 * left + _z1Left;
            _z1Left = _b1 * left - _a1 * processedLeft + _z2Left;
            _z2Left = _b2 * left - _a2 * processedLeft;

            var processedRight = _b0 * right + _z1Right;
            _z1Right = _b1 * right - _a1 * processedRight + _z2Right;
            _z2Right = _b2 * right - _a2 * processedRight;

            left = (float)processedLeft;
            right = (float)processedRight;
        }

        private void SetPass(int sampleRate, double frequency, double q, bool highPass)
        {
            if (frequency <= 0 || sampleRate <= 0)
            {
                Disable();
                return;
            }

            var w0 = 2.0 * Math.PI * Math.Clamp(frequency, EqMinFrequency, sampleRate * 0.48) / sampleRate;
            var cos = Math.Cos(w0);
            var sin = Math.Sin(w0);
            var alpha = sin / (2.0 * Math.Max(0.05, q));

            double b0;
            double b1;
            double b2;
            var a0 = 1.0 + alpha;
            var a1 = -2.0 * cos;
            var a2 = 1.0 - alpha;

            if (highPass)
            {
                b0 = (1.0 + cos) / 2.0;
                b1 = -(1.0 + cos);
                b2 = (1.0 + cos) / 2.0;
            }
            else
            {
                b0 = (1.0 - cos) / 2.0;
                b1 = 1.0 - cos;
                b2 = (1.0 - cos) / 2.0;
            }

            SetNormalizedCoefficients(b0, b1, b2, a0, a1, a2);
        }

        private void SetNormalizedCoefficients(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            if (Math.Abs(a0) < 1e-9)
            {
                Disable();
                return;
            }

            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
            IsActive = true;
        }
    }

    private sealed class EqBandState
    {
        public EqBandState(double frequency, double gainDb, double q, int slopeDbPerOct)
        {
            Frequency = frequency;
            GainDb = gainDb;
            Q = q;
            SlopeDbPerOct = slopeDbPerOct;
        }

        public double Frequency { get; set; }

        public double GainDb { get; set; }

        public double Q { get; set; }

        public int SlopeDbPerOct { get; set; }
    }

    private readonly record struct TempoPoint(long Tick, double Bpm);

    private enum MidiTimeBaseKind
    {
        Unknown,
        Ppq,
        Smpte
    }

    private readonly record struct MidiTimeBase(MidiTimeBaseKind Kind, int TicksPerQuarterNote, double TicksPerSecond)
    {
        public static MidiTimeBase Default => new(MidiTimeBaseKind.Ppq, 480, 0);

        public static MidiTimeBase Load(string path)
        {
            try
            {
                return TryReadDivision(path, out ushort division)
                    ? Create(division)
                    : Default;
            }
            catch
            {
                return Default;
            }
        }

        private static MidiTimeBase Create(ushort division)
        {
            if ((division & 0x8000) == 0)
            {
                int ticksPerQuarterNote = division == 0 ? Default.TicksPerQuarterNote : division;
                return new MidiTimeBase(MidiTimeBaseKind.Ppq, ticksPerQuarterNote, 0);
            }

            int ticksPerFrame = division & 0xFF;
            if (ticksPerFrame <= 0)
            {
                return Default;
            }

            sbyte frameCode = unchecked((sbyte)(division >> 8));
            double framesPerSecond = frameCode switch
            {
                -24 => 24d,
                -25 => 25d,
                -29 => 29.97d,
                -30 => 30d,
                _ => 30d
            };

            return new MidiTimeBase(MidiTimeBaseKind.Smpte, 0, framesPerSecond * ticksPerFrame);
        }

        private static bool TryReadDivision(string path, out ushort division)
        {
            using var stream = File.OpenRead(path);
            return TryReadDivision(stream, out division);
        }

        private static bool TryReadDivision(Stream stream, out ushort division)
        {
            division = 0;
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) != header.Length)
            {
                return false;
            }

            if (header.SequenceEqual("MThd"u8))
            {
                stream.Seek(-header.Length, SeekOrigin.Current);
                return TryReadMidiHeaderDivision(stream, out division);
            }

            if (!header.SequenceEqual("RIFF"u8))
            {
                return false;
            }

            Span<byte> riffHeader = stackalloc byte[8];
            if (stream.Read(riffHeader) != riffHeader.Length)
            {
                return false;
            }

            if (!riffHeader[4..].SequenceEqual("RMID"u8))
            {
                return false;
            }

            Span<byte> chunkHeader = stackalloc byte[8];
            while (stream.Position + chunkHeader.Length <= stream.Length)
            {
                if (stream.Read(chunkHeader) != chunkHeader.Length)
                {
                    return false;
                }

                uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..]);
                if (chunkHeader[..4].SequenceEqual("data"u8))
                {
                    return TryReadMidiHeaderDivision(stream, out division);
                }

                long skip = chunkSize + (chunkSize & 1u);
                if (stream.Position + skip > stream.Length)
                {
                    return false;
                }

                stream.Seek(skip, SeekOrigin.Current);
            }

            return false;
        }

        private static bool TryReadMidiHeaderDivision(Stream stream, out ushort division)
        {
            division = 0;
            Span<byte> buffer = stackalloc byte[8];
            if (stream.Read(buffer) != buffer.Length)
            {
                return false;
            }

            if (!buffer[..4].SequenceEqual("MThd"u8))
            {
                return false;
            }

            uint headerLength = BinaryPrimitives.ReadUInt32BigEndian(buffer[4..8]);
            if (headerLength < 6)
            {
                return false;
            }

            Span<byte> headerData = stackalloc byte[6];
            if (stream.Read(headerData) != headerData.Length)
            {
                return false;
            }

            division = BinaryPrimitives.ReadUInt16BigEndian(headerData[4..6]);

            long remainingHeaderBytes = (long)headerLength - headerData.Length;
            if (remainingHeaderBytes > 0)
            {
                if (stream.Position + remainingHeaderBytes > stream.Length)
                {
                    return false;
                }

                stream.Seek(remainingHeaderBytes, SeekOrigin.Current);
            }

            return true;
        }
    }
}
