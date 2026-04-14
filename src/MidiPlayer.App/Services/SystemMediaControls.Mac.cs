using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace MidiPlayer.App.Services;

internal sealed class MacSystemMediaControlsBackend : ISystemMediaControlsBackend
{
    private readonly SystemMediaControlsCallbacks _callbacks;
    private readonly MediaCommandCallback _commandCallback;
    private readonly MediaSeekCallback _seekCallback;
    private readonly bool _isAvailable;

    public MacSystemMediaControlsBackend(SystemMediaControlsCallbacks callbacks)
    {
        _callbacks = callbacks;
        _commandCallback = HandleCommand;
        _seekCallback = HandleSeek;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        try
        {
            InitMediaControls(_commandCallback, _seekCallback);
            _isAvailable = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to init Mac media controls: {ex.Message}");
        }
    }

    public void AttachWindow(Window window)
    {
    }

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
    {
        if (!_isAvailable)
        {
            return;
        }

        try
        {
            UpdateNowPlayingMac(
                title,
                artist,
                SystemMediaControlsMath.SanitizeSeconds(duration),
                SystemMediaControlsMath.ClampSeconds(position, duration));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Mac now playing metadata: {ex.Message}");
        }
    }

    public void UpdatePlaybackState(SystemMediaPlaybackState state, double position)
    {
        if (!_isAvailable)
        {
            return;
        }

        try
        {
            UpdatePlaybackStateMac(MapState(state), SystemMediaControlsMath.SanitizeSeconds(position));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Mac playback state: {ex.Message}");
        }
    }

    public void UpdatePosition(double position)
    {
        if (!_isAvailable)
        {
            return;
        }

        try
        {
            UpdatePlaybackPositionMac(SystemMediaControlsMath.SanitizeSeconds(position));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update Mac playback position: {ex.Message}");
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
        if (!_isAvailable)
        {
            return;
        }

        try
        {
            UpdatePlaybackStateMac(MapState(SystemMediaPlaybackState.Stopped), 0);
        }
        catch
        {
        }
    }

    private void HandleCommand(int command)
    {
        switch (command)
        {
            case 0:
                _callbacks.Play();
                break;
            case 1:
                _callbacks.Pause();
                break;
            case 2:
                _callbacks.Stop();
                break;
            case 3:
                _callbacks.Toggle();
                break;
            case 4:
                _callbacks.Next();
                break;
            case 5:
                _callbacks.Previous();
                break;
        }
    }

    private void HandleSeek(double position)
        => _callbacks.Seek(position);

    private static int MapState(SystemMediaPlaybackState state)
        => state switch
        {
            SystemMediaPlaybackState.Playing => 1,
            SystemMediaPlaybackState.Paused => 2,
            _ => 0
        };

    private delegate void MediaCommandCallback(int command);
    private delegate void MediaSeekCallback(double position);

    [DllImport("MediaControls", EntryPoint = "InitMediaControls")]
    private static extern void InitMediaControls(MediaCommandCallback callback, MediaSeekCallback seekCallback);

    [DllImport("MediaControls", EntryPoint = "UpdateNowPlaying")]
    private static extern void UpdateNowPlayingMac(string title, string artist, double duration, double position);

    [DllImport("MediaControls", EntryPoint = "UpdatePlaybackState")]
    private static extern void UpdatePlaybackStateMac(int state, double position);

    [DllImport("MediaControls", EntryPoint = "UpdatePlaybackPosition")]
    private static extern void UpdatePlaybackPositionMac(double position);
}
