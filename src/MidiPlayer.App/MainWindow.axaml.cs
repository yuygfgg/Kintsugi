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
using MidiPlayer.App.Models;
using MidiPlayer.App.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace MidiPlayer.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private enum VisualizerView
    {
        Eq,
        PianoRoll,
        Mix
    }

    private static readonly IBrush LoopEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#214B75"));
    private static readonly IBrush LoopEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush LoopEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush LoopPreparedBackgroundBrush = new SolidColorBrush(Color.Parse("#16293B"));
    private static readonly IBrush LoopPreparedBorderBrush = new SolidColorBrush(Color.Parse("#3B6288"));
    private static readonly IBrush LoopPreparedForegroundBrush = new SolidColorBrush(Color.Parse("#B8D3EC"));
    private static readonly IBrush LoopDisabledBackgroundBrush = Brushes.Transparent;
    private static readonly IBrush LoopDisabledBorderBrush = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush LoopDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#A0A0A0"));
    private static readonly IBrush LoopBadgeEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#13283E"));
    private static readonly IBrush LoopBadgeEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush LoopBadgeEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush LoopBadgeStandbyBackgroundBrush = new SolidColorBrush(Color.Parse("#161F29"));
    private static readonly IBrush LoopBadgeStandbyBorderBrush = new SolidColorBrush(Color.Parse("#3B6288"));
    private static readonly IBrush LoopBadgeStandbyForegroundBrush = new SolidColorBrush(Color.Parse("#B8D3EC"));
    private static readonly IBrush MixerEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#214B75"));
    private static readonly IBrush MixerEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush MixerEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush MixerDisabledBackgroundBrush = Brushes.Transparent;
    private static readonly IBrush MixerDisabledBorderBrush = new SolidColorBrush(Color.Parse("#333333"));
    private static readonly IBrush MixerDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#A0A0A0"));
    private static readonly IBrush SpeedEnabledBackgroundBrush = new SolidColorBrush(Color.Parse("#214B75"));
    private static readonly IBrush SpeedEnabledBorderBrush = new SolidColorBrush(Color.Parse("#4A90E2"));
    private static readonly IBrush SpeedEnabledForegroundBrush = new SolidColorBrush(Color.Parse("#DCEEFF"));
    private static readonly IBrush SpeedDisabledBackgroundBrush = new SolidColorBrush(Color.Parse("#171717"));
    private static readonly IBrush SpeedDisabledBorderBrush = new SolidColorBrush(Color.Parse("#2F2F2F"));
    private static readonly IBrush SpeedDisabledForegroundBrush = new SolidColorBrush(Color.Parse("#A0A0A0"));
    private static readonly string[] SupportedPlayableExtensions = [.. PlaylistFileParser.SupportedMidiExtensions, .. PlaylistFileParser.SupportedPlaylistExtensions];
    private static readonly string SupportedPlayableFileDescription = string.Join(", ", SupportedPlayableExtensions);
    private const double ChannelMixerPopupWidth = 244;
    private const double ChannelMixerPopupHeight = 206;
    private const double ChannelMixerPopupPointerWidth = 20;
    private const double ChannelMixerPopupPointerInset = 12;
    private const double ChannelMixerPopupPointerHeight = 10;
    private const double ChannelMixerPopupCornerRadius = 8;

    private readonly BassMidiPlayer _player = new();
    private readonly AppSettings _settings;
    private readonly SystemMediaControls _mediaControls;
    private readonly DispatcherTimer _positionTimer;
    private MidiEventsWindow? _midiEventsWindow;
    private bool _isScrubbing;
    private bool _isUpdatingPosition;
    private bool _isExporting;
    private bool _isLoadingAudioPlugin;
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
    private bool _isSpeedPopupOpen;
    private bool _isEqEnabled = true;
    private int _selectedChannelIndex = -1;
    private VisualizerView _selectedVisualizerView = VisualizerView.Eq;
    private PianoRollNote[] _pianoRollNotes = Array.Empty<PianoRollNote>();
    private bool _isLoadingSavedMidiMix;
    private string _currentMidiMixKey = string.Empty;
    private bool _playlistUsesExplicitOrder;
    private int _currentPlaylistSourceIndex = -1;

    public ObservableCollection<PlaylistItem> Playlist { get; } = new();
    private bool _isPlaylistSortAscending = true;
    public bool IsPlaylistSortAscending
    {
        get => _isPlaylistSortAscending;
        set
        {
            if (SetField(ref _isPlaylistSortAscending, value))
            {
                SortPlaylist();
            }
        }
    }
    
    private bool _isPlaylistVisible;
    public bool IsPlaylistVisible
    {
        get => _isPlaylistVisible;
        set
        {
            if (SetField(ref _isPlaylistVisible, value))
            {
                OnPropertyChanged(nameof(PlaylistToggleMargin));
                OnPropertyChanged(nameof(PlaylistToggleIcon));
            }
        }
    }

    public Thickness PlaylistToggleMargin => IsPlaylistVisible ? new Thickness(0, 0, 300, 0) : new Thickness(0, 0, 0, 0);
    public string PlaylistToggleIcon => IsPlaylistVisible ? "▶" : "◀";
    
    private CancellationTokenSource? _playlistParseCts;

    public MainWindow()
    {
        InitializeComponent();
        ChannelMixRows = CreateChannelMixRows();
        DataContext = this;
        ChannelMonitorView.SizeChanged += (_, _) => UpdateChannelMixerPopupPosition();

        _settings = AppSettings.Load();
        _player.EqStateChanged += OnPlayerEqStateChanged;
        _player.PluginStateChanged += OnPlayerPluginStateChanged;

        _player.SampleRate = _settings.SampleRate;

        var initialSoundFontPath = BundledSoundFont.ResolvePreferredPath(_settings.SoundFontPath);
        if (!string.IsNullOrWhiteSpace(initialSoundFontPath))
        {
            try
            {
                _player.LoadSoundFont(initialSoundFontPath);
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
            onSeek: (pos) => { if (CanSeek) { _player.Seek(pos); _mediaControls?.UpdatePosition(pos); RefreshTransport(); } },
            onNext: () => Dispatcher.UIThread.Post(() => OnPlayNextClicked(this, new RoutedEventArgs())),
            onPrevious: () => Dispatcher.UIThread.Post(() => OnPlayPreviousClicked(this, new RoutedEventArgs()))
        );
        _mediaControls.AttachWindow(this);

        _positionTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => RefreshTransport());
        _positionTimer.Start();

        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Bubble, true);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
        
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        UpdateSortIcon();

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


    public bool IsSpeedPopupOpen
    {
        get => _isSpeedPopupOpen;
        private set
        {
            if (SetField(ref _isSpeedPopupOpen, value))
            {
                OnPropertyChanged(nameof(SpeedButtonBackground));
                OnPropertyChanged(nameof(SpeedButtonBorderBrush));
                OnPropertyChanged(nameof(SpeedButtonForeground));
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

    public bool CanBrowseMidiEvents => _player.HasStream && !_isExporting && !string.IsNullOrWhiteSpace(_player.MidiPath);

    public bool CanLoadAudioPlugin => !_isExporting && !_isLoadingAudioPlugin;

    public bool CanUnloadAudioPlugin => _player.HasEffectPlugin && !_isExporting && !_isLoadingAudioPlugin;

    public bool CanOpenAudioPluginEditor => _player.EffectPluginHasEditor && !_isExporting && !_isLoadingAudioPlugin;

    public string ExportButtonText => _isExporting ? "EXPORTING..." : "EXPORT AUDIO";

    public string AudioPluginButtonText
        => _isLoadingAudioPlugin
            ? "LOADING FX..."
            : _player.HasEffectPlugin
                ? "REPLACE FX"
                : "LOAD FX";

    public string AudioPluginSummaryText
        => _player.HasEffectPlugin
            ? $"FX: {_player.EffectPluginDisplayName}"
            : "FX: OFF";

    public string AudioPluginToolTip
        => _player.HasEffectPlugin
            ? "Replace the currently loaded VST3 or Audio Unit effect."
            : "Load a VST3 or Audio Unit effect.";

    public string CurrentBpmText => FormatBpm(_player.GetCurrentBpm());

    public bool CanAdjustPlaybackModifiers => _player.HasStream;

    public bool IsEqEnabled
    {
        get => _isEqEnabled;
        set
        {
            if (SetField(ref _isEqEnabled, value))
            {
                _player.IsEqEnabled = value;
                OnPropertyChanged(nameof(EqButtonBackground));
                OnPropertyChanged(nameof(EqButtonBorderBrush));
                OnPropertyChanged(nameof(EqButtonForeground));
                OnPropertyChanged(nameof(EqButtonToolTip));
            }
        }
    }

    public IBrush EqButtonBackground => IsEqEnabled ? SpeedEnabledBackgroundBrush : SpeedDisabledBackgroundBrush;

    public IBrush EqButtonBorderBrush => IsEqEnabled ? SpeedEnabledBorderBrush : SpeedDisabledBorderBrush;

    public IBrush EqButtonForeground => IsEqEnabled ? SpeedEnabledForegroundBrush : SpeedDisabledForegroundBrush;

    public string EqButtonToolTip => IsEqEnabled ? "Turn EQ off" : "Turn EQ on";

    public PianoRollNote[] PianoRollNotes
    {
        get => _pianoRollNotes;
        private set => SetField(ref _pianoRollNotes, value);
    }

    public bool IsEqViewSelected => _selectedVisualizerView == VisualizerView.Eq;

    public bool IsPianoRollViewSelected => _selectedVisualizerView == VisualizerView.PianoRoll;

    public bool IsMixViewSelected => _selectedVisualizerView == VisualizerView.Mix;

    public IBrush EqViewButtonBackground => GetVisualizerViewButtonBackground(VisualizerView.Eq);

    public IBrush EqViewButtonBorderBrush => GetVisualizerViewButtonBorderBrush(VisualizerView.Eq);

    public IBrush EqViewButtonForeground => GetVisualizerViewButtonForeground(VisualizerView.Eq);

    public IBrush PianoRollViewButtonBackground => GetVisualizerViewButtonBackground(VisualizerView.PianoRoll);

    public IBrush PianoRollViewButtonBorderBrush => GetVisualizerViewButtonBorderBrush(VisualizerView.PianoRoll);

    public IBrush PianoRollViewButtonForeground => GetVisualizerViewButtonForeground(VisualizerView.PianoRoll);

    public IBrush MixViewButtonBackground => GetVisualizerViewButtonBackground(VisualizerView.Mix);

    public IBrush MixViewButtonBorderBrush => GetVisualizerViewButtonBorderBrush(VisualizerView.Mix);

    public IBrush MixViewButtonForeground => GetVisualizerViewButtonForeground(VisualizerView.Mix);

    public double PlaybackSpeedPercent
    {
        get => _player.PlaybackSpeedPercent;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.PlaybackSpeedPercent == intValue)
            {
                return;
            }

            _player.PlaybackSpeedPercent = intValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackSpeedPercentText));
            RefreshTransport();
            PersistCurrentMidiMix();
            UpdateNowPlayingTimeline();
        }
    }

    public string PlaybackSpeedPercentText => FormatPercent(_player.PlaybackSpeedPercent);

    public double TransposeSemitones
    {
        get => _player.TransposeSemitones;
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.TransposeSemitones == intValue)
            {
                return;
            }

            _player.TransposeSemitones = intValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TransposeSemitonesText));
            PersistCurrentMidiMix();
        }
    }

    public string TransposeSemitonesText => FormatSemitoneShift(_player.TransposeSemitones);

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
                PersistCurrentMidiMix();
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
                PersistCurrentMidiMix();
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
                PersistCurrentMidiMix();
            }
        }
    }

    public string ChorusReturnPercentText => FormatPercent(_player.ChorusReturnPercent);

    public double GlobalReverbReturnValue
    {
        get => GetGlobalReverbReturnValue();
        set
        {
            var intValue = (int)Math.Round(value);
            if (GetGlobalReverbReturnValue() == intValue)
            {
                return;
            }

            switch (_player.ReverbReturnMode)
            {
                case ChannelSendMode.Scale:
                    _player.ReverbReturnScalePercent = intValue;
                    break;
                case ChannelSendMode.Bias:
                    _player.ReverbReturnBiasValue = intValue;
                    break;
                default:
                    _player.ReverbReturnPercent = intValue;
                    break;
            }

            RefreshGlobalReverbBindings();
            PersistCurrentMidiMix();
        }
    }

    public double GlobalReverbReturnMinimum => _player.ReverbReturnMode == ChannelSendMode.Bias
        ? -BassMidiPlayer.MaxMixPercent
        : 0;

    public double GlobalReverbReturnMaximum => BassMidiPlayer.MaxMixPercent;

    public string GlobalReverbReturnText => _player.ReverbReturnMode == ChannelSendMode.Bias
        ? FormatSignedValue(GetGlobalReverbReturnValue())
        : FormatPercent(GetGlobalReverbReturnValue());

    public string GlobalReverbReturnModeLabel => _player.ReverbReturnMode switch
    {
        ChannelSendMode.Scale => "SCL",
        ChannelSendMode.Bias => "BIA",
        _ => "ABS"
    };

    public string GlobalReverbReturnModeToolTip => _player.ReverbReturnMode switch
    {
        ChannelSendMode.Scale => "Scale the MIDI file's current reverb return",
        ChannelSendMode.Bias => "Add or subtract from the MIDI file's current reverb return",
        _ => "Absolute reverb return level"
    };

    public double GlobalChorusReturnValue
    {
        get => GetGlobalChorusReturnValue();
        set
        {
            var intValue = (int)Math.Round(value);
            if (GetGlobalChorusReturnValue() == intValue)
            {
                return;
            }

            switch (_player.ChorusReturnMode)
            {
                case ChannelSendMode.Scale:
                    _player.ChorusReturnScalePercent = intValue;
                    break;
                case ChannelSendMode.Bias:
                    _player.ChorusReturnBiasValue = intValue;
                    break;
                default:
                    _player.ChorusReturnPercent = intValue;
                    break;
            }

            RefreshGlobalChorusBindings();
            PersistCurrentMidiMix();
        }
    }

    public double GlobalChorusReturnMinimum => _player.ChorusReturnMode == ChannelSendMode.Bias
        ? -BassMidiPlayer.MaxMixPercent
        : 0;

    public double GlobalChorusReturnMaximum => BassMidiPlayer.MaxMixPercent;

    public string GlobalChorusReturnText => _player.ChorusReturnMode == ChannelSendMode.Bias
        ? FormatSignedValue(GetGlobalChorusReturnValue())
        : FormatPercent(GetGlobalChorusReturnValue());

    public string GlobalChorusReturnModeLabel => _player.ChorusReturnMode switch
    {
        ChannelSendMode.Scale => "SCL",
        ChannelSendMode.Bias => "BIA",
        _ => "ABS"
    };

    public string GlobalChorusReturnModeToolTip => _player.ChorusReturnMode switch
    {
        ChannelSendMode.Scale => "Scale the MIDI file's current chorus return",
        ChannelSendMode.Bias => "Add or subtract from the MIDI file's current chorus return",
        _ => "Absolute chorus return level"
    };

    public bool IsLoopEnabled => _player.IsLooping;

    public bool HasCustomLoopRange => _player.HasCustomLoopRange;

    public double LoopRangeStartSeconds => _player.GetLoopStartSeconds();

    public double LoopRangeEndSeconds => _player.GetLoopEndSeconds();

    public bool IsLoopBadgeVisible => CanSeek && (_player.IsLooping || _player.HasCustomLoopRange);

    public string LoopRangeSummaryText => _player.HasCustomLoopRange
        ? $"{(_player.IsLooping ? "LOOP A-B" : "A-B READY")} {FormatTime(LoopRangeStartSeconds)} - {FormatTime(LoopRangeEndSeconds)}"
        : "LOOP FULL TRACK";

    public string LoopButtonToolTip => _player.HasCustomLoopRange
        ? (_player.IsLooping
            ? "Loop the selected A-B range. Drag the upper timeline lane to adjust it."
            : "A-B range selected. Click to loop it.")
        : (_player.IsLooping
            ? "Looping the full track. Drag the upper timeline lane to create an A-B range instead."
            : "Loop playback. Drag the upper timeline lane to create an A-B range.");

    public IBrush LoopButtonBackground => _player.IsLooping
        ? LoopEnabledBackgroundBrush
        : _player.HasCustomLoopRange
            ? LoopPreparedBackgroundBrush
            : LoopDisabledBackgroundBrush;

    public IBrush LoopButtonBorderBrush => _player.IsLooping
        ? LoopEnabledBorderBrush
        : _player.HasCustomLoopRange
            ? LoopPreparedBorderBrush
            : LoopDisabledBorderBrush;

    public IBrush LoopButtonForeground => _player.IsLooping
        ? LoopEnabledForegroundBrush
        : _player.HasCustomLoopRange
            ? LoopPreparedForegroundBrush
            : LoopDisabledForegroundBrush;

    public IBrush LoopBadgeBackground => _player.IsLooping ? LoopBadgeEnabledBackgroundBrush : LoopBadgeStandbyBackgroundBrush;

    public IBrush LoopBadgeBorderBrush => _player.IsLooping ? LoopBadgeEnabledBorderBrush : LoopBadgeStandbyBorderBrush;

    public IBrush LoopBadgeForeground => _player.IsLooping ? LoopBadgeEnabledForegroundBrush : LoopBadgeStandbyForegroundBrush;




    public IBrush SpeedButtonBackground => IsSpeedPopupOpen ? SpeedEnabledBackgroundBrush : SpeedDisabledBackgroundBrush;

    public IBrush SpeedButtonBorderBrush => IsSpeedPopupOpen ? SpeedEnabledBorderBrush : SpeedDisabledBorderBrush;

    public IBrush SpeedButtonForeground => IsSpeedPopupOpen ? SpeedEnabledForegroundBrush : SpeedDisabledForegroundBrush;

    private async void OnOpenMidiClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanOpenMidi)
        {
            return;
        }

        var path = await PickSingleFileAsync(
            new FilePickerFileType("MIDI / Playlist")
            {
                Patterns = [.. SupportedPlayableExtensions.Select(extension => $"*{extension}")]
            });

        if (path is null)
        {
            return;
        }

        OpenPlayablePath(path);
    }

    private async void OnLoadAudioPluginClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanLoadAudioPlugin)
        {
            return;
        }

        var path = await PickAudioPluginPathAsync();

        if (path is null)
        {
            return;
        }

        _isLoadingAudioPlugin = true;
        RefreshAudioPluginBindings();
        StatusText = $"Loading FX: {Path.GetFileName(path)}";

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            _player.LoadEffectPlugin(path);
            StatusText = $"FX loaded: {_player.EffectPluginDisplayName}";
        }
        catch (Exception ex)
        {
            StatusText = "Plug-in error: " + ex.Message;
        }
        finally
        {
            _isLoadingAudioPlugin = false;
            RefreshAudioPluginBindings();
        }
    }

    private void OnUnloadAudioPluginClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanUnloadAudioPlugin)
        {
            return;
        }

        try
        {
            _player.UnloadEffectPlugin();
            StatusText = "FX cleared.";
        }
        catch (Exception ex)
        {
            StatusText = "Plug-in error: " + ex.Message;
        }

        RefreshAudioPluginBindings();
    }

    private async void OnShowAudioPluginEditorClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanOpenAudioPluginEditor)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
            _player.ShowEffectPluginEditor();
            StatusText = $"FX UI opened: {_player.EffectPluginDisplayName}";
        }
        catch (Exception ex)
        {
            StatusText = "Plug-in error: " + ex.Message;
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

    private async void OnBrowseMidiEventsClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanBrowseMidiEvents || string.IsNullOrWhiteSpace(_player.MidiPath))
        {
            return;
        }

        if (_midiEventsWindow is null)
        {
            _midiEventsWindow = new MidiEventsWindow(_player);
            _midiEventsWindow.Closed += OnMidiEventsWindowClosed;
            _midiEventsWindow.Show();
        }
        else if (!_midiEventsWindow.IsVisible)
        {
            _midiEventsWindow.Show();
        }

        await _midiEventsWindow.LoadMidiAsync(_player.MidiPath);
        _midiEventsWindow.Activate();
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

        var dialog = new ExportWavWindow(
            _player.MidiPath,
            _player.SoundFontPath,
            _player.SampleRate,
            _player.PlaybackSpeedPercent,
            _player.TransposeSemitones);
        await dialog.ShowDialog(this);

        if (!dialog.WasConfirmed || dialog.ExportOptions is null)
        {
            return;
        }

        SetExportingState(true);
        StatusText = $"Exporting {GetExportFormatLabel(dialog.ExportOptions.Format)} at {dialog.ExportOptions.SampleRate / 1000d:0.###} kHz...";

        try
        {
            await Task.Run(() => _player.ExportCurrentMidiToAudio(dialog.ExportOptions));
            StatusText = $"{GetExportFormatLabel(dialog.ExportOptions.Format)} exported: {Path.GetFileName(dialog.ExportOptions.OutputPath)}";
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
        IsSpeedPopupOpen = false;
        _player.IsLooping = !_player.IsLooping;

        if (_player.IsLooping && _player.HasCustomLoopRange)
        {
            double loopStart = _player.GetLoopStartSeconds();
            double loopEnd = _player.GetLoopEndSeconds();
            if (PositionSeconds < loopStart || PositionSeconds >= loopEnd)
            {
                _player.Seek(loopStart);
                RefreshTransport();
            }
        }

        StatusText = _player.IsLooping
            ? (_player.HasCustomLoopRange
                ? $"Looping {FormatTime(_player.GetLoopStartSeconds())} - {FormatTime(_player.GetLoopEndSeconds())}"
                : "Looping full track")
            : (_player.HasCustomLoopRange ? "A-B range kept" : "Loop off");
        RefreshLoopBindings();
    }

    private void OnClearLoopRangeClicked(object? sender, RoutedEventArgs e)
    {
        ClearLoopRange();
        e.Handled = true;
    }


    private void OnSpeedButtonClicked(object? sender, RoutedEventArgs e)
    {
        CloseChannelMixerPopup();
        IsSpeedPopupOpen = !IsSpeedPopupOpen;
    }

    private void OnEqToggleClicked(object? sender, RoutedEventArgs e)
    {
        IsEqEnabled = !IsEqEnabled;
        e.Handled = true;
    }

    private void OnShowEqViewClicked(object? sender, RoutedEventArgs e)
    {
        SetVisualizerView(VisualizerView.Eq);
        e.Handled = true;
    }

    private void OnShowPianoRollViewClicked(object? sender, RoutedEventArgs e)
    {
        SetVisualizerView(VisualizerView.PianoRoll);
        e.Handled = true;
    }

    private void OnShowMixViewClicked(object? sender, RoutedEventArgs e)
    {
        SetVisualizerView(VisualizerView.Mix);
        e.Handled = true;
    }

    private void OnTogglePlaylistClicked(object? sender, RoutedEventArgs e)
    {
        IsPlaylistVisible = !IsPlaylistVisible;
    }

    private void OnToggleSortClicked(object? sender, RoutedEventArgs e)
    {
        if (_playlistUsesExplicitOrder)
        {
            _playlistUsesExplicitOrder = false;
            SortPlaylist();
        }
        else
        {
            IsPlaylistSortAscending = !IsPlaylistSortAscending;
        }

        UpdateSortIcon();
    }

    private void UpdateSortIcon()
    {
        var run = this.FindControl<Avalonia.Controls.TextBlock>("SortIconText");
        if (run != null)
        {
            run.Text = _playlistUsesExplicitOrder
                ? "LIST"
                : IsPlaylistSortAscending ? "↓ A-Z" : "↑ Z-A";
        }
    }

    private void OnPlaylistSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Avalonia.Controls.ListBox listBox && listBox.SelectedItem is PlaylistItem item)
        {
            listBox.SelectedItem = null; // Reset selection to allow clicking again
            if (!item.IsPlaying)
            {
                LoadMidiFromPath(item.FilePath, true, item.SourceIndex);
            }
        }
    }

    internal void OnPlayNextClicked(object? sender, RoutedEventArgs e)
    {
        if (Playlist.Count == 0)
        {
            return;
        }

        int index = GetCurrentPlaylistIndex();
        if (index >= 0 && index < Playlist.Count - 1)
        {
            PlaylistItem nextItem = Playlist[index + 1];
            LoadMidiFromPath(nextItem.FilePath, true, nextItem.SourceIndex);
        }
    }

    internal void OnPlayPreviousClicked(object? sender, RoutedEventArgs e)
    {
        if (Playlist.Count == 0)
        {
            return;
        }

        int index = GetCurrentPlaylistIndex();
        if (index > 0)
        {
            PlaylistItem previousItem = Playlist[index - 1];
            LoadMidiFromPath(previousItem.FilePath, true, previousItem.SourceIndex);
        }
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
        IsSpeedPopupOpen = false;
        UpdateChannelMixerPopupPosition();
    }

    private void OnMasterVolumeSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        MasterVolumePercent = BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnPlaybackSpeedSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        PlaybackSpeedPercent = BassMidiPlayer.DefaultPlaybackSpeedPercent;
        e.Handled = true;
    }

    private void OnTransposeDownClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanAdjustPlaybackModifiers)
        {
            return;
        }

        TransposeSemitones -= 1;
        e.Handled = true;
    }

    private void OnTransposeUpClicked(object? sender, RoutedEventArgs e)
    {
        if (!CanAdjustPlaybackModifiers)
        {
            return;
        }

        TransposeSemitones += 1;
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
        GlobalReverbReturnValue = _player.ReverbReturnMode == ChannelSendMode.Bias
            ? MidiMixSettings.DefaultBiasValue
            : BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnChorusSliderDoubleTapped(object? sender, TappedEventArgs e)
    {
        GlobalChorusReturnValue = _player.ChorusReturnMode == ChannelSendMode.Bias
            ? MidiMixSettings.DefaultBiasValue
            : BassMidiPlayer.DefaultMixPercent;
        e.Handled = true;
    }

    private void OnGlobalReverbModeClicked(object? sender, RoutedEventArgs e)
    {
        _player.ReverbReturnMode = GetNextMode(_player.ReverbReturnMode);
        RefreshGlobalReverbBindings();
        PersistCurrentMidiMix();
        e.Handled = true;
    }

    private void OnGlobalChorusModeClicked(object? sender, RoutedEventArgs e)
    {
        _player.ChorusReturnMode = GetNextMode(_player.ChorusReturnMode);
        RefreshGlobalChorusBindings();
        PersistCurrentMidiMix();
        e.Handled = true;
    }

    private void OnChannelReverbModeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChannelMixStrip strip })
        {
            strip.ToggleReverbSendMode();
        }

        e.Handled = true;
    }

    private void OnChannelChorusModeClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChannelMixStrip strip })
        {
            strip.ToggleChorusSendMode();
        }

        e.Handled = true;
    }

    private void OnTimelineSeekStarted(object? sender, EventArgs e)
    {
        CloseChannelMixerPopup();
        IsSpeedPopupOpen = false;
        if (!CanSeek)
        {
            return;
        }

        _isScrubbing = true;
    }

    private void OnTimelineSeekChanged(object? sender, TimelineSeekChangedEventArgs e)
    {
        if (!CanSeek)
        {
            return;
        }

        PositionSeconds = e.Value;
    }

    private void OnTimelineSeekCompleted(object? sender, TimelineSeekChangedEventArgs e)
    {
        if (!_isScrubbing || !CanSeek)
        {
            return;
        }

        _isScrubbing = false;

        try
        {
            _player.Seek(e.Value);
            RefreshTransport();
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void OnTimelineLoopRangeChanged(object? sender, TimelineLoopRangeChangedEventArgs e)
    {
        CloseChannelMixerPopup();
        IsSpeedPopupOpen = false;
        if (!CanSeek)
        {
            return;
        }

        try
        {
            _player.SetLoopRange(e.StartSeconds, e.EndSeconds);
            if (e.IsFinal)
            {
                _player.IsLooping = true;
                double loopStart = _player.GetLoopStartSeconds();
                double loopEnd = _player.GetLoopEndSeconds();
                if (PositionSeconds < loopStart || PositionSeconds >= loopEnd)
                {
                    _player.Seek(loopStart);
                    RefreshTransport();
                }

                StatusText = $"Looping {FormatTime(loopStart)} - {FormatTime(loopEnd)}";
            }

            RefreshLoopBindings();
        }
        catch (Exception ex)
        {
            StatusText = "Error: " + ex.Message;
        }
    }

    private void OnTimelineLoopRangeCleared(object? sender, EventArgs e)
        => ClearLoopRange();

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var sourceVisual = e.Source as Visual;
        bool clickedChannelMixer = IsWithinVisual(sourceVisual, ChannelMonitorView) || IsWithinVisual(sourceVisual, ChannelMixerPopupHost);
        bool clickedSpeedPopup = IsWithinVisual(sourceVisual, SpeedButtonHost);

        if (!clickedChannelMixer)
        {
            CloseChannelMixerPopup();
        }

        if (!clickedSpeedPopup)
        {
            IsSpeedPopupOpen = false;
        }
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        bool canDrop = CanOpenMidi && TryGetDraggedPlayablePath(e.DataTransfer) is not null;
        e.DragEffects = canDrop ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (!CanOpenMidi)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        string? path = TryGetDraggedPlayablePath(e.DataTransfer);
        if (path is null)
        {
            StatusText = $"Drop a MIDI or playlist file ({SupportedPlayableFileDescription}).";
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        OpenPlayablePath(path);
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
            if (Playlist.Count > 1)
            {
                OnPlayNextClicked(this, new RoutedEventArgs());
            }
            else
            {
                StatusText = "Finished";
                _mediaControls.UpdatePlaybackState(false, PositionSeconds);
            }
        }

        _wasPlayingLastRefresh = isPlaying;

        OnPropertyChanged(nameof(CanTogglePlayback));
        OnPropertyChanged(nameof(CanSeek));
        OnPropertyChanged(nameof(CanOpenMidi));
        OnPropertyChanged(nameof(CanOpenSettings));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanBrowseMidiEvents));
        OnPropertyChanged(nameof(ExportButtonText));
        OnPropertyChanged(nameof(CanAdjustPlaybackModifiers));
        OnPropertyChanged(nameof(PlayPauseIcon));
        OnPropertyChanged(nameof(PlayPauseIconMargin));
        OnPropertyChanged(nameof(CurrentBpmText));
        RefreshLoopBindings();
        RefreshAudioPluginBindings();
    }

    private async Task<string?> PickSingleFileAsync(FilePickerFileType fileType)
    {
        CloseChannelMixerPopup();
        IsSpeedPopupOpen = false;
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

    private async Task<string?> PickAudioPluginPathAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return await PickSingleFileAsync(new FilePickerFileType("Audio Plug-ins")
            {
                Patterns = ["*.vst3"]
            });
        }

        CloseChannelMixerPopup();
        IsSpeedPopupOpen = false;
        var dialog = new AudioPluginPickerWindow();
        await dialog.ShowDialog(this);
        return dialog.SelectedPath;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExporting)
        {
            e.Cancel = true;
            StatusText = "Wait for the audio export to finish.";
            return;
        }

        _positionTimer.Stop();
        if (_midiEventsWindow is not null)
        {
            _midiEventsWindow.Closed -= OnMidiEventsWindowClosed;
            _midiEventsWindow.Close();
            _midiEventsWindow = null;
        }

        _mediaControls.Dispose();
        _player.EqStateChanged -= OnPlayerEqStateChanged;
        _player.PluginStateChanged -= OnPlayerPluginStateChanged;
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

    private static string FormatSignedValue(double value)
    {
        var rounded = (int)Math.Round(value);
        return rounded > 0 ? $"+{rounded}" : rounded.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSemitoneShift(double value)
        => $"{FormatSignedValue(value)} st";

    private static ChannelSendMode GetNextMode(ChannelSendMode mode)
        => mode switch
        {
            ChannelSendMode.Scale => ChannelSendMode.Absolute,
            ChannelSendMode.Absolute => ChannelSendMode.Bias,
            _ => ChannelSendMode.Scale
        };

    private int GetGlobalReverbReturnValue()
        => _player.ReverbReturnMode switch
        {
            ChannelSendMode.Scale => _player.ReverbReturnScalePercent,
            ChannelSendMode.Bias => _player.ReverbReturnBiasValue,
            _ => _player.ReverbReturnPercent
        };

    private int GetGlobalChorusReturnValue()
        => _player.ChorusReturnMode switch
        {
            ChannelSendMode.Scale => _player.ChorusReturnScalePercent,
            ChannelSendMode.Bias => _player.ChorusReturnBiasValue,
            _ => _player.ChorusReturnPercent
        };

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
            rows[i] = new ChannelMixStrip(_player, i, PersistCurrentMidiMix);
        }

        return rows;
    }

    private void LoadMixSettingsForMidi(string midiPath)
    {
        _isLoadingSavedMidiMix = true;

        try
        {
            _player.ApplyMixSettings(_settings.GetMidiMixSettings(midiPath, out _currentMidiMixKey));
            RefreshEqBindings();
            RefreshMixBindings();
            RefreshChannelMixRows();
        }
        finally
        {
            _isLoadingSavedMidiMix = false;
        }
    }

    private void PersistCurrentMidiMix()
    {
        if (string.IsNullOrWhiteSpace(_player.MidiPath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentMidiMixKey))
        {
            _currentMidiMixKey = _settings.GetMidiMixSettingsKey(_player.MidiPath);
        }

        if (string.IsNullOrWhiteSpace(_currentMidiMixKey))
        {
            return;
        }

        _settings.SetMidiMixSettings(_currentMidiMixKey, _player.CaptureMixSettings());
        _settings.Save();
    }

    private void RefreshMixBindings()
    {
        RefreshPlaybackBindings();
        OnPropertyChanged(nameof(MasterVolumePercent));
        OnPropertyChanged(nameof(MasterVolumePercentText));
        OnPropertyChanged(nameof(ReverbReturnPercent));
        OnPropertyChanged(nameof(ReverbReturnPercentText));
        OnPropertyChanged(nameof(ChorusReturnPercent));
        OnPropertyChanged(nameof(ChorusReturnPercentText));
        RefreshGlobalReverbBindings();
        RefreshGlobalChorusBindings();
    }

    private void RefreshAudioPluginBindings()
    {
        OnPropertyChanged(nameof(CanLoadAudioPlugin));
        OnPropertyChanged(nameof(CanUnloadAudioPlugin));
        OnPropertyChanged(nameof(CanOpenAudioPluginEditor));
        OnPropertyChanged(nameof(AudioPluginButtonText));
        OnPropertyChanged(nameof(AudioPluginSummaryText));
        OnPropertyChanged(nameof(AudioPluginToolTip));
    }

    private void RefreshPlaybackBindings()
    {
        OnPropertyChanged(nameof(PlaybackSpeedPercent));
        OnPropertyChanged(nameof(PlaybackSpeedPercentText));
        OnPropertyChanged(nameof(TransposeSemitones));
        OnPropertyChanged(nameof(TransposeSemitonesText));
        RefreshLoopBindings();
    }

    private void RefreshLoopBindings()
    {
        OnPropertyChanged(nameof(IsLoopEnabled));
        OnPropertyChanged(nameof(HasCustomLoopRange));
        OnPropertyChanged(nameof(LoopRangeStartSeconds));
        OnPropertyChanged(nameof(LoopRangeEndSeconds));
        OnPropertyChanged(nameof(IsLoopBadgeVisible));
        OnPropertyChanged(nameof(LoopRangeSummaryText));
        OnPropertyChanged(nameof(LoopButtonToolTip));
        OnPropertyChanged(nameof(LoopButtonBackground));
        OnPropertyChanged(nameof(LoopButtonBorderBrush));
        OnPropertyChanged(nameof(LoopButtonForeground));
        OnPropertyChanged(nameof(LoopBadgeBackground));
        OnPropertyChanged(nameof(LoopBadgeBorderBrush));
        OnPropertyChanged(nameof(LoopBadgeForeground));
    }

    private void ClearLoopRange()
    {
        _player.ClearLoopRange();
        StatusText = _player.IsLooping ? "Looping full track" : "Loop range cleared";
        RefreshLoopBindings();
    }

    private void RefreshChannelMixRows()
    {
        foreach (var row in ChannelMixRows)
        {
            row.Refresh();
        }
    }

    private void RefreshGlobalReverbBindings()
    {
        OnPropertyChanged(nameof(GlobalReverbReturnValue));
        OnPropertyChanged(nameof(GlobalReverbReturnMinimum));
        OnPropertyChanged(nameof(GlobalReverbReturnMaximum));
        OnPropertyChanged(nameof(GlobalReverbReturnText));
        OnPropertyChanged(nameof(GlobalReverbReturnModeLabel));
        OnPropertyChanged(nameof(GlobalReverbReturnModeToolTip));
    }

    private void RefreshGlobalChorusBindings()
    {
        OnPropertyChanged(nameof(GlobalChorusReturnValue));
        OnPropertyChanged(nameof(GlobalChorusReturnMinimum));
        OnPropertyChanged(nameof(GlobalChorusReturnMaximum));
        OnPropertyChanged(nameof(GlobalChorusReturnText));
        OnPropertyChanged(nameof(GlobalChorusReturnModeLabel));
        OnPropertyChanged(nameof(GlobalChorusReturnModeToolTip));
    }

    private void RefreshEqBindings()
    {
        if (_isEqEnabled != _player.IsEqEnabled)
        {
            _isEqEnabled = _player.IsEqEnabled;
            OnPropertyChanged(nameof(IsEqEnabled));
        }

        OnPropertyChanged(nameof(EqButtonBackground));
        OnPropertyChanged(nameof(EqButtonBorderBrush));
        OnPropertyChanged(nameof(EqButtonForeground));
        OnPropertyChanged(nameof(EqButtonToolTip));
    }

    private void OnPlayerEqStateChanged(object? sender, EventArgs e)
    {
        RefreshEqBindings();

        if (!_isLoadingSavedMidiMix)
        {
            PersistCurrentMidiMix();
        }
    }

    private void OnPlayerPluginStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshAudioPluginBindings);
    }

    private void SetVisualizerView(VisualizerView view)
    {
        if (_selectedVisualizerView == view)
        {
            return;
        }

        _selectedVisualizerView = view;
        OnPropertyChanged(nameof(IsEqViewSelected));
        OnPropertyChanged(nameof(IsPianoRollViewSelected));
        OnPropertyChanged(nameof(IsMixViewSelected));
        OnPropertyChanged(nameof(EqViewButtonBackground));
        OnPropertyChanged(nameof(EqViewButtonBorderBrush));
        OnPropertyChanged(nameof(EqViewButtonForeground));
        OnPropertyChanged(nameof(PianoRollViewButtonBackground));
        OnPropertyChanged(nameof(PianoRollViewButtonBorderBrush));
        OnPropertyChanged(nameof(PianoRollViewButtonForeground));
        OnPropertyChanged(nameof(MixViewButtonBackground));
        OnPropertyChanged(nameof(MixViewButtonBorderBrush));
        OnPropertyChanged(nameof(MixViewButtonForeground));
    }

    private IBrush GetVisualizerViewButtonBackground(VisualizerView view)
        => _selectedVisualizerView == view ? SpeedEnabledBackgroundBrush : SpeedDisabledBackgroundBrush;

    private IBrush GetVisualizerViewButtonBorderBrush(VisualizerView view)
        => _selectedVisualizerView == view ? SpeedEnabledBorderBrush : SpeedDisabledBorderBrush;

    private IBrush GetVisualizerViewButtonForeground(VisualizerView view)
        => _selectedVisualizerView == view ? SpeedEnabledForegroundBrush : SpeedDisabledForegroundBrush;

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

    internal void LoadMidiFromPath(string path, bool isFromPlaylist = false, int playlistSourceIndex = -1)
    {
        CloseChannelMixerPopup();
        IsSpeedPopupOpen = false;
        _currentMidiMixKey = string.Empty;

        try
        {
            _player.LoadMidi(path);
            try
            {
                PianoRollNotes = MidiFileAnalysisService.LoadPianoRollNotes(path);
            }
            catch
            {
                PianoRollNotes = Array.Empty<PianoRollNote>();
            }

            LoadMixSettingsForMidi(path);
            var title = Path.GetFileNameWithoutExtension(path);
            MidiDisplayName = title;
            StatusText = "Playing";
            _player.Play();
            RefreshTransport(resetPosition: true);
            _mediaControls.UpdateNowPlaying(title, "Kintsugi Midi Player", DurationSeconds, PositionSeconds);
            _mediaControls.UpdatePlaybackState(true, PositionSeconds);
            if (_midiEventsWindow is not null)
            {
                _ = _midiEventsWindow.LoadMidiAsync(path);
            }

            if (!isFromPlaylist)
            {
                ImportPlaylistFromDirectory(path);
            }
            else
            {
                UpdatePlaylistPlayingState(playlistSourceIndex, path);
            }
        }
        catch (Exception ex)
        {
            _currentMidiMixKey = string.Empty;
            PianoRollNotes = Array.Empty<PianoRollNote>();
            StatusText = "Error: " + ex.Message;
        }
    }

    private void ImportPlaylistFromDirectory(string currentMidiPath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(currentMidiPath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return;
            }

            var files = Directory.GetFiles(dir)
                .Where(f => IsSupportedMidiPath(f))
                .ToList();

            ReplacePlaylist(files, currentMidiPath, preserveExplicitOrder: false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error importing playlist: {ex.Message}");
        }
    }

    private void SortPlaylist()
    {
        var items = Playlist.ToList();
        if (_isPlaylistSortAscending)
            items.Sort((a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase));
        else
            items.Sort((a, b) => string.Compare(b.FileName, a.FileName, StringComparison.OrdinalIgnoreCase));
        
        Playlist.Clear();
        foreach (var item in items) Playlist.Add(item);
    }

    private void UpdatePlaylistPlayingState(int playingSourceIndex, string? playingPath = null)
    {
        _currentPlaylistSourceIndex = playingSourceIndex;
        foreach (var item in Playlist)
        {
            item.IsPlaying = playingSourceIndex >= 0
                ? item.SourceIndex == playingSourceIndex
                : !string.IsNullOrWhiteSpace(playingPath) && string.Equals(item.FilePath, playingPath, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task ParsePlaylistDurationsAsync(CancellationToken ct)
    {
        var itemsToParse = Playlist.ToList();
        foreach (var item in itemsToParse)
        {
            if (ct.IsCancellationRequested) return;
            if (item.IsDurationParsed || item.IsFailed) continue;

            await Task.Run(() =>
            {
                try
                {
                    int handle = ManagedBass.Midi.BassMidi.CreateStream(item.FilePath, 0, 0, ManagedBass.BassFlags.Decode | ManagedBass.BassFlags.Prescan, 44100);
                    if (handle != 0)
                    {
                        long lenBytes = ManagedBass.Bass.ChannelGetLength(handle, ManagedBass.PositionFlags.Bytes);
                        double dur = ManagedBass.Bass.ChannelBytes2Seconds(handle, lenBytes);
                        ManagedBass.Bass.StreamFree(handle);
                        
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (ct.IsCancellationRequested) return;
                            item.DurationSeconds = dur;
                            item.IsDurationParsed = true;
                        });
                    }
                    else 
                    {
                        throw new Exception("CreateStream failed");
                    }
                }
                catch
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ct.IsCancellationRequested) return;
                        item.IsFailed = true;
                    });
                }
            }, ct);
        }
    }

    private static string? TryGetDraggedPlayablePath(IDataTransfer dataTransfer)
    {
        if (dataTransfer.TryGetFiles() is { } storageItems)
        {
            foreach (var item in storageItems)
            {
                if (item is not IStorageFile storageFile)
                {
                    continue;
                }

                string? path = storageFile.TryGetLocalPath();
                if (IsSupportedPlayablePath(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static bool IsSupportedMidiPath(string? path)
        => PlaylistFileParser.IsSupportedMidiPath(path);

    private static bool IsSupportedPlayablePath(string? path)
        => PlaylistFileParser.IsSupportedMidiPath(path) || PlaylistFileParser.IsSupportedPlaylistPath(path);

    private void OpenPlayablePath(string path)
    {
        if (PlaylistFileParser.IsSupportedPlaylistPath(path))
        {
            ImportPlaylistFromFile(path);
            return;
        }

        if (PlaylistFileParser.IsSupportedMidiPath(path))
        {
            LoadMidiFromPath(path);
            return;
        }

        StatusText = $"Unsupported file type: {Path.GetExtension(path)}";
    }

    private void ImportPlaylistFromFile(string playlistPath)
    {
        try
        {
            IReadOnlyList<string> midiPaths = PlaylistFileParser.ParseLocalMidiEntries(playlistPath);
            if (midiPaths.Count == 0)
            {
                StatusText = $"No supported local MIDI files found in {Path.GetFileName(playlistPath)}.";
                return;
            }

            _isPlaylistSortAscending = true;
            ReplacePlaylist(midiPaths, currentMidiPath: null, preserveExplicitOrder: true);
            PlaylistItem firstItem = Playlist[0];
            LoadMidiFromPath(firstItem.FilePath, true, firstItem.SourceIndex);
        }
        catch (Exception ex)
        {
            StatusText = "Playlist error: " + ex.Message;
        }
    }

    private void ReplacePlaylist(IReadOnlyList<string> filePaths, string? currentMidiPath, bool preserveExplicitOrder)
    {
        _playlistParseCts?.Cancel();
        Playlist.Clear();

        int currentSourceIndex = -1;
        for (int i = 0; i < filePaths.Count; i++)
        {
            string filePath = filePaths[i];
            if (currentSourceIndex < 0 && !string.IsNullOrWhiteSpace(currentMidiPath) &&
                string.Equals(filePath, currentMidiPath, StringComparison.OrdinalIgnoreCase))
            {
                currentSourceIndex = i;
            }

            Playlist.Add(new PlaylistItem
            {
                SourceIndex = i,
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                IsPlaying = false
            });
        }

        _playlistUsesExplicitOrder = preserveExplicitOrder;
        if (!_playlistUsesExplicitOrder)
        {
            SortPlaylist();
        }

        UpdatePlaylistPlayingState(currentSourceIndex, currentMidiPath);
        UpdateSortIcon();

        if (Playlist.Count == 0)
        {
            return;
        }

        _playlistParseCts = new CancellationTokenSource();
        _ = ParsePlaylistDurationsAsync(_playlistParseCts.Token);
    }

    private int GetCurrentPlaylistIndex()
    {
        if (_currentPlaylistSourceIndex >= 0)
        {
            int indexBySource = Playlist.ToList().FindIndex(item => item.SourceIndex == _currentPlaylistSourceIndex);
            if (indexBySource >= 0)
            {
                return indexBySource;
            }
        }

        if (string.IsNullOrWhiteSpace(_player.MidiPath))
        {
            return -1;
        }

        return Playlist.ToList().FindIndex(item => string.Equals(item.FilePath, _player.MidiPath, StringComparison.OrdinalIgnoreCase));
    }

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
        OnPropertyChanged(nameof(CanBrowseMidiEvents));
        OnPropertyChanged(nameof(ExportButtonText));
    }

    private void UpdateNowPlayingTimeline()
    {
        if (!_player.HasStream)
        {
            return;
        }

        _mediaControls.UpdateNowPlaying(MidiDisplayName, "Kintsugi Midi Player", DurationSeconds, PositionSeconds);
        _mediaControls.UpdatePlaybackState(_player.IsPlaying, PositionSeconds);
    }

    private static string GetExportFormatLabel(AudioExportFormat format)
        => format switch
        {
            AudioExportFormat.Flac => "FLAC",
            AudioExportFormat.Opus => "Opus",
            _ => "WAV"
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnMidiEventsWindowClosed(object? sender, EventArgs e)
    {
        if (_midiEventsWindow is null)
        {
            return;
        }

        _midiEventsWindow.Closed -= OnMidiEventsWindowClosed;
        _midiEventsWindow = null;
    }
}
