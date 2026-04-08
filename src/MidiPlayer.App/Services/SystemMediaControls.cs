using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MidiPlayer.App.Services;

public class SystemMediaControls : IDisposable
{
    private readonly Action _onPlay;
    private readonly Action _onPause;
    private readonly Action _onStop;
    private readonly Action _onToggle;
    private readonly Action<double> _onSeek;
    private readonly Action _onNext;
    private readonly Action _onPrevious;

    private static SystemMediaControls? _instance;

#if WINDOWS
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private double _currentDuration;
    private double _currentPosition;
    private bool _hasNowPlaying;
    private bool _isPlaying;
    private bool _hasPlaybackState;
    private Windows.Media.SystemMediaTransportControls? _smtc;
    private Window? _attachedWindow;
#endif

    public SystemMediaControls(Action onPlay, Action onPause, Action onStop, Action onToggle, Action<double> onSeek, Action onNext, Action onPrevious)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onStop = onStop;
        _onToggle = onToggle;
        _onSeek = onSeek;
        _onNext = onNext;
        _onPrevious = onPrevious;
        _instance = this;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            InitMacControls();
        }
    }

    public void AttachWindow(Window window)
    {
#if WINDOWS
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

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
#endif
    }

    private void InitMacControls()
    {
        try
        {
            _macCallback = MacCommandCallback;
            _macSeekCallback = MacSeekCallback;
            InitMediaControls(_macCallback, _macSeekCallback);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to init Mac media controls: {ex.Message}");
        }
    }

#if WINDOWS
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

        try
        {
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
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to init Windows SMTC: {ex}");
        }
    }

    private void Smtc_ButtonPressed(Windows.Media.SystemMediaTransportControls sender, Windows.Media.SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (args.Button)
            {
                case Windows.Media.SystemMediaTransportControlsButton.Play:
                    _onPlay?.Invoke();
                    break;
                case Windows.Media.SystemMediaTransportControlsButton.Pause:
                    _onPause?.Invoke();
                    break;
                case Windows.Media.SystemMediaTransportControlsButton.Stop:
                    _onStop?.Invoke();
                    break;
                case Windows.Media.SystemMediaTransportControlsButton.Next:
                    _onNext?.Invoke();
                    break;
                case Windows.Media.SystemMediaTransportControlsButton.Previous:
                    _onPrevious?.Invoke();
                    break;
            }
        });
    }

    private void Smtc_PlaybackPositionChangeRequested(Windows.Media.SystemMediaTransportControls sender, Windows.Media.PlaybackPositionChangeRequestedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _onSeek?.Invoke(args.RequestedPlaybackPosition.TotalSeconds);
        });
    }

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

        _smtc.PlaybackStatus = _hasPlaybackState
            ? (_isPlaying ? Windows.Media.MediaPlaybackStatus.Playing : Windows.Media.MediaPlaybackStatus.Paused)
            : Windows.Media.MediaPlaybackStatus.Stopped;
    }

    private void ApplyWindowsTimeline()
    {
        if (_smtc is null || !_hasNowPlaying)
        {
            return;
        }

        double duration = SanitizeSeconds(_currentDuration);
        double position = ClampSeconds(_currentPosition, duration);

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
#endif

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                UpdateNowPlayingMac(title, artist, duration, position);
            }
            catch
            {
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            _currentTitle = title;
            _currentArtist = artist;
            _currentDuration = SanitizeSeconds(duration);
            _currentPosition = ClampSeconds(position, _currentDuration);
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
#endif
        }
    }

    public void UpdatePlaybackState(bool isPlaying, double position)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                UpdatePlaybackStateMac(isPlaying ? 1 : 2, position);
            }
            catch
            {
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            _isPlaying = isPlaying;
            _currentPosition = ClampSeconds(position, _currentDuration);
            _hasPlaybackState = true;

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
#endif
        }
    }

    public void UpdatePosition(double position)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                UpdatePlaybackPositionMac(position);
            }
            catch
            {
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            _currentPosition = ClampSeconds(position, _currentDuration);

            try
            {
                TryInitWindowsControls();
                ApplyWindowsTimeline();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update Windows playback position: {ex}");
            }
#endif
        }
    }

    public void Dispose()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;

                try
                {
                    UpdatePlaybackStateMac(0, 0);
                }
                catch
                {
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            if (_attachedWindow is not null)
            {
                _attachedWindow.Opened -= AttachedWindow_Opened;
                _attachedWindow = null;
            }

            if (_smtc != null)
            {
                _smtc.ButtonPressed -= Smtc_ButtonPressed;
                _smtc.PlaybackPositionChangeRequested -= Smtc_PlaybackPositionChangeRequested;
                _smtc.PlaybackStatus = Windows.Media.MediaPlaybackStatus.Stopped;
            }
#endif
        }
    }

    // Mac P/Invoke
    private delegate void MediaCommandCallback(int command);
    private delegate void MediaSeekCallback(double position);
    private MediaCommandCallback? _macCallback;
    private MediaSeekCallback? _macSeekCallback;

    private static void MacCommandCallback(int command)
    {
        if (_instance == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            switch (command)
            {
                case 0: _instance._onPlay?.Invoke(); break;
                case 1: _instance._onPause?.Invoke(); break;
                case 2: _instance._onStop?.Invoke(); break;
                case 3: _instance._onToggle?.Invoke(); break;
                case 4: _instance._onNext?.Invoke(); break;
                case 5: _instance._onPrevious?.Invoke(); break;
            }
        });
    }

    private static void MacSeekCallback(double position)
    {
        if (_instance == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _instance._onSeek?.Invoke(position);
        });
    }

    [DllImport("MediaControls", EntryPoint = "InitMediaControls")]
    private static extern void InitMediaControls(MediaCommandCallback callback, MediaSeekCallback seekCallback);

    [DllImport("MediaControls", EntryPoint = "UpdateNowPlaying")]
    private static extern void UpdateNowPlayingMac(string title, string artist, double duration, double position);

    [DllImport("MediaControls", EntryPoint = "UpdatePlaybackState")]
    private static extern void UpdatePlaybackStateMac(int state, double position);

    [DllImport("MediaControls", EntryPoint = "UpdatePlaybackPosition")]
    private static extern void UpdatePlaybackPositionMac(double position);

    private static double SanitizeSeconds(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value;
    }

    private static double ClampSeconds(double value, double maxValue)
    {
        value = SanitizeSeconds(value);
        maxValue = SanitizeSeconds(maxValue);
        if (maxValue <= 0)
        {
            return value;
        }

        return Math.Min(value, maxValue);
    }
}
