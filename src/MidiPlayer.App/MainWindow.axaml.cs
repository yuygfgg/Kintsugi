using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly IBrush LoopEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#214B75"));
    private static readonly IBrush LoopEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush LoopEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush LoopDisabledBackgroundBrush = Brushes.Transparent;
    private static readonly IBrush LoopDisabledBorderBrush = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush LoopDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#A0A0A0"));

    private readonly BassMidiPlayer _player = new();
    private readonly AppSettings _settings;
    private readonly SystemMediaControls _mediaControls;
    private readonly DispatcherTimer _positionTimer;
    private bool _isScrubbing;
    private bool _isUpdatingPosition;
    private bool _isExporting;
    private bool _wasPlayingLastRefresh;
    private double _durationSeconds;
    private double _positionSeconds;
    private string _midiDisplayName = "No Track Loaded";
    private string _statusText = "Ready to play";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _settings = AppSettings.Load();

        _player.SampleRate = _settings.SampleRate;

        if (!string.IsNullOrEmpty(_settings.SoundFontPath) && File.Exists(_settings.SoundFontPath))
        {
            try
            {
                _player.LoadSoundFont(_settings.SoundFontPath);
            }
            catch
            {
                // Ignore initial load error for soundfont
            }
        }
        _player.SystemMode = _settings.SystemMode;

        _mediaControls = new SystemMediaControls(
            onPlay: () => { if (CanTogglePlayback && !_player.IsPlaying) OnPlayPauseClicked(this, new RoutedEventArgs()); },
            onPause: () => { if (CanTogglePlayback && _player.IsPlaying) OnPlayPauseClicked(this, new RoutedEventArgs()); },
            onStop: () => {
                if (CanTogglePlayback) {
                    _player.Pause();
                    _player.Seek(0);
                    StatusText = "Stopped";
                    RefreshTransport(true);
                    _mediaControls?.UpdatePlaybackState(false, 0);
                }
            },
            onToggle: () => { if (CanTogglePlayback) OnPlayPauseClicked(this, new RoutedEventArgs()); },
            onSeek: (pos) => { if (CanSeek) { _player.Seek(pos); _mediaControls?.UpdatePosition(pos); RefreshTransport(); } }
        );

        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => RefreshTransport());
        _positionTimer.Start();

        SeekSlider.AddHandler(PointerPressedEvent, OnSeekPointerPressed, RoutingStrategies.Tunnel);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekPointerReleased, RoutingStrategies.Tunnel);
        
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Closing += OnClosing;
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (CanTogglePlayback)
            {
                OnPlayPauseClicked(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public BassMidiPlayer Player => _player;

    public string MidiDisplayName
    {
        get => _midiDisplayName;
        private set => SetField(ref _midiDisplayName, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            if (SetField(ref _durationSeconds, value))
            {
                OnPropertyChanged(nameof(DurationText));
                OnPropertyChanged(nameof(CanSeek));
            }
        }
    }

    public double PositionSeconds
    {
        get => _positionSeconds;
        set
        {
            if (SetField(ref _positionSeconds, value))
            {
                OnPropertyChanged(nameof(PositionText));

                // If this change was not driven by the playback timer, it's a user seek.
                if (!_isUpdatingPosition && CanSeek)
                {
                    try
                    {
                        _player.Seek(value);
                        _mediaControls?.UpdatePosition(value);
                    }
                    catch (Exception ex)
                    {
                        StatusText = "Error: " + ex.Message;
                    }
                }
            }
        }
    }

    public string PositionText => FormatTime(PositionSeconds);

    public string DurationText => FormatTime(DurationSeconds);

    public string PlayPauseIcon => _player.IsPlaying ? "⏸" : "▶";

    public Thickness PlayPauseIconMargin => _player.IsPlaying ? new Thickness(0, 0, 0, 0) : new Thickness(2, 0, 0, 0);

    public bool CanTogglePlayback => _player.HasStream;

    public bool CanSeek => _player.HasStream && DurationSeconds > 0;

    public bool CanOpenMidi => !_isExporting;

    public bool CanOpenSettings => !_isExporting;

    public bool CanExport => _player.HasStream && !_isExporting;

    public string ExportButtonText => _isExporting ? "EXPORTING..." : "EXPORT WAV";

    public string CurrentBpmText => FormatBpm(_player.GetCurrentBpm());

    public double ReverbScalePercent
    {
        get => _player.ReverbScalePercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.ReverbScalePercent != intValue)
            {
                _player.ReverbScalePercent = intValue;
                OnPropertyChanged();
            }
        }
    }

    public double ChorusScalePercent
    {
        get => _player.ChorusScalePercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.ChorusScalePercent != intValue)
            {
                _player.ChorusScalePercent = intValue;
                OnPropertyChanged();
            }
        }
    }

    public IBrush LoopButtonBackground => _player.IsLooping ? LoopEnabledBackgroundBrush : LoopDisabledBackgroundBrush;

    public IBrush LoopButtonBorderBrush => _player.IsLooping ? LoopEnabledBorderBrush : LoopDisabledBorderBrush;

    public IBrush LoopButtonForeground => _player.IsLooping ? LoopEnabledForegroundBrush : LoopDisabledForegroundBrush;

    private async void OnOpenMidiClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanOpenMidi)
        {
            return;
        }

        var path = await PickSingleFileAsync(
            new FilePickerFileType("MIDI")
            {
                Patterns = ["*.mid", "*.midi", "*.kar", "*.rmi"],
                MimeTypes = ["audio/midi"]
            });

        if (path is null)
        {
            return;
        }

        try
        {
            _player.LoadMidi(path);
            var title = Path.GetFileNameWithoutExtension(path);
            MidiDisplayName = title;
            StatusText = "Playing";
            _player.Play();
            RefreshTransport(resetPosition: true);
            _mediaControls.UpdateNowPlaying(title, "Kintsugi Midi Player", DurationSeconds, PositionSeconds);
            _mediaControls.UpdatePlaybackState(true, PositionSeconds);
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanOpenSettings)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(_player);
        settingsWindow.ShowDialog(this);
    }

    private async void OnExportWavClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanExport || string.IsNullOrWhiteSpace(_player.MidiPath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_player.SoundFontPath))
        {
            StatusText = "Load a SoundFont before exporting.";
            return;
        }

        var dialog = new ExportWavWindow(_player.MidiPath, _player.SoundFontPath, _player.SampleRate);
        await dialog.ShowDialog(this);

        if (!dialog.WasConfirmed || dialog.ExportOptions is null)
        {
            return;
        }

        SetExportingState(true);
        StatusText = $"Exporting WAV at {dialog.ExportOptions.SampleRate / 1000d:0.###} kHz...";

        try
        {
            await Task.Run(() => _player.ExportCurrentMidiToWav(dialog.ExportOptions));
            StatusText = $"WAV exported: {Path.GetFileName(dialog.ExportOptions.OutputPath)}";
        }
        catch (Exception ex)
        {
            StatusText = "Export error: " + ex.Message;
        }
        finally
        {
            SetExportingState(false);
            RefreshTransport();
        }
    }

    private void OnPlayPauseClicked(object? sender, RoutedEventArgs e)
    {
        if (!_player.HasStream)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                StatusText = "Paused";
                _mediaControls.UpdatePlaybackState(false, PositionSeconds);
            }
            else
            {
                if (IsPlaybackAtEnd())
                {
                    _player.Seek(0);
                    _isUpdatingPosition = true;
                    PositionSeconds = 0;
                    _isUpdatingPosition = false;
                }

                _player.Play();
                StatusText = "Playing";
                _mediaControls.UpdatePlaybackState(true, PositionSeconds);
            }

            OnPropertyChanged(nameof(PlayPauseIcon));
            OnPropertyChanged(nameof(PlayPauseIconMargin));
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void OnLoopClicked(object? sender, RoutedEventArgs e)
    {
        _player.IsLooping = !_player.IsLooping;
        OnPropertyChanged(nameof(LoopButtonBackground));
        OnPropertyChanged(nameof(LoopButtonBorderBrush));
        OnPropertyChanged(nameof(LoopButtonForeground));
    }

    private void OnReverbSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        ReverbScalePercent = BassMidiPlayer.DefaultEffectScalePercent;
        e.Handled = true;
    }

    private void OnChorusSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        ChorusScalePercent = BassMidiPlayer.DefaultEffectScalePercent;
        e.Handled = true;
    }

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!CanSeek)
        {
            return;
        }
        _isScrubbing = true;
    }

    private void OnSeekPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isScrubbing || !CanSeek)
        {
            return;
        }
        _isScrubbing = false;
        
        try
        {
            _player.Seek(PositionSeconds);
            RefreshTransport();
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void RefreshTransport(bool resetPosition = false)
    {
        var isPlaying = _player.IsPlaying;

        DurationSeconds = _player.GetDurationSeconds();

        if (resetPosition)
        {
            _isUpdatingPosition = true;
            PositionSeconds = 0;
            _isUpdatingPosition = false;
        }
        else if (!_isScrubbing)
        {
            _isUpdatingPosition = true;
            PositionSeconds = _player.GetPositionSeconds();
            _isUpdatingPosition = false;
        }

        if (_wasPlayingLastRefresh && !isPlaying && IsPlaybackAtEnd() && !_player.IsLooping)
        {
            StatusText = "Finished";
            _mediaControls.UpdatePlaybackState(false, PositionSeconds);
        }

        _wasPlayingLastRefresh = isPlaying;

        OnPropertyChanged(nameof(CanTogglePlayback));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(CanOpenMidi));
        OnPropertyChanged(nameof(CanOpenSettings));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(PlayPauseIcon));
        OnPropertyChanged(nameof(PlayPauseIconMargin));
        OnPropertyChanged(nameof(CurrentBpmText));
        OnPropertyChanged(nameof(LoopButtonBackground));
        OnPropertyChanged(nameof(LoopButtonBorderBrush));
        OnPropertyChanged(nameof(LoopButtonForeground));
    }

    private async Task<string?> PickSingleFileAsync(FilePickerFileType fileType)
    {
        if (StorageProvider is null || !StorageProvider.CanOpen)
        {
            StatusText = "File picker not supported.";
            return null;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a file",
            AllowMultiple = false,
            FileTypeFilter = [fileType]
        });

        if (files.Count == 0)
        {
            return null;
        }

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText = "Only local files supported.";
            return null;
        }

        return path;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExporting)
        {
            e.Cancel = true;
            StatusText = "Wait for the WAV export to finish.";
            return;
        }

        _positionTimer.Stop();
        _mediaControls.Dispose();
        _player.Dispose();
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return "00:00";
        }

        var value = TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private static string FormatBpm(double bpm)
    {
        if (bpm <= 0 || double.IsNaN(bpm) || double.IsInfinity(bpm))
        {
            return "--";
        }

        return bpm >= 100
            ? bpm.ToString("0")
            : bpm.ToString("0.0");
    }

    private bool IsPlaybackAtEnd()
        => DurationSeconds > 0 && PositionSeconds >= Math.Max(0, DurationSeconds - 0.05);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void SetExportingState(bool isExporting)
    {
        if (_isExporting == isExporting)
        {
            return;
        }

        _isExporting = isExporting;
        OnPropertyChanged(nameof(CanOpenMidi));
        OnPropertyChanged(nameof(CanOpenSettings));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportButtonText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
