using System;

namespace MidiPlayer.App.Services;

public sealed class MidiMixSettings
{
    public const int DefaultBiasValue = 0;

    public int PlaybackSpeedPercent { get; set; } = BassMidiPlayer.DefaultPlaybackSpeedPercent;

    public int PlaybackTransposeSemitones { get; set; } = BassMidiPlayer.DefaultTransposeSemitones;

    public int MasterVolumePercent { get; set; } = BassMidiPlayer.DefaultMixPercent;

    public int ReverbReturnPercent { get; set; } = BassMidiPlayer.DefaultMixPercent;

    public int ChorusReturnPercent { get; set; } = BassMidiPlayer.DefaultMixPercent;

    public int ReverbReturnScalePercent { get; set; } = BassMidiPlayer.DefaultMixPercent;

    public int ChorusReturnScalePercent { get; set; } = BassMidiPlayer.DefaultMixPercent;

    public int ReverbReturnBiasValue { get; set; } = DefaultBiasValue;

    public int ChorusReturnBiasValue { get; set; } = DefaultBiasValue;

    public ChannelSendMode ReverbReturnMode { get; set; } = ChannelSendMode.Absolute;

    public ChannelSendMode ChorusReturnMode { get; set; } = ChannelSendMode.Absolute;

    public int[] ChannelVolumePercents { get; set; } = CreateIntArray(BassMidiPlayer.DefaultMixPercent);

    public int[] ChannelReverbSendPercents { get; set; } = CreateIntArray(BassMidiPlayer.DefaultMixPercent);

    public int[] ChannelChorusSendPercents { get; set; } = CreateIntArray(BassMidiPlayer.DefaultMixPercent);

    public ChannelSendMode[] ChannelReverbSendModes { get; set; } = CreateModeArray(ChannelSendMode.Scale);

    public ChannelSendMode[] ChannelChorusSendModes { get; set; } = CreateModeArray(ChannelSendMode.Scale);

    public int[] ChannelReverbSendAbsoluteValues { get; set; } = CreateIntArray(BassMidiPlayer.DefaultChannelReverb);

    public int[] ChannelChorusSendAbsoluteValues { get; set; } = CreateIntArray(BassMidiPlayer.DefaultChannelChorus);

    public int[] ChannelReverbSendBiasValues { get; set; } = CreateIntArray(DefaultBiasValue);

    public int[] ChannelChorusSendBiasValues { get; set; } = CreateIntArray(DefaultBiasValue);

    public void Normalize()
    {
        PlaybackSpeedPercent = NormalizeValue(PlaybackSpeedPercent, BassMidiPlayer.DefaultPlaybackSpeedPercent);
        PlaybackTransposeSemitones = NormalizeValue(PlaybackTransposeSemitones, BassMidiPlayer.DefaultTransposeSemitones);
        ReverbReturnScalePercent = NormalizeValue(ReverbReturnScalePercent, BassMidiPlayer.DefaultMixPercent);
        ChorusReturnScalePercent = NormalizeValue(ChorusReturnScalePercent, BassMidiPlayer.DefaultMixPercent);
        ReverbReturnBiasValue = NormalizeValue(ReverbReturnBiasValue, DefaultBiasValue);
        ChorusReturnBiasValue = NormalizeValue(ChorusReturnBiasValue, DefaultBiasValue);
        ReverbReturnMode = NormalizeMode(ReverbReturnMode, ChannelSendMode.Absolute);
        ChorusReturnMode = NormalizeMode(ChorusReturnMode, ChannelSendMode.Absolute);
        ChannelVolumePercents = NormalizeIntArray(ChannelVolumePercents, BassMidiPlayer.DefaultMixPercent);
        ChannelReverbSendPercents = NormalizeIntArray(ChannelReverbSendPercents, BassMidiPlayer.DefaultMixPercent);
        ChannelChorusSendPercents = NormalizeIntArray(ChannelChorusSendPercents, BassMidiPlayer.DefaultMixPercent);
        ChannelReverbSendModes = NormalizeModeArray(ChannelReverbSendModes, ChannelSendMode.Scale);
        ChannelChorusSendModes = NormalizeModeArray(ChannelChorusSendModes, ChannelSendMode.Scale);
        ChannelReverbSendAbsoluteValues = NormalizeIntArray(ChannelReverbSendAbsoluteValues, BassMidiPlayer.DefaultChannelReverb);
        ChannelChorusSendAbsoluteValues = NormalizeIntArray(ChannelChorusSendAbsoluteValues, BassMidiPlayer.DefaultChannelChorus);
        ChannelReverbSendBiasValues = NormalizeIntArray(ChannelReverbSendBiasValues, DefaultBiasValue);
        ChannelChorusSendBiasValues = NormalizeIntArray(ChannelChorusSendBiasValues, DefaultBiasValue);
    }

    public MidiMixSettings Clone()
        => new()
        {
            PlaybackSpeedPercent = PlaybackSpeedPercent,
            PlaybackTransposeSemitones = PlaybackTransposeSemitones,
            MasterVolumePercent = MasterVolumePercent,
            ReverbReturnPercent = ReverbReturnPercent,
            ChorusReturnPercent = ChorusReturnPercent,
            ReverbReturnScalePercent = ReverbReturnScalePercent,
            ChorusReturnScalePercent = ChorusReturnScalePercent,
            ReverbReturnBiasValue = ReverbReturnBiasValue,
            ChorusReturnBiasValue = ChorusReturnBiasValue,
            ReverbReturnMode = ReverbReturnMode,
            ChorusReturnMode = ChorusReturnMode,
            ChannelVolumePercents = [.. ChannelVolumePercents],
            ChannelReverbSendPercents = [.. ChannelReverbSendPercents],
            ChannelChorusSendPercents = [.. ChannelChorusSendPercents],
            ChannelReverbSendModes = [.. ChannelReverbSendModes],
            ChannelChorusSendModes = [.. ChannelChorusSendModes],
            ChannelReverbSendAbsoluteValues = [.. ChannelReverbSendAbsoluteValues],
            ChannelChorusSendAbsoluteValues = [.. ChannelChorusSendAbsoluteValues],
            ChannelReverbSendBiasValues = [.. ChannelReverbSendBiasValues],
            ChannelChorusSendBiasValues = [.. ChannelChorusSendBiasValues]
        };

    private static int[] CreateIntArray(int defaultValue)
    {
        var values = new int[16];
        Array.Fill(values, defaultValue);
        return values;
    }

    private static ChannelSendMode[] CreateModeArray(ChannelSendMode defaultValue)
    {
        var values = new ChannelSendMode[16];
        Array.Fill(values, defaultValue);
        return values;
    }

    private static int[] NormalizeIntArray(int[]? source, int defaultValue)
    {
        var values = CreateIntArray(defaultValue);
        if (source is null)
        {
            return values;
        }

        Array.Copy(source, values, Math.Min(source.Length, values.Length));
        return values;
    }

    private static ChannelSendMode[] NormalizeModeArray(ChannelSendMode[]? source, ChannelSendMode defaultValue)
    {
        var values = CreateModeArray(defaultValue);
        if (source is null)
        {
            return values;
        }

        Array.Copy(source, values, Math.Min(source.Length, values.Length));
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = NormalizeMode(values[i], defaultValue);
        }
        return values;
    }

    private static int NormalizeValue(int value, int defaultValue)
        => value;

    private static ChannelSendMode NormalizeMode(ChannelSendMode value, ChannelSendMode defaultValue)
        => value switch
        {
            ChannelSendMode.Scale => ChannelSendMode.Scale,
            ChannelSendMode.Absolute => ChannelSendMode.Absolute,
            ChannelSendMode.Bias => ChannelSendMode.Bias,
            _ => defaultValue
        };
}
