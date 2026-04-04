using System;
using System.Runtime.InteropServices;

namespace MidiPlayer.App.Services;

public class SystemMediaControls : IDisposable
{
    private readonly Action _onPlay;
    private readonly Action _onPause;
    private readonly Action _onStop;
    private readonly Action _onToggle;
    private readonly Action<double> _onSeek;

    private static SystemMediaControls? _instance;

#if WINDOWS
    private Windows.Media.Playback.SystemMediaTransportControls? _smtc;
#endif

    public SystemMediaControls(Action onPlay, Action onPause, Action onStop, Action onToggle, Action<double> onSeek)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onStop = onStop;
        _onToggle = onToggle;
        _onSeek = onSeek;
        _instance = this;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            InitMacControls();
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            InitWindowsControls();
        }
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

    private void InitWindowsControls()
    {
#if WINDOWS
        try
        {
            // For Windows 10+, requires net10.0-windows TFM
            _smtc = Windows.Media.Playback.SystemMediaTransportControls.GetForCurrentView();
            _smtc.IsPlayEnabled = true;
            _smtc.IsPauseEnabled = true;
            _smtc.IsStopEnabled = true;
            _smtc.ButtonPressed += Smtc_ButtonPressed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to init Windows SMTC: {ex.Message}");
        }
#endif
    }

#if WINDOWS
    private void Smtc_ButtonPressed(Windows.Media.Playback.SystemMediaTransportControls sender, Windows.Media.Playback.SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case Windows.Media.Playback.SystemMediaTransportControlsButton.Play:
                _onPlay?.Invoke();
                break;
            case Windows.Media.Playback.SystemMediaTransportControlsButton.Pause:
                _onPause?.Invoke();
                break;
            case Windows.Media.Playback.SystemMediaTransportControlsButton.Stop:
                _onStop?.Invoke();
                break;
        }
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
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            try
            {
                if (_smtc != null)
                {
                    var updater = _smtc.DisplayUpdater;
                    updater.Type = Windows.Media.MediaPlaybackType.Music;
                    updater.MusicProperties.Title = title;
                    updater.MusicProperties.Artist = artist;
                    updater.Update();

                    var timelineProperties = new Windows.Media.SystemMediaTransportControlsTimelineProperties();
                    timelineProperties.StartTime = TimeSpan.Zero;
                    timelineProperties.MinSeekTime = TimeSpan.Zero;
                    timelineProperties.Position = TimeSpan.FromSeconds(position);
                    timelineProperties.MaxSeekTime = TimeSpan.FromSeconds(duration);
                    timelineProperties.EndTime = TimeSpan.FromSeconds(duration);
                    _smtc.UpdateTimelineProperties(timelineProperties);
                }
            }
            catch { }
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
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            try
            {
                if (_smtc != null)
                {
                    _smtc.PlaybackStatus = isPlaying
                        ? Windows.Media.Playback.MediaPlaybackStatus.Playing
                        : Windows.Media.Playback.MediaPlaybackStatus.Paused;
                }
            }
            catch { }
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
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            try
            {
                if (_smtc != null)
                {
                    // TODO
                }
            }
            catch { }
#endif
        }
    }

    public void Dispose()
    {
        _instance = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                UpdatePlaybackStateMac(0, 0);
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
#if WINDOWS
            if (_smtc != null)
            {
                _smtc.ButtonPressed -= Smtc_ButtonPressed;
                _smtc.PlaybackStatus = Windows.Media.Playback.MediaPlaybackStatus.Stopped;
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
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            switch (command)
            {
                case 0: _instance._onPlay?.Invoke(); break;
                case 1: _instance._onPause?.Invoke(); break;
                case 2: _instance._onStop?.Invoke(); break;
                case 3: _instance._onToggle?.Invoke(); break;
            }
        });
    }

    private static void MacSeekCallback(double position)
    {
        if (_instance == null) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
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
}
