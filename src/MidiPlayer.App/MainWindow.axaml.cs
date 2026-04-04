using System;
using System.ComponentModel;
using System.Globalization;
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
using Avalonia.VisualTree;
using MidiPlayer.App.Controls;
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
    private static readonly IBrush MixerEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#214B75"));
    private static readonly IBrush MixerEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush MixerEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush MixerDisabledBackgroundBrush = Brushes.Transparent;
    private static readonly IBrush MixerDisabledBorderBrush = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush MixerDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#A0A0A0"));
    private const double ChannelMixerPopupWidth = 224;
    private const double ChannelMixerPopupHeight = 206;
    private const double ChannelMixerPopupPointerWidth = 20;
    private const double ChannelMixerPopupPointerInset = 12;
    private const double ChannelMixerPopupPointerHeight = 10;
    private const double ChannelMixerPopupCornerRadius = 8;

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
    private double _channelMixerPopupLeft;
    private double _channelMixerPopupPointerLeft;
    private string _midiDisplayName = "No Track Loaded";
    private string _statusText = "Ready to play";
    private string _channelMixerPopupOutlinePathData = string.Empty;
    private ChannelMixStrip? _selectedChannelMixStrip;
    private bool _isChannelMixerPopupOpen;
    private bool _isGlobalMixerPopupOpen;
    private int _selectedChannelIndex = -1;

    public MainWindow()
    {
        InitializeComponent();
        ChannelMixRows = CreateChannelMixRows();
        DataContext = this;
        ChannelMonitorView.SizeChanged += (_, _) => UpdateChannelMixerPopupPosition();

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
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, true);
        
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

    public ChannelMixStrip[] ChannelMixRows { get; }

    public ChannelMixStrip? SelectedChannelMixStrip
    {
        get => _selectedChannelMixStrip;
        private set => SetField(ref _selectedChannelMixStrip, value);
    }

    public bool IsChannelMixerPopupOpen
    {
        get => _isChannelMixerPopupOpen;
        private set => SetField(ref _isChannelMixerPopupOpen, value);
    }

    public double ChannelMixerPopupLeft
    {
        get => _channelMixerPopupLeft;
        private set => SetField(ref _channelMixerPopupLeft, value);
    }

    public double ChannelMixerPopupPointerLeft
    {
        get => _channelMixerPopupPointerLeft;
        private set => SetField(ref _channelMixerPopupPointerLeft, value);
    }

    public string ChannelMixerPopupOutlinePathData
    {
        get => _channelMixerPopupOutlinePathData;
        private set => SetField(ref _channelMixerPopupOutlinePathData, value);
    }

    public bool IsGlobalMixerPopupOpen
    {
        get => _isGlobalMixerPopupOpen;
        private set
        {
            if (SetField(ref _isGlobalMixerPopupOpen, value))
            {
                OnPropertyChanged(nameof(GlobalMixerButtonBackground));
                OnPropertyChanged(nameof(GlobalMixerButtonBorderBrush));
                OnPropertyChanged(nameof(GlobalMixerButtonForeground));
            }
        }
    }

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

    public double MasterVolumePercent
    {
        get => _player.MasterVolumePercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.MasterVolumePercent != intValue)
            {
                _player.MasterVolumePercent = intValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MasterVolumePercentText));
            }
        }
    }

    public string MasterVolumePercentText => FormatPercent(_player.MasterVolumePercent);

    public double ReverbReturnPercent
    {
        get => _player.ReverbReturnPercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.ReverbReturnPercent != intValue)
            {
                _player.ReverbReturnPercent = intValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReverbReturnPercentText));
            }
        }
    }

    public string ReverbReturnPercentText => FormatPercent(_player.ReverbReturnPercent);

    public double ChorusReturnPercent
    {
        get => _player.ChorusReturnPercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.ChorusReturnPercent != intValue)
            {
                _player.ChorusReturnPercent = intValue;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChorusReturnPercentText));
            }
        }
    }

    public string ChorusReturnPercentText => FormatPercent(_player.ChorusReturnPercent);

    public IBrush LoopButtonBackground => _player.IsLooping ? LoopEnabledBackgroundBrush : LoopDisabledBackgroundBrush;

    public IBrush LoopButtonBorderBrush => _player.IsLooping ? LoopEnabledBorderBrush : LoopDisabledBorderBrush;

    public IBrush LoopButtonForeground => _player.IsLooping ? LoopEnabledForegroundBrush : LoopDisabledForegroundBrush;

    public IBrush GlobalMixerButtonBackground => IsGlobalMixerPopupOpen ? MixerEnabledBackgroundBrush : MixerDisabledBackgroundBrush;

    public IBrush GlobalMixerButtonBorderBrush => IsGlobalMixerPopupOpen ? MixerEnabledBorderBrush : MixerDisabledBorderBrush;

    public IBrush GlobalMixerButtonForeground => IsGlobalMixerPopupOpen ? MixerEnabledForegroundBrush : MixerDisabledForegroundBrush;

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
        CloseChannelMixerPopup();
        _player.IsLooping = !_player.IsLooping;
        OnPropertyChanged(nameof(LoopButtonBackground));
        OnPropertyChanged(nameof(LoopButtonBorderBrush));
        OnPropertyChanged(nameof(LoopButtonForeground));
    }

    private void OnGlobalMixerClicked(object? sender, RoutedEventArgs e)
    {
        CloseChannelMixerPopup();
        IsGlobalMixerPopupOpen = !IsGlobalMixerPopupOpen;
    }

    private void OnChannelMixerRequested(object? sender, ChannelMixerRequestedEventArgs e)
    {
        if (e.Channel == _selectedChannelIndex && IsChannelMixerPopupOpen)
        {
            CloseChannelMixerPopup();
            return;
        }

        _selectedChannelIndex = e.Channel;
        SelectedChannelMixStrip = ChannelMixRows[e.Channel];
        IsChannelMixerPopupOpen = true;
        IsGlobalMixerPopupOpen = false;
        UpdateChannelMixerPopupPosition();
    }

    private void OnMasterVolumeSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        MasterVolumePercent = BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnChannelVolumeSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ChannelMixStrip strip })
        {
            strip.ResetVolume();
        }

        e.Handled = true;
    }

    private void OnChannelReverbSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ChannelMixStrip strip })
        {
            strip.ResetReverbSend();
        }

        e.Handled = true;
    }

    private void OnChannelChorusSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: ChannelMixStrip strip })
        {
            strip.ResetChorusSend();
        }

        e.Handled = true;
    }

    private void OnReverbSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        ReverbReturnPercent = BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnChorusSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        ChorusReturnPercent = BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnSeekPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseChannelMixerPopup();
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var sourceVisual = e.Source as Visual;
        bool clickedChannelMixer = IsWithinVisual(sourceVisual, ChannelMonitorView) || IsWithinVisual(sourceVisual, ChannelMixerPopupHost);
        bool clickedGlobalMixer = IsWithinVisual(sourceVisual, GlobalMixerButtonHost);

        if (!clickedChannelMixer)
        {
            CloseChannelMixerPopup();
        }

        if (!clickedGlobalMixer)
        {
            IsGlobalMixerPopupOpen = false;
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
        CloseChannelMixerPopup();
        IsGlobalMixerPopupOpen = false;
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

    private static string FormatPercent(double percent)
        => $"{Math.Round(percent):0}%";

    private bool IsPlaybackAtEnd()
        => DurationSeconds > 0 && PositionSeconds >= Math.Max(0, DurationSeconds - 0.05);

    private void CloseChannelMixerPopup()
    {
        _selectedChannelIndex = -1;
        SelectedChannelMixStrip = null;
        IsChannelMixerPopupOpen = false;
    }

    private void UpdateChannelMixerPopupPosition()
    {
        if (!IsChannelMixerPopupOpen || _selectedChannelIndex < 0)
        {
            return;
        }

        double width = ChannelMonitorView.Bounds.Width;
        if (width <= 0)
        {
            ChannelMixerPopupLeft = 0;
            ChannelMixerPopupPointerLeft = ChannelMixerPopupPointerInset;
            ChannelMixerPopupOutlinePathData = CreateChannelMixerPopupOutlinePath(ChannelMixerPopupPointerLeft);
            return;
        }

        double itemWidth = width / 16d;
        double channelCenter = (_selectedChannelIndex * itemWidth) + (itemWidth / 2d);
        double centeredLeft = channelCenter - (ChannelMixerPopupWidth / 2d);
        double maxLeft = Math.Max(0, width - ChannelMixerPopupWidth);
        ChannelMixerPopupLeft = Math.Clamp(centeredLeft, 0, maxLeft);
        double pointerLeft = channelCenter - ChannelMixerPopupLeft - (ChannelMixerPopupPointerWidth / 2d);
        double maxPointerLeft = Math.Max(ChannelMixerPopupPointerInset, ChannelMixerPopupWidth - ChannelMixerPopupPointerWidth - ChannelMixerPopupPointerInset);
        ChannelMixerPopupPointerLeft = Math.Clamp(pointerLeft, ChannelMixerPopupPointerInset, maxPointerLeft);
        ChannelMixerPopupOutlinePathData = CreateChannelMixerPopupOutlinePath(ChannelMixerPopupPointerLeft);
    }

    private ChannelMixStrip[] CreateChannelMixRows()
    {
        var rows = new ChannelMixStrip[16];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new ChannelMixStrip(_player, i);
        }

        return rows;
    }

    private static bool IsWithinVisual(Visual? source, Visual? target)
    {
        for (var current = source; current != null; current = current.GetVisualParent())
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateChannelMixerPopupOutlinePath(double pointerLeft)
    {
        double width = ChannelMixerPopupWidth;
        double height = ChannelMixerPopupHeight;
        double radius = ChannelMixerPopupCornerRadius;
        double bodyTop = ChannelMixerPopupPointerHeight;
        double right = width;
        double bottom = height;
        double pointerCenter = pointerLeft + (ChannelMixerPopupPointerWidth / 2d);
        double pointerRight = pointerLeft + ChannelMixerPopupPointerWidth;

        return string.Create(CultureInfo.InvariantCulture, $"M {FormatCoord(radius)} {FormatCoord(bodyTop)} " +
            $"L {FormatCoord(pointerLeft)} {FormatCoord(bodyTop)} " +
            $"L {FormatCoord(pointerCenter)} 0 " +
            $"L {FormatCoord(pointerRight)} {FormatCoord(bodyTop)} " +
            $"L {FormatCoord(right - radius)} {FormatCoord(bodyTop)} " +
            $"A {FormatCoord(radius)} {FormatCoord(radius)} 0 0 1 {FormatCoord(right)} {FormatCoord(bodyTop + radius)} " +
            $"L {FormatCoord(right)} {FormatCoord(bottom - radius)} " +
            $"A {FormatCoord(radius)} {FormatCoord(radius)} 0 0 1 {FormatCoord(right - radius)} {FormatCoord(bottom)} " +
            $"L {FormatCoord(radius)} {FormatCoord(bottom)} " +
            $"A {FormatCoord(radius)} {FormatCoord(radius)} 0 0 1 0 {FormatCoord(bottom - radius)} " +
            $"L 0 {FormatCoord(bodyTop + radius)} " +
            $"A {FormatCoord(radius)} {FormatCoord(radius)} 0 0 1 {FormatCoord(radius)} {FormatCoord(bodyTop)} Z");
    }

    private static string FormatCoord(double value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

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
