using System;
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

    private static readonly IBrush[] ChannelBrushes =
    {
        Brushes.Red, Brushes.Orange, Brushes.Yellow, Brushes.LimeGreen,
        Brushes.Cyan, Brushes.DodgerBlue, Brushes.Indigo, Brushes.Violet,
        Brushes.Magenta, Brushes.Purple, Brushes.DeepPink, Brushes.Goldenrod,
        Brushes.Teal, Brushes.Navy, Brushes.Maroon, Brushes.Olive
    };

    private static readonly IBrush WhiteKeyBrush = new SolidColorBrush(Color.Parse("#E0E0E0"));
    private static readonly IBrush BlackKeyBrush = new SolidColorBrush(Color.Parse("#202020"));
    private static readonly IPen OutlinePen = new Pen(new SolidColorBrush(Color.Parse("#111")), 1);

    private readonly bool[] _isBlack = new bool[128];
    private readonly int[] _whiteIndex = new int[128];
    private int _numWhiteKeys = 0;

    public PianoControl()
    {
        ClipToBounds = true;

        for (int i = 0; i < 128; i++)
        {
            int noteInOctave = i % 12;
            bool isBlack = noteInOctave == 1 || noteInOctave == 3 || noteInOctave == 6 || noteInOctave == 8 || noteInOctave == 10;
            _isBlack[i] = isBlack;
            if (!isBlack)
            {
                _whiteIndex[i] = _numWhiteKeys++;
            }
            else
            {
                _whiteIndex[i] = _numWhiteKeys;
            }
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
        else if (change.Property == TransposeOffsetSemitonesProperty)
        {
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
        double whiteKeyWidth = bounds.Width / _numWhiteKeys;
        double blackKeyWidth = whiteKeyWidth * 0.6;
        double blackKeyHeight = bounds.Height * 0.65;

        // Draw white keys
        for (int i = 0; i < 128; i++)
        {
            if (!_isBlack[i])
            {
                double x = _whiteIndex[i] * whiteKeyWidth;
                var rect = new Rect(x, 0, whiteKeyWidth, bounds.Height);
                
                IBrush fill = WhiteKeyBrush;
                int sourceNote = i - TransposeOffsetSemitones;
                for (int ch = 0; ch < 16; ch++)
                {
                    if ((uint)sourceNote < 128 && player.ActiveNotes[ch, sourceNote])
                    {
                        fill = ChannelBrushes[ch];
                        break;
                    }
                }
                
                context.DrawRectangle(fill, OutlinePen, rect);
            }
        }

        // Draw black keys
        for (int i = 0; i < 128; i++)
        {
            if (_isBlack[i])
            {
                double x = _whiteIndex[i] * whiteKeyWidth - (blackKeyWidth / 2);
                var rect = new Rect(x, 0, blackKeyWidth, blackKeyHeight);
                
                IBrush fill = BlackKeyBrush;
                int sourceNote = i - TransposeOffsetSemitones;
                for (int ch = 0; ch < 16; ch++)
                {
                    if ((uint)sourceNote < 128 && player.ActiveNotes[ch, sourceNote])
                    {
                        fill = ChannelBrushes[ch];
                        break;
                    }
                }

                context.DrawRectangle(fill, OutlinePen, rect);
            }
        }
    }
}
