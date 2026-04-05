namespace MidiPlayer.App.Services;

public enum AudioExportFormat
{
    Wav,
    Flac,
    Opus
}

public enum AudioBitDepth
{
    Pcm16,
    Pcm24,
    Float32
}

public sealed record AudioExportOptions(
    string OutputPath,
    int SampleRate,
    AudioExportFormat Format,
    AudioBitDepth BitDepth,
    int OpusBitrateKbps);
