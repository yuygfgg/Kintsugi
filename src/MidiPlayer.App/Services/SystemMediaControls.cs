using System;
using Avalonia.Controls;

namespace MidiPlayer.App.Services;

public sealed class SystemMediaControls : IDisposable
{
    private readonly ISystemMediaControlsBackend _backend;

    public SystemMediaControls(
        Action onPlay,
        Action onPause,
        Action onStop,
        Action onToggle,
        Action<double> onSeek,
        Action onNext,
        Action onPrevious,
        Func<double>? getVolume = null,
        Action<double>? onSetVolume = null,
        Func<double>? getRate = null,
        Action<double>? onSetRate = null)
    {
        ArgumentNullException.ThrowIfNull(onPlay);
        ArgumentNullException.ThrowIfNull(onPause);
        ArgumentNullException.ThrowIfNull(onStop);
        ArgumentNullException.ThrowIfNull(onToggle);
        ArgumentNullException.ThrowIfNull(onSeek);
        ArgumentNullException.ThrowIfNull(onNext);
        ArgumentNullException.ThrowIfNull(onPrevious);

        var callbacks = new SystemMediaControlsCallbacks(
            onPlay,
            onPause,
            onStop,
            onToggle,
            onSeek,
            onNext,
            onPrevious,
            getVolume,
            onSetVolume,
            getRate,
            onSetRate);

        _backend = SystemMediaControlsBackendFactory.Create(callbacks);
    }

    public void AttachWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _backend.AttachWindow(window);
    }

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
        => _backend.UpdateNowPlaying(title, artist, duration, position);

    public void UpdatePlaybackState(SystemMediaPlaybackState state, double position)
        => _backend.UpdatePlaybackState(state, position);

    public void UpdatePosition(double position)
        => _backend.UpdatePosition(position);

    public void UpdateVolume(double volume)
        => _backend.UpdateVolume(volume);

    public void UpdateRate(double rate)
        => _backend.UpdateRate(rate);

    public void Dispose()
        => _backend.Dispose();
}
