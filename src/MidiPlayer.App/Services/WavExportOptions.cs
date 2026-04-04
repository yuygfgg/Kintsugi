namespace MidiPlayer.App.Services;

public enum WavBitDepth
{
    Pcm16,
    Pcm24,
    Float32
}

public sealed record WavExportOptions(string OutputPath, int SampleRate, WavBitDepth BitDepth);
