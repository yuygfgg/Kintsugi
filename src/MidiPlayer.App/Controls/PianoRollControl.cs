using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Models;
using MidiPlayer.App.Services;

namespace MidiPlayer.App.Controls;

public class PianoRollControl : Control
{
    private const double LookAheadSeconds = 8.0;
    private const double TimeGridStepSeconds = 1.0;
    private const double HitLineGlowHeight = 24.0;
    private const double MinimumVisibleNoteHeight = 2.0;
    private const double NoteInsetRatio = 0.12;

    public static readonly StyledProperty<BassMidiPlayer?> PlayerProperty =
        AvaloniaProperty.Register<PianoRollControl, BassMidiPlayer?>(nameof(Player));

    public static readonly StyledProperty<IReadOnlyList<PianoRollNote>?> NotesProperty =
        AvaloniaProperty.Register<PianoRollControl, IReadOnlyList<PianoRollNote>?>(nameof(Notes));

    public static readonly StyledProperty<int> TransposeOffsetSemitonesProperty =
        AvaloniaProperty.Register<PianoRollControl, int>(nameof(TransposeOffsetSemitones));

    private static readonly Typeface UiTypeface = new("Segoe UI");
    private static readonly IBrush[] NoteBrushes = CreateBrushes(0xC8);
    private static readonly IBrush[] NoteHighlightBrushes = CreateBrushes(0xFF);

    private readonly DispatcherTimer _timer;

    public PianoRollControl()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => InvalidateVisual());
    }

    public BassMidiPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public IReadOnlyList<PianoRollNote>? Notes
    {
        get => GetValue(NotesProperty);
        set => SetValue(NotesProperty, value);
    }

    public int TransposeOffsetSemitones
    {
        get => GetValue(TransposeOffsetSemitonesProperty);
        set => SetValue(TransposeOffsetSemitonesProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        App.Current.SkinManager.SkinChanged += OnSkinChanged;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        App.Current.SkinManager.SkinChanged -= OnSkinChanged;
        _timer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlayerProperty ||
            change.Property == NotesProperty ||
            change.Property == TransposeOffsetSemitonesProperty)
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.DrawRectangle(BackgroundBrush, null, bounds);
        RenderLaneBackground(context, bounds);
        RenderTimeGrid(context, bounds);
        RenderHitLine(context, bounds);
        RenderNotes(context, bounds);
        RenderOverlayText(context, bounds);
    }

    private void RenderLaneBackground(DrawingContext context, Rect bounds)
    {
        for (int note = 0; note < PianoKeyLayout.KeyCount; note++)
        {
            Rect laneRect = PianoKeyLayout.GetLaneRect(bounds, note);
            if (laneRect.Width <= 0 || laneRect.Height <= 0)
            {
                continue;
            }

            context.DrawRectangle(
                PianoKeyLayout.IsBlackKey(note) ? BlackLaneBrush : WhiteLaneBrush,
                LanePen,
                laneRect);
        }
    }

    private void RenderTimeGrid(DrawingContext context, Rect bounds)
    {
        for (double seconds = 0; seconds <= LookAheadSeconds + 0.001; seconds += TimeGridStepSeconds)
        {
            double y = bounds.Bottom - ((seconds / LookAheadSeconds) * bounds.Height);
            context.DrawLine(TimeGridPen, new Point(bounds.X, y), new Point(bounds.Right, y));

            if (seconds >= LookAheadSeconds)
            {
                continue;
            }

            DrawText(
                context,
                $"+{seconds:0}s",
                new Point(bounds.X + 8, Math.Max(bounds.Y + 4, y - 16)),
                TextBrush,
                10,
                TextAlignment.Left);
        }
    }

    private void RenderHitLine(DrawingContext context, Rect bounds)
    {
        Rect glowRect = new(bounds.X, Math.Max(bounds.Y, bounds.Bottom - HitLineGlowHeight), bounds.Width, Math.Min(bounds.Height, HitLineGlowHeight));
        context.DrawRectangle(HitLineGlowBrush, null, glowRect);
        context.DrawLine(HitLinePen, new Point(bounds.X, bounds.Bottom - 1), new Point(bounds.Right, bounds.Bottom - 1));
    }

    private void RenderNotes(DrawingContext context, Rect bounds)
    {
        var player = Player;
        var notes = Notes;
        if (player is null || notes is null || notes.Count == 0 || !player.HasStream)
        {
            return;
        }

        double speedFactor = Math.Max(0.01, player.PlaybackSpeedPercent / 100d);
        double currentSeconds = player.GetPositionSeconds();

        foreach (PianoRollNote note in notes)
        {
            int displayNote = note.Note + TransposeOffsetSemitones;
            if ((uint)displayNote >= PianoKeyLayout.KeyCount)
            {
                continue;
            }

            double startSeconds = note.StartSeconds / speedFactor;
            if (startSeconds > currentSeconds + LookAheadSeconds)
            {
                break;
            }

            double endSeconds = note.EndSeconds / speedFactor;
            if (endSeconds < currentSeconds)
            {
                continue;
            }

            Rect laneRect = PianoKeyLayout.GetLaneRect(bounds, displayNote);
            if (laneRect.Width <= 0 || laneRect.Height <= 0)
            {
                continue;
            }

            double startY = bounds.Bottom - (((startSeconds - currentSeconds) / LookAheadSeconds) * bounds.Height);
            double endY = bounds.Bottom - (((endSeconds - currentSeconds) / LookAheadSeconds) * bounds.Height);
            double visibleTop = Math.Max(bounds.Y, endY);
            double visibleBottom = Math.Min(bounds.Bottom, startY);
            if (visibleBottom <= bounds.Y || visibleTop >= bounds.Bottom)
            {
                continue;
            }

            double inset = Math.Clamp(laneRect.Width * NoteInsetRatio, 1.0, 4.0);
            double width = Math.Max(2.0, laneRect.Width - inset);
            double height = Math.Max(MinimumVisibleNoteHeight, visibleBottom - visibleTop);
            var noteRect = new Rect(laneRect.X + (laneRect.Width - width) / 2d, visibleTop, width, height);

            IBrush noteBrush = (uint)note.Channel < NoteBrushes.Length ? NoteBrushes[note.Channel] : ChannelVisualPalette.GetChannelBrush(note.Channel);
            IBrush highlightBrush = (uint)note.Channel < NoteHighlightBrushes.Length ? NoteHighlightBrushes[note.Channel] : noteBrush;
            context.DrawRectangle(noteBrush, null, noteRect);

            double headHeight = Math.Min(6.0, Math.Max(2.0, noteRect.Height * 0.18));
            var headRect = new Rect(noteRect.X, Math.Max(bounds.Y, noteRect.Bottom - headHeight), noteRect.Width, headHeight);
            context.DrawRectangle(highlightBrush, null, headRect);
        }
    }

    private void RenderOverlayText(DrawingContext context, Rect bounds)
    {
        var player = Player;
        var notes = Notes;
        if (player is null || !player.HasStream)
        {
            DrawCenteredText(context, bounds, "Load a MIDI file to see the piano roll.");
            return;
        }

        if (notes is null || notes.Count == 0)
        {
            DrawCenteredText(context, bounds, "This track has no parsed note events.");
            return;
        }
    }

    private void DrawCenteredText(DrawingContext context, Rect bounds, string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTypeface,
            12,
            EmptyStateBrush);

        context.DrawText(
            formatted,
            new Point(
                bounds.Center.X - (formatted.Width / 2d),
                bounds.Center.Y - (formatted.Height / 2d)));
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        Point anchor,
        IBrush brush,
        double size,
        TextAlignment alignment)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            UiTypeface,
            size,
            brush);

        double x = alignment switch
        {
            TextAlignment.Center => anchor.X - (formatted.Width / 2d),
            TextAlignment.Right => anchor.X - formatted.Width,
            _ => anchor.X
        };

        context.DrawText(formatted, new Point(x, anchor.Y));
    }

    private static IBrush[] CreateBrushes(byte alpha)
    {
        var brushes = new IBrush[ChannelVisualPalette.ChannelColors.Length];
        for (int i = 0; i < brushes.Length; i++)
        {
            Color color = ChannelVisualPalette.ChannelColors[i];
            brushes[i] = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }

        return brushes;
    }

    private static IBrush BackgroundBrush => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(App.Current.SkinManager.GetColor("Theme.PianoRollBackgroundTopColor", "#111926"), 0),
            new GradientStop(App.Current.SkinManager.GetColor("Theme.PianoRollBackgroundBottomColor", "#090D14"), 1)
        ]
    };

    private static IPen LanePen => new Pen(App.Current.SkinManager.GetBrush("Theme.PianoRollLaneBorderBrush", "#172130"), 1);

    private static IPen TimeGridPen => new Pen(App.Current.SkinManager.GetBrush("Theme.PianoRollTimeGridBrush", "#223249"), 1);

    private static IPen HitLinePen => new Pen(App.Current.SkinManager.GetBrush("Theme.PianoRollHitLineBrush", "#EAF6FF"), 1.2);

    private static IBrush WhiteLaneBrush => App.Current.SkinManager.GetBrush("Theme.PianoRollWhiteLaneBrush", "#101723");

    private static IBrush BlackLaneBrush => App.Current.SkinManager.GetBrush("Theme.PianoRollBlackLaneBrush", "#0A111C");

    private static IBrush TextBrush => App.Current.SkinManager.GetBrush("Theme.PianoRollTextBrush", "#8FA4BB");

    private static IBrush EmptyStateBrush => App.Current.SkinManager.GetBrush("Theme.PianoRollEmptyStateBrush", "#6E8094");

    private static IBrush HitLineGlowBrush => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = CreateHitGlowStops()
    };

    private void OnSkinChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    private static GradientStops CreateHitGlowStops()
    {
        var topColor = App.Current.SkinManager.GetColor("Theme.PianoRollHitGlowColor", "#60B8DDFF");
        return
        [
            new GradientStop(topColor, 0),
            new GradientStop(Color.FromArgb(0, topColor.R, topColor.G, topColor.B), 1)
        ];
    }
}
