using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
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

    private static readonly IPen OutlinePen = new Pen(new SolidColorBrush(Color.Parse("#111")), 1);
    private readonly DispatcherTimer _pendingMuteTimer;
    private int _pendingMuteChannel = -1;
    private int _hoveredChannel = -1;

    public event EventHandler<ChannelMixerRequestedEventArgs>? ChannelMixerRequested;

    public ChannelMonitorControl()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetShowDelay(this, 0);
        ToolTip.SetBetweenShowDelay(this, 0);

        _pendingMuteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pendingMuteTimer.Tick += OnPendingMuteTimerTick;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var player = Player;
        if (player == null) return;

        var currentPoint = e.GetCurrentPoint(this);
        int? channel = GetChannelAt(currentPoint.Position);
        ToolTip.SetIsOpen(this, false);
        if (channel is null)
        {
            return;
        }

        if (currentPoint.Properties.IsRightButtonPressed)
        {
            CancelPendingMute(channel.Value);
            ChannelMixerRequested?.Invoke(this, new ChannelMixerRequestedEventArgs(channel.Value));
            e.Handled = true;
            return;
        }

        if (!currentPoint.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 1)
        {
            QueuePendingMute(channel.Value, currentPoint.Pointer.Type);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2)
        {
            CancelPendingMute(channel.Value);
            player.ToggleChannelSolo(channel.Value);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateHoveredChannel(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHoveredChannel();
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
            Dispatcher.UIThread.Post(() =>
            {
                RefreshToolTip();
                InvalidateVisual();
            });
        }
    }

    private void OnNotesChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshToolTip();
            InvalidateVisual();
        });
    }

    private int? GetChannelAt(Point point)
    {
        if (Bounds.Width <= 0)
        {
            return null;
        }

        double itemWidth = Bounds.Width / 16.0;
        int channel = (int)(point.X / itemWidth);
        return channel is >= 0 and < 16 ? channel : null;
    }

    private void QueuePendingMute(int channel, PointerType pointerType)
    {
        if (_pendingMuteChannel >= 0 && _pendingMuteChannel != channel)
        {
            CommitPendingMute();
        }

        _pendingMuteChannel = channel;
        _pendingMuteTimer.Stop();
        _pendingMuteTimer.Interval = TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(pointerType)
            ?? TimeSpan.FromMilliseconds(250);
        _pendingMuteTimer.Start();
    }

    private void UpdateHoveredChannel(Point point)
    {
        int hoveredChannel = Player is { HasStream: true }
            ? GetChannelAt(point) ?? -1
            : -1;

        if (_hoveredChannel == hoveredChannel)
        {
            return;
        }

        _hoveredChannel = hoveredChannel;
        RefreshToolTip();
    }

    private void ClearHoveredChannel()
    {
        if (_hoveredChannel < 0)
        {
            ToolTip.SetIsOpen(this, false);
            return;
        }

        _hoveredChannel = -1;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }

    private void CancelPendingMute(int channel)
    {
        if (_pendingMuteChannel != channel)
        {
            return;
        }

        _pendingMuteTimer.Stop();
        _pendingMuteChannel = -1;
    }

    private void OnPendingMuteTimerTick(object? sender, EventArgs e)
    {
        _pendingMuteTimer.Stop();
        CommitPendingMute();
    }

    private void CommitPendingMute()
    {
        var player = Player;
        if (player == null || _pendingMuteChannel < 0)
        {
            _pendingMuteChannel = -1;
            return;
        }

        int channel = _pendingMuteChannel;
        _pendingMuteChannel = -1;
        player.ToggleChannelMute(channel);
        InvalidateVisual();
    }

    private void RefreshToolTip()
    {
        var player = Player;
        if (player is null || !player.HasStream || _hoveredChannel < 0)
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
            return;
        }

        string instrumentName = player.GetChannelInstrumentName(_hoveredChannel);
        if (string.IsNullOrWhiteSpace(instrumentName))
        {
            ToolTip.SetIsOpen(this, false);
            ToolTip.SetTip(this, null);
            return;
        }

        ToolTip.SetTip(this, $"CH {_hoveredChannel + 1:00}: {instrumentName}");
        ToolTip.SetIsOpen(this, true);
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
            IBrush fill = isMuted ? ChannelVisualPalette.MutedBrush : (isActive ? ChannelVisualPalette.ChannelBrushes[ch] : ChannelVisualPalette.InactiveBrush);
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

public sealed class ChannelMixerRequestedEventArgs(int channel) : EventArgs
{
    public int Channel { get; } = channel;
}
