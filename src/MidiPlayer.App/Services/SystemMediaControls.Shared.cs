using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MidiPlayer.App.Services;

public enum SystemMediaPlaybackState
{
    Stopped = 0,
    Paused = 1,
    Playing = 2
}

internal interface ISystemMediaControlsBackend : IDisposable
{
    void AttachWindow(Window window);
    void UpdateNowPlaying(string title, string artist, double duration, double position);
    void UpdatePlaybackState(SystemMediaPlaybackState state, double position);
    void UpdatePosition(double position);
    void UpdateVolume(double volume);
    void UpdateRate(double rate);
}

internal static class SystemMediaControlsBackendFactory
{
    public static ISystemMediaControlsBackend Create(SystemMediaControlsCallbacks callbacks)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacSystemMediaControlsBackend(callbacks);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsSystemMediaControlsBackend(callbacks);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new LinuxSystemMediaControlsBackend(callbacks);
        }

        return new NoopSystemMediaControlsBackend();
    }
}

internal sealed class SystemMediaControlsCallbacks
{
    private readonly Action _onPlay;
    private readonly Action _onPause;
    private readonly Action _onStop;
    private readonly Action _onToggle;
    private readonly Action<double> _onSeek;
    private readonly Action _onNext;
    private readonly Action _onPrevious;
    private readonly Func<double>? _getVolume;
    private readonly Action<double>? _onSetVolume;
    private readonly Func<double>? _getRate;
    private readonly Action<double>? _onSetRate;

    public SystemMediaControlsCallbacks(
        Action onPlay,
        Action onPause,
        Action onStop,
        Action onToggle,
        Action<double> onSeek,
        Action onNext,
        Action onPrevious,
        Func<double>? getVolume,
        Action<double>? onSetVolume,
        Func<double>? getRate,
        Action<double>? onSetRate)
    {
        _onPlay = onPlay;
        _onPause = onPause;
        _onStop = onStop;
        _onToggle = onToggle;
        _onSeek = onSeek;
        _onNext = onNext;
        _onPrevious = onPrevious;
        _getVolume = getVolume;
        _onSetVolume = onSetVolume;
        _getRate = getRate;
        _onSetRate = onSetRate;
    }

    public void Play() => Post(_onPlay);

    public void Pause() => Post(_onPause);

    public void Stop() => Post(_onStop);

    public void Toggle() => Post(_onToggle);

    public void Seek(double position)
    {
        double boundedPosition = SystemMediaControlsMath.SanitizeSeconds(position);
        Post(() => _onSeek(boundedPosition));
    }

    public void Next() => Post(_onNext);

    public void Previous() => Post(_onPrevious);

    public double GetVolumeOrDefault()
        => _getVolume is null ? 1.0 : SystemMediaControlsMath.ClampVolume(_getVolume());

    public void SetVolume(double volume)
    {
        if (_onSetVolume is null)
        {
            return;
        }

        double boundedVolume = SystemMediaControlsMath.ClampVolume(volume);
        Post(() => _onSetVolume(boundedVolume));
    }

    public double GetRateOrDefault()
        => _getRate is null ? 1.0 : SystemMediaControlsMath.ClampRate(_getRate());

    public void SetRate(double rate)
    {
        if (_onSetRate is null)
        {
            return;
        }

        double boundedRate = SystemMediaControlsMath.ClampRate(rate);
        Post(() => _onSetRate(boundedRate));
    }

    private static void Post(Action action)
        => Dispatcher.UIThread.Post(action);
}

internal static class SystemMediaControlsMath
{
    private const double MaxVolume = 2.0;
    private const double MinRate = 0.25;
    private const double MaxRate = 4.0;

    public static double SanitizeSeconds(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value;
    }

    public static double ClampSeconds(double value, double maxValue)
    {
        value = SanitizeSeconds(value);
        maxValue = SanitizeSeconds(maxValue);
        if (maxValue <= 0)
        {
            return value;
        }

        return Math.Min(value, maxValue);
    }

    public static double ClampVolume(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Clamp(value, 0, MaxVolume);
    }

    public static double ClampRate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Clamp(value, MinRate, MaxRate);
    }

    public static long ToMicroseconds(double seconds)
        => (long)Math.Round(SanitizeSeconds(seconds) * 1_000_000d);

    public static double FromMicroseconds(long microseconds)
        => microseconds <= 0 ? 0 : microseconds / 1_000_000d;
}
