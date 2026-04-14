using Avalonia.Controls;

namespace MidiPlayer.App.Services;

internal sealed class NoopSystemMediaControlsBackend : ISystemMediaControlsBackend
{
    public void AttachWindow(Window window)
    {
    }

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
    {
    }

    public void UpdatePlaybackState(SystemMediaPlaybackState state, double position)
    {
    }

    public void UpdatePosition(double position)
    {
    }

    public void UpdateVolume(double volume)
    {
    }

    public void UpdateRate(double rate)
    {
    }

    public void Dispose()
    {
    }
}
