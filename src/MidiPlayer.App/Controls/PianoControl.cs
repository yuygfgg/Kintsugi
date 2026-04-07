using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App.Controls;

public class PianoControl : Control
{
    public static readonly StyledProperty<BassMidiPlayer?> PlayerProperty =
        AvaloniaProperty.Register<PianoControl, BassMidiPlayer?>(nameof(Player));

    public static readonly StyledProperty<int> TransposeOffsetSemitonesProperty =
        AvaloniaProperty.Register<PianoControl, int>(nameof(TransposeOffsetSemitones));

    public BassMidiPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public int TransposeOffsetSemitones
    {
        get => GetValue(TransposeOffsetSemitonesProperty);
        set => SetValue(TransposeOffsetSemitonesProperty, value);
    }

    public PianoControl()
    {
        ClipToBounds = true;
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
        else if (change.Property == TransposeOffsetSemitonesProperty)
        {
            Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        App.Current.SkinManager.SkinChanged += OnSkinChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        App.Current.SkinManager.SkinChanged -= OnSkinChanged;
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
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Draw white keys
        for (int i = 0; i < PianoKeyLayout.KeyCount; i++)
        {
            if (!PianoKeyLayout.IsBlackKey(i))
            {
                Rect rect = PianoKeyLayout.GetKeyRect(bounds, i);
                IBrush fill = WhiteKeyBrush;
                int sourceNote = i - TransposeOffsetSemitones;
                for (int ch = 0; ch < 16; ch++)
                {
                    if ((uint)sourceNote < 128 && player.ActiveNotes[ch, sourceNote])
                    {
                        fill = ChannelVisualPalette.GetChannelBrush(ch);
                        break;
                    }
                }

                context.DrawRectangle(fill, OutlinePen, rect);
            }
        }

        // Draw black keys
        for (int i = 0; i < PianoKeyLayout.KeyCount; i++)
        {
            if (PianoKeyLayout.IsBlackKey(i))
            {
                Rect rect = PianoKeyLayout.GetKeyRect(bounds, i);
                IBrush fill = BlackKeyBrush;
                int sourceNote = i - TransposeOffsetSemitones;
                for (int ch = 0; ch < 16; ch++)
                {
                    if ((uint)sourceNote < 128 && player.ActiveNotes[ch, sourceNote])
                    {
                        fill = ChannelVisualPalette.GetChannelBrush(ch);
                        break;
                    }
                }

                context.DrawRectangle(fill, OutlinePen, rect);
            }
        }
    }

    private static IBrush WhiteKeyBrush => App.Current.SkinManager.GetBrush("Theme.PianoWhiteKeyBrush", "#E0E0E0");

    private static IBrush BlackKeyBrush => App.Current.SkinManager.GetBrush("Theme.PianoBlackKeyBrush", "#202020");

    private static IPen OutlinePen => new Pen(App.Current.SkinManager.GetBrush("Theme.ControlOutlineBrush", "#111111"), 1);

    private void OnSkinChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }
}
