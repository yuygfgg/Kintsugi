using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MidiPlayer.App.Services;

#if WINDOWS
internal sealed class WindowsSystemMediaControlsBackend : ISystemMediaControlsBackend
{
    private readonly SystemMediaControlsCallbacks _callbacks;
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private double _currentDuration;
    private double _currentPosition;
    private bool _hasNowPlaying;
    private SystemMediaPlaybackState _playbackState = SystemMediaPlaybackState.Stopped;
    private Windows.Media.SystemMediaTransportControls? _smtc;
    private Window? _attachedWindow;

    public WindowsSystemMediaControlsBackend(SystemMediaControlsCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    public void AttachWindow(Window window)
    {
        if (!ReferenceEquals(_attachedWindow, window))
        {
            if (_attachedWindow is not null)
            {
                _attachedWindow.Opened -= AttachedWindow_Opened;
            }

            _attachedWindow = window;
            _attachedWindow.Opened += AttachedWindow_Opened;
        }

        if (_attachedWindow.IsVisible)
        {
            Dispatcher.UIThread.Post(TryInitWindowsControls);
        }
    }

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
    {
        _currentTitle = title;
        _currentArtist = artist;
        _currentDuration = SystemMediaControlsMath.SanitizeSeconds(duration);
        _currentPosition = SystemMediaControlsMath.ClampSeconds(position, _currentDuration);
        _hasNowPlaying = true;

        try
        {
            TryInitWindowsControls();
            ApplyWindowsNowPlaying();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Windows now playing metadata: {ex}");
        }
    }

    public void UpdatePlaybackState(SystemMediaPlaybackState state, double position)
    {
        _playbackState = state;
        _currentPosition = SystemMediaControlsMath.ClampSeconds(position, _currentDuration);

        try
        {
            TryInitWindowsControls();
            ApplyWindowsPlaybackState();
            ApplyWindowsTimeline();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Windows playback state: {ex}");
        }
    }

    public void UpdatePosition(double position)
    {
        _currentPosition = SystemMediaControlsMath.ClampSeconds(position, _currentDuration);

        try
        {
            TryInitWindowsControls();
            ApplyWindowsTimeline();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Windows playback position: {ex}");
        }
    }

    public void UpdateVolume(double volume)
    {
    }

    public void UpdateRate(double rate)
    {
    }

    public void Dispose()
    {
        if (_attachedWindow is not null)
        {
            _attachedWindow.Opened -= AttachedWindow_Opened;
            _attachedWindow = null;
        }

        if (_smtc is null)
        {
            return;
        }

        _smtc.ButtonPressed -= Smtc_ButtonPressed;
        _smtc.PlaybackPositionChangeRequested -= Smtc_PlaybackPositionChangeRequested;
        _smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Stopped;
    }

    private void AttachedWindow_Opened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(TryInitWindowsControls);
    }

    private void TryInitWindowsControls()
    {
        if (_smtc is not null || _attachedWindow is null)
        {
            return;
        }

        var platformHandle = _attachedWindow.TryGetPlatformHandle();
        if (platformHandle is null || platformHandle.Handle == IntPtr.Zero)
        {
            return;
        }

        _smtc = Windows.Media.SystemMediaTransportControlsInterop.GetForWindow(platformHandle.Handle);
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.ButtonPressed += Smtc_ButtonPressed;
        _smtc.PlaybackPositionChangeRequested += Smtc_PlaybackPositionChangeRequested;
        ApplyWindowsNowPlaying();
        ApplyWindowsPlaybackState();
    }

    private void Smtc_ButtonPressed(Windows.Media.SystemMediaTransportControls sender, Windows.Media.SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case Windows.Media.SystemMediaTransportControlsButton.Play:
                _callbacks.Play();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Pause:
                _callbacks.Pause();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Stop:
                _callbacks.Stop();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Next:
                _callbacks.Next();
                break;
            case Windows.Media.SystemMediaTransportControlsButton.Previous:
                _callbacks.Previous();
                break;
        }
    }

    private void Smtc_PlaybackPositionChangeRequested(Windows.Media.SystemMediaTransportControls sender, Windows.Media.PlaybackPositionChangeRequestedEventArgs args)
        => _callbacks.Seek(args.RequestedPlaybackPosition.TotalSeconds);

    private void ApplyWindowsNowPlaying()
    {
        if (_smtc is null || !_hasNowPlaying)
        {
            return;
        }

        var updater = _smtc.DisplayUpdater;
        updater.Type = Windows.Media.MediaPlaybackType.Music;
        updater.MusicProperties.Title = _currentTitle;
        updater.MusicProperties.Artist = _currentArtist;
        updater.Update();

        ApplyWindowsTimeline();
    }

    private void ApplyWindowsPlaybackState()
    {
        if (_smtc is null)
        {
            return;
        }

        _smtc.PlaybackStatus = _playbackState switch
        {
            SystemMediaPlaybackState.Playing => Windows.Media.MediaPlaybackStatus.Playing,
            SystemMediaPlaybackState.Paused => Windows.Media.MediaPlaybackStatus.Paused,
            _ => Windows.Media.MediaPlaybackStatus.Stopped
        };
    }

    private void ApplyWindowsTimeline()
    {
        if (_smtc is null || !_hasNowPlaying)
        {
            return;
        }

        double duration = SystemMediaControlsMath.SanitizeSeconds(_currentDuration);
        double position = SystemMediaControlsMath.ClampSeconds(_currentPosition, duration);

        var timelineProperties = new Windows.Media.SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = TimeSpan.FromSeconds(position),
            MaxSeekTime = TimeSpan.FromSeconds(duration),
            EndTime = TimeSpan.FromSeconds(duration)
        };

        _smtc.UpdateTimelineProperties(timelineProperties);
    }
}
#else
internal sealed class WindowsSystemMediaControlsBackend : ISystemMediaControlsBackend
{
    public WindowsSystemMediaControlsBackend(SystemMediaControlsCallbacks callbacks)
    {
    }

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
#endif
