using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace MidiPlayer.App.Models;

public sealed class MidiEventBrowserDocument
{
    public required string FileName { get; init; }

    public required string FilePath { get; init; }

    public required string FormatText { get; init; }

    public required string DivisionText { get; init; }

    public required IReadOnlyList<MidiEventBrowserRow> Rows { get; init; }

    public int TrackCount { get; init; }
}

public sealed class MidiEventBrowserRow : INotifyPropertyChanged
{
    private IBrush _foregroundBrush = null!;
    private IBrush _backgroundBrush = null!;
    private FontWeight _fontWeight = FontWeight.Normal;

    public required string TrackText { get; init; }

    public required string LocationText { get; init; }

    public required string TimeText { get; init; }

    public required string StatusText { get; init; }

    public required string ChannelText { get; init; }

    public required string NumberText { get; init; }

    public required string ValueText { get; init; }

    public required string SummaryText { get; init; }

    public required string ToolTipText { get; init; }

    public required long Tick { get; init; }

    public required int ChannelIndex { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IBrush ForegroundBrush
    {
        get => _foregroundBrush;
        set => SetField(ref _foregroundBrush, value);
    }

    public IBrush BackgroundBrush
    {
        get => _backgroundBrush;
        set => SetField(ref _backgroundBrush, value);
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        set => SetField(ref _fontWeight, value);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
