using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Controls;
using MidiPlayer.App.Models;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class MidiEventsWindow : Window, INotifyPropertyChanged
{
    private BassMidiPlayer? _player;
    private readonly DispatcherTimer _followTimer;
    private IReadOnlyList<MidiEventBrowserRow> _rows = Array.Empty<MidiEventBrowserRow>();
    private MidiEventBrowserRow[] _orderedRows = Array.Empty<MidiEventBrowserRow>();
    private int[][] _channelRowIndexes = CreateEmptyChannelIndexTable();
    private HashSet<int> _activeRowIndexes = [];
    private string _fileName = "No MIDI Loaded";
    private string _filePath = "Open a MIDI file in the main player, then browse its event list here.";
    private string _summaryText = "No data";
    private string _emptyStateTitle = "No MIDI Data";
    private string _emptyStateText = "Load a MIDI file from the player to inspect its events.";
    private string? _loadedPath;
    private int _loadVersion;
    private long _lastFollowTick = -1;
    private int _focusedRowIndex = -1;

    public MidiEventsWindow()
    {
        InitializeComponent();
        App.Current.SkinManager.ApplySkinToWindow(this);
        DataContext = this;
        _followTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, OnFollowTimerTick);
        _followTimer.Start();
        Closed += OnWindowClosed;
    }

    public MidiEventsWindow(BassMidiPlayer player) : this()
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<MidiEventBrowserRow> Rows
    {
        get => _rows;
        private set
        {
            if (ReferenceEquals(_rows, value))
            {
                return;
            }

            _rows = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    public string FileName
    {
        get => _fileName;
        private set => SetField(ref _fileName, value);
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetField(ref _filePath, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetField(ref _summaryText, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetField(ref _emptyStateTitle, value);
    }

    public string EmptyStateText
    {
        get => _emptyStateText;
        private set => SetField(ref _emptyStateText, value);
    }

    public bool IsEmpty => Rows.Count == 0;

    public async Task LoadMidiAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int version = Interlocked.Increment(ref _loadVersion);
        _loadedPath = path;
        FileName = System.IO.Path.GetFileName(path);
        FilePath = path;
        SummaryText = "Parsing events...";
        EmptyStateTitle = "Loading";
        EmptyStateText = "Reading track data, meta events, and controller messages.";
        Rows = Array.Empty<MidiEventBrowserRow>();

        try
        {
            MidiEventBrowserDocument document = await Task.Run(() => MidiFileAnalysisService.Load(path));
            if (version != _loadVersion || !string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FileName = document.FileName;
            FilePath = document.FilePath;
            SummaryText = $"{document.FormatText}  |  {document.DivisionText}  |  {document.TrackCount} tracks  |  {document.Rows.Count:N0} events";
            BuildPlaybackIndex(document.Rows);
            ResetRowVisuals();
            Rows = document.Rows;
            UpdatePlaybackHighlight(forceScroll: true);
            EmptyStateTitle = "No Events";
            EmptyStateText = "The file was parsed successfully, but no browsable MIDI events were found.";
        }
        catch (Exception ex)
        {
            if (version != _loadVersion || !string.Equals(_loadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SummaryText = "Parse failed";
            Rows = Array.Empty<MidiEventBrowserRow>();
            _orderedRows = Array.Empty<MidiEventBrowserRow>();
            _channelRowIndexes = CreateEmptyChannelIndexTable();
            _activeRowIndexes.Clear();
            _focusedRowIndex = -1;
            _lastFollowTick = -1;
            EmptyStateTitle = "Unable To Parse MIDI";
            EmptyStateText = ex.Message;
        }
    }

    private void OnFollowTimerTick(object? sender, EventArgs e)
    {
        if (!IsVisible || _orderedRows.Length == 0)
        {
            return;
        }

        UpdatePlaybackHighlight();
    }

    private void BuildPlaybackIndex(IReadOnlyList<MidiEventBrowserRow> rows)
    {
        _orderedRows = rows as MidiEventBrowserRow[] ?? [.. rows];

        var channelIndexes = new List<int>[16];
        for (int channel = 0; channel < channelIndexes.Length; channel++)
        {
            channelIndexes[channel] = [];
        }

        for (int index = 0; index < _orderedRows.Length; index++)
        {
            int channelIndex = _orderedRows[index].ChannelIndex;
            if ((uint)channelIndex < channelIndexes.Length)
            {
                channelIndexes[channelIndex].Add(index);
            }
        }

        _channelRowIndexes = new int[channelIndexes.Length][];
        for (int channel = 0; channel < channelIndexes.Length; channel++)
        {
            _channelRowIndexes[channel] = [.. channelIndexes[channel]];
        }

        _activeRowIndexes.Clear();
        _focusedRowIndex = -1;
        _lastFollowTick = -1;
    }

    private void ResetRowVisuals()
    {
        foreach (MidiEventBrowserRow row in _orderedRows)
        {
            row.ForegroundBrush = ChannelVisualPalette.DefaultTextBrush;
            row.BackgroundBrush = Brushes.Transparent;
            row.FontWeight = FontWeight.Normal;
        }
    }

    private void UpdatePlaybackHighlight(bool forceScroll = false)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_loadedPath) || !string.Equals(_loadedPath, _player.MidiPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearPlaybackHighlight();
            return;
        }

        long tick = _player.GetPositionTicks();
        if (!forceScroll && tick == _lastFollowTick && _player.IsPlaying)
        {
            return;
        }

        _lastFollowTick = tick;

        int focusIndex = FindLastRowIndexAtOrBefore(tick);
        var newActiveIndexes = new HashSet<int>();
        if (focusIndex >= 0)
        {
            newActiveIndexes.Add(focusIndex);
        }

        for (int channel = 0; channel < _channelRowIndexes.Length; channel++)
        {
            int channelRowIndex = FindLastChannelRowIndexAtOrBefore(_channelRowIndexes[channel], tick);
            if (channelRowIndex >= 0)
            {
                newActiveIndexes.Add(channelRowIndex);
            }
        }

        ApplyPlaybackHighlight(newActiveIndexes, focusIndex, forceScroll);
    }

    private void ApplyPlaybackHighlight(HashSet<int> newActiveIndexes, int focusIndex, bool forceScroll)
    {
        foreach (int index in _activeRowIndexes)
        {
            if (newActiveIndexes.Contains(index))
            {
                continue;
            }

            ApplyRowStyle(_orderedRows[index], isActive: false, isFocused: false);
        }

        foreach (int index in newActiveIndexes)
        {
            ApplyRowStyle(_orderedRows[index], isActive: true, isFocused: index == focusIndex);
        }

        _activeRowIndexes = newActiveIndexes;

        if (focusIndex >= 0 && (forceScroll || focusIndex != _focusedRowIndex))
        {
            _focusedRowIndex = focusIndex;
            try
            {
                RowsListBox.ScrollIntoView(_orderedRows[focusIndex]);
            }
            catch
            {
                // Keep playback highlight working even if the platform handler cannot scroll programmatically.
            }
        }
    }

    private void ApplyRowStyle(MidiEventBrowserRow row, bool isActive, bool isFocused)
    {
        if (!isActive)
        {
            row.ForegroundBrush = ChannelVisualPalette.DefaultTextBrush;
            row.BackgroundBrush = Brushes.Transparent;
            row.FontWeight = FontWeight.Normal;
            return;
        }

        if (row.ChannelIndex >= 0)
        {
            row.ForegroundBrush = ChannelVisualPalette.GetChannelBrush(row.ChannelIndex);
            row.BackgroundBrush = isFocused
                ? ChannelVisualPalette.GetFocusBackgroundBrush(row.ChannelIndex)
                : ChannelVisualPalette.GetActiveBackgroundBrush(row.ChannelIndex);
        }
        else
        {
            row.ForegroundBrush = ChannelVisualPalette.NeutralCurrentTextBrush;
            row.BackgroundBrush = isFocused
                ? ChannelVisualPalette.FocusBackgroundBrush
                : ChannelVisualPalette.NeutralActiveBackgroundBrush;
        }

        row.FontWeight = isFocused ? FontWeight.Bold : FontWeight.SemiBold;
    }

    private void ClearPlaybackHighlight()
    {
        if (_orderedRows.Length == 0 && _activeRowIndexes.Count == 0)
        {
            return;
        }

        foreach (int index in _activeRowIndexes)
        {
            ApplyRowStyle(_orderedRows[index], isActive: false, isFocused: false);
        }

        _activeRowIndexes.Clear();
        _focusedRowIndex = -1;
        _lastFollowTick = -1;
    }

    private int FindLastRowIndexAtOrBefore(long tick)
    {
        if (_orderedRows.Length == 0)
        {
            return -1;
        }

        int low = 0;
        int high = _orderedRows.Length - 1;
        int result = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            if (_orderedRows[mid].Tick <= tick)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private int FindLastChannelRowIndexAtOrBefore(int[] channelIndexes, long tick)
    {
        if (channelIndexes.Length == 0)
        {
            return -1;
        }

        int low = 0;
        int high = channelIndexes.Length - 1;
        int result = -1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            int rowIndex = channelIndexes[mid];
            if (_orderedRows[rowIndex].Tick <= tick)
            {
                result = rowIndex;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    private static int[][] CreateEmptyChannelIndexTable()
    {
        var indexes = new int[16][];
        for (int channel = 0; channel < indexes.Length; channel++)
        {
            indexes[channel] = Array.Empty<int>();
        }

        return indexes;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _followTimer.Stop();
        Closed -= OnWindowClosed;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
