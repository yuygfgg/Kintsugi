using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App.Controls;

public class ChannelMonitorControl : Control
{
    public static readonly StyledProperty<BassMidiPlayer?> PlayerProperty =
        AvaloniaProperty.Register<ChannelMonitorControl, BassMidiPlayer?>(nameof(Player));

    public BassMidiPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    private static readonly IBrush[] ChannelBrushes =
    {
        Brushes.Red, Brushes.Orange, Brushes.Yellow, Brushes.LimeGreen,
        Brushes.Cyan, Brushes.DodgerBlue, Brushes.Indigo, Brushes.Violet,
        Brushes.Magenta, Brushes.Purple, Brushes.DeepPink, Brushes.Goldenrod,
        Brushes.Teal, Brushes.Navy, Brushes.Maroon, Brushes.Olive
    };

    private static readonly IBrush InactiveBrush = new SolidColorBrush(Color.Parse("#303030"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#1A1A1A"));
    private static readonly IPen OutlinePen = new Pen(new SolidColorBrush(Color.Parse("#111")), 1);

    public ChannelMonitorControl()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var player = Player;
        if (player == null) return;

        var point = e.GetCurrentPoint(this).Position;
        var bounds = Bounds;
        double itemWidth = bounds.Width / 16.0;

        int ch = (int)(point.X / itemWidth);
        if (ch >= 0 && ch < 16)
        {
            player.ToggleChannelMute(ch);
            InvalidateVisual();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PlayerProperty)
        {
            if (change.OldValue is BassMidiPlayer oldPlayer)
            {
                oldPlayer.NotesChanged -= OnNotesChanged;
            }
            if (change.NewValue is BassMidiPlayer newPlayer)
            {
                newPlayer.NotesChanged += OnNotesChanged;
            }
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    private void OnNotesChanged()
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(DrawingContext context)
    {
        var player = Player;
        if (player == null) return;

        var bounds = Bounds;
        double itemWidth = bounds.Width / 16.0;

        for (int ch = 0; ch < 16; ch++)
        {
            bool isMuted = player.IsChannelMuted(ch);
            bool isActive = false;
            if (!isMuted)
            {
                for (int i = 0; i < 128; i++)
                {
                    if (player.ActiveNotes[ch, i])
                    {
                        isActive = true;
                        break;
                    }
                }
            }

            var rect = new Rect(ch * itemWidth + 2, 2, itemWidth - 4, bounds.Height - 4);
            IBrush fill = isMuted ? MutedBrush : (isActive ? ChannelBrushes[ch] : InactiveBrush);
            context.DrawRectangle(fill, OutlinePen, rect);

            // Draw channel number
            var text = new FormattedText(
                (ch + 1).ToString(),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Arial"),
                10,
                isMuted ? Brushes.DimGray : Brushes.White);
            
            context.DrawText(text, new Point(rect.Center.X - text.Width / 2, rect.Center.Y - text.Height / 2));
        }
    }
}