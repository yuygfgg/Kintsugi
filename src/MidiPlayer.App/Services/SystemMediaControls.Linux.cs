using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Tmds.DBus.Protocol;
using Tmds.DBus.SourceGenerator;

namespace MidiPlayer.App.Services;

internal sealed class LinuxSystemMediaControlsBackend : ISystemMediaControlsBackend
{
    private const string MprisObjectPath = "/org/mpris/MediaPlayer2";
    private const string PlayerIdentity = "Kintsugi Midi Player";
    private const string DesktopEntryName = "kintsugi-midiplayer";
    private const string NoTrackPath = "/org/mpris/MediaPlayer2/TrackList/NoTrack";
    private const string ServiceNamePrefix = "org.mpris.MediaPlayer2.kintsugi.instance";

    private static readonly ObjectPath NoTrackObjectPath = new(NoTrackPath);

    private readonly SystemMediaControlsCallbacks _callbacks;
    private readonly DBusConnection? _connection;
    private readonly PathHandler? _pathHandler;
    private readonly MediaPlayerRootHandler? _mediaPlayerHandler;
    private readonly MediaPlayerPlayerHandler? _playerHandler;
    private readonly Task _initializationTask;
    private readonly object _sync = new();

    private bool _emitSignals;
    private bool _hasNowPlaying;
    private int _trackRevision;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private double _duration;
    private double _rate = 1.0;
    private double _volume = 1.0;

    public LinuxSystemMediaControlsBackend(SystemMediaControlsCallbacks callbacks)
    {
        _callbacks = callbacks;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || DBusAddress.Session is null)
        {
            _initializationTask = Task.CompletedTask;
            return;
        }

        _connection = new DBusConnection(DBusAddress.Session);
        _pathHandler = new PathHandler(MprisObjectPath);
        _mediaPlayerHandler = new MediaPlayerRootHandler(this) { PathHandler = _pathHandler };
        _playerHandler = new MediaPlayerPlayerHandler(this) { PathHandler = _pathHandler };
        _pathHandler.Add(_mediaPlayerHandler);
        _pathHandler.Add(_playerHandler);

        _mediaPlayerHandler.CanQuit = false;
        _mediaPlayerHandler.CanRaise = false;
        _mediaPlayerHandler.HasTrackList = false;
        _mediaPlayerHandler.Identity = PlayerIdentity;
        _mediaPlayerHandler.DesktopEntry = DesktopEntryName;
        _mediaPlayerHandler.SupportedUriSchemes = ["file"];
        _mediaPlayerHandler.SupportedMimeTypes =
        [
            "audio/midi",
            "audio/x-midi",
            "audio/mid",
            "audio/sp-midi",
            "audio/x-sp-midi",
            "application/x-midi"
        ];

        _playerHandler.PlaybackStatus = ToMprisPlaybackStatus(SystemMediaPlaybackState.Stopped);
        _playerHandler.Metadata = CreateMetadata(NoTrackObjectPath, string.Empty, string.Empty, 0);
        _playerHandler.Position = 0;
        _playerHandler.MinimumRate = 0.25;
        _playerHandler.MaximumRate = 4.0;
        _playerHandler.CanGoNext = true;
        _playerHandler.CanGoPrevious = true;
        _playerHandler.CanPlay = false;
        _playerHandler.CanPause = false;
        _playerHandler.CanSeek = false;
        _playerHandler.CanControl = true;

        _rate = _callbacks.GetRateOrDefault();
        _volume = _callbacks.GetVolumeOrDefault();

        _initializationTask = InitializeAsync();
    }

    public void AttachWindow(Window window)
    {
    }

    public void UpdateNowPlaying(string title, string artist, double duration, double position)
    {
        if (_playerHandler is null)
        {
            return;
        }

        title ??= string.Empty;
        artist ??= string.Empty;
        duration = SystemMediaControlsMath.SanitizeSeconds(duration);
        position = SystemMediaControlsMath.ClampSeconds(position, duration);

        lock (_sync)
        {
            bool changed = !_hasNowPlaying
                || !string.Equals(_title, title, StringComparison.Ordinal)
                || !string.Equals(_artist, artist, StringComparison.Ordinal)
                || Math.Abs(_duration - duration) > 0.001;

            if (changed)
            {
                _trackRevision++;
                _title = title;
                _artist = artist;
                _duration = duration;
                _hasNowPlaying = true;
                Metadata = CreateMetadata(CreateTrackObjectPath(_trackRevision), title, artist, duration);
            }

            Position = SystemMediaControlsMath.ToMicroseconds(position);
            CanPlay = true;
            CanPause = true;
            CanSeek = duration > 0;
        }
    }

    public void UpdatePlaybackState(SystemMediaPlaybackState state, double position)
    {
        if (_playerHandler is null)
        {
            return;
        }

        lock (_sync)
        {
            PlaybackStatus = ToMprisPlaybackStatus(state);
            Position = SystemMediaControlsMath.ToMicroseconds(position);
        }
    }

    public void UpdatePosition(double position)
    {
        if (_playerHandler is null)
        {
            return;
        }

        lock (_sync)
        {
            Position = SystemMediaControlsMath.ToMicroseconds(position);
            EmitSeeked(Position);
        }
    }

    public void UpdateVolume(double volume)
    {
        if (_playerHandler is null)
        {
            return;
        }

        lock (_sync)
        {
            Volume = SystemMediaControlsMath.ClampVolume(volume);
        }
    }

    public void UpdateRate(double rate)
    {
        if (_playerHandler is null)
        {
            return;
        }

        lock (_sync)
        {
            Rate = SystemMediaControlsMath.ClampRate(rate);
        }
    }

    public void Dispose()
    {
        _emitSignals = false;
        _connection?.Dispose();
    }

    private async Task InitializeAsync()
    {
        if (_connection is null || _pathHandler is null)
        {
            return;
        }

        try
        {
            await _connection.ConnectAsync().ConfigureAwait(false);
            _connection.AddMethodHandler(_pathHandler);
            await _connection.RequestNameAsync($"{ServiceNamePrefix}{Environment.ProcessId}").ConfigureAwait(false);
            _emitSignals = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize Linux media controls: {ex}");
        }
    }

    private string PlaybackStatus
    {
        get => _playerHandler?.PlaybackStatus ?? ToMprisPlaybackStatus(SystemMediaPlaybackState.Stopped);
        set
        {
            if (_playerHandler is null || string.Equals(_playerHandler.PlaybackStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            _playerHandler.PlaybackStatus = value;
            EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.PlaybackStatus), value);
        }
    }

    private double Rate
    {
        get => _rate;
        set
        {
            value = SystemMediaControlsMath.ClampRate(value);
            if (Math.Abs(_rate - value) < 0.0001)
            {
                return;
            }

            _rate = value;
            if (_playerHandler is not null)
            {
                EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.Rate), value);
            }
        }
    }

    private double Volume
    {
        get => _volume;
        set
        {
            value = SystemMediaControlsMath.ClampVolume(value);
            if (Math.Abs(_volume - value) < 0.0001)
            {
                return;
            }

            _volume = value;
            if (_playerHandler is not null)
            {
                EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.Volume), value);
            }
        }
    }

    private Dictionary<string, VariantValue> Metadata
    {
        get => _playerHandler?.Metadata ?? CreateMetadata(NoTrackObjectPath, string.Empty, string.Empty, 0);
        set
        {
            if (_playerHandler is null)
            {
                return;
            }

            _playerHandler.Metadata = value;
            EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.Metadata), new Dict<string, VariantValue>(value));
        }
    }

    private long Position
    {
        get => _playerHandler?.Position ?? 0;
        set
        {
            if (_playerHandler is null)
            {
                return;
            }

            _playerHandler.Position = Math.Max(0, value);
        }
    }

    private bool CanPlay
    {
        get => _playerHandler?.CanPlay ?? false;
        set
        {
            if (_playerHandler is null || _playerHandler.CanPlay == value)
            {
                return;
            }

            _playerHandler.CanPlay = value;
            EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.CanPlay), value);
        }
    }

    private bool CanPause
    {
        get => _playerHandler?.CanPause ?? false;
        set
        {
            if (_playerHandler is null || _playerHandler.CanPause == value)
            {
                return;
            }

            _playerHandler.CanPause = value;
            EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.CanPause), value);
        }
    }

    private bool CanSeek
    {
        get => _playerHandler?.CanSeek ?? false;
        set
        {
            if (_playerHandler is null || _playerHandler.CanSeek == value)
            {
                return;
            }

            _playerHandler.CanSeek = value;
            EmitPropertyChanged(_playerHandler.InterfaceName, nameof(_playerHandler.CanSeek), value);
        }
    }

    private ObjectPath CurrentTrackId
    {
        get
        {
            if (_playerHandler?.Metadata is null || !_playerHandler.Metadata.TryGetValue("mpris:trackid", out VariantValue value))
            {
                return NoTrackObjectPath;
            }

            return value.GetObjectPath();
        }
    }

    private void EmitPropertyChanged(string interfaceName, string propertyName, VariantValue value)
    {
        if (!_emitSignals || _connection is null)
        {
            return;
        }

        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: MprisObjectPath,
            @interface: "org.freedesktop.DBus.Properties",
            member: "PropertiesChanged",
            signature: "sa{sv}as");
        writer.WriteString(interfaceName);
        writer.WriteDictionary([KeyValuePair.Create(propertyName, value)]);
        writer.WriteArray(Array.Empty<string>());
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private void EmitSeeked(long position)
    {
        if (!_emitSignals || _connection is null)
        {
            return;
        }

        using MessageWriter writer = _connection.GetMessageWriter();
        writer.WriteSignalHeader(
            destination: null,
            path: MprisObjectPath,
            @interface: "org.mpris.MediaPlayer2.Player",
            member: "Seeked",
            signature: "x");
        writer.WriteInt64(position);
        _connection.TrySendMessage(writer.CreateMessage());
    }

    private static Dictionary<string, VariantValue> CreateMetadata(ObjectPath trackId, string title, string artist, double duration)
    {
        var metadata = new Dictionary<string, VariantValue>
        {
            ["mpris:trackid"] = trackId
        };

        if (!string.IsNullOrWhiteSpace(title))
        {
            metadata["xesam:title"] = title;
        }

        if (!string.IsNullOrWhiteSpace(artist))
        {
            metadata["xesam:artist"] = VariantValue.Array(new[] { artist });
        }

        long length = SystemMediaControlsMath.ToMicroseconds(duration);
        if (length > 0)
        {
            metadata["mpris:length"] = length;
        }

        return metadata;
    }

    private static ObjectPath CreateTrackObjectPath(int revision)
        => new($"/io/github/kintsugi/MidiPlayer/track/{Math.Max(1, revision)}");

    private static string ToMprisPlaybackStatus(SystemMediaPlaybackState state)
        => state switch
        {
            SystemMediaPlaybackState.Playing => "Playing",
            SystemMediaPlaybackState.Paused => "Paused",
            _ => "Stopped"
        };

    private void SetRateFromRemote(double value)
    {
        Rate = value;
        _callbacks.SetRate(_rate);
    }

    private void SetVolumeFromRemote(double value)
    {
        Volume = value;
        _callbacks.SetVolume(_volume);
    }

    private sealed class MediaPlayerRootHandler(LinuxSystemMediaControlsBackend owner) : OrgMprisMediaPlayer2Handler
    {
        private readonly LinuxSystemMediaControlsBackend _owner = owner;

        public override Connection Connection => _owner._connection?.AsConnection() ?? throw new InvalidOperationException("D-Bus connection is not available.");

        protected override ValueTask OnRaiseAsync(Message request)
            => ValueTask.CompletedTask;

        protected override ValueTask OnQuitAsync(Message request)
            => ValueTask.CompletedTask;
    }

    private sealed class MediaPlayerPlayerHandler(LinuxSystemMediaControlsBackend owner) : OrgMprisMediaPlayer2PlayerHandler
    {
        private readonly LinuxSystemMediaControlsBackend _owner = owner;

        public override Connection Connection => _owner._connection?.AsConnection() ?? throw new InvalidOperationException("D-Bus connection is not available.");

        public override double Rate
        {
            get => _owner._rate;
            set => _owner.SetRateFromRemote(value);
        }

        public override double Volume
        {
            get => _owner._volume;
            set => _owner.SetVolumeFromRemote(value);
        }

        protected override ValueTask OnNextAsync(Message request)
        {
            _owner._callbacks.Next();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnPreviousAsync(Message request)
        {
            _owner._callbacks.Previous();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnPauseAsync(Message request)
        {
            _owner.PlaybackStatus = "Paused";
            _owner._callbacks.Pause();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnPlayPauseAsync(Message request)
        {
            _owner.PlaybackStatus = _owner.PlaybackStatus == "Playing" ? "Paused" : "Playing";
            _owner._callbacks.Toggle();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnStopAsync(Message request)
        {
            _owner.PlaybackStatus = "Stopped";
            _owner.Position = 0;
            _owner.EmitSeeked(0);
            _owner._callbacks.Stop();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnPlayAsync(Message request)
        {
            _owner.PlaybackStatus = "Playing";
            _owner._callbacks.Play();
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnSeekAsync(Message request, long offset)
        {
            if (!CanSeek)
            {
                return ValueTask.CompletedTask;
            }

            long targetPosition = Math.Max(0, Position + offset);
            _owner.Position = targetPosition;
            _owner.EmitSeeked(targetPosition);
            _owner._callbacks.Seek(SystemMediaControlsMath.FromMicroseconds(targetPosition));
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnSetPositionAsync(Message request, ObjectPath trackId, long position)
        {
            if (!CanSeek || trackId != _owner.CurrentTrackId)
            {
                return ValueTask.CompletedTask;
            }

            long targetPosition = Math.Max(0, position);
            _owner.Position = targetPosition;
            _owner.EmitSeeked(targetPosition);
            _owner._callbacks.Seek(SystemMediaControlsMath.FromMicroseconds(targetPosition));
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnOpenUriAsync(Message request, string uri)
            => ValueTask.CompletedTask;
    }
}
