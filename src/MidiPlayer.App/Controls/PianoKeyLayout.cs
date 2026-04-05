using System;
using Avalonia;

namespace MidiPlayer.App.Controls;

internal static class PianoKeyLayout
{
    public const int KeyCount = 128;
    private const double BlackKeyWidthRatio = 0.6;
    private const double BlackKeyHeightRatio = 0.65;

    private static readonly bool[] IsBlack = new bool[KeyCount];
    private static readonly int[] WhiteIndex = new int[KeyCount];

    static PianoKeyLayout()
    {
        int whiteKeyCount = 0;
        for (int note = 0; note < KeyCount; note++)
        {
            int noteInOctave = note % 12;
            bool isBlack = noteInOctave == 1 || noteInOctave == 3 || noteInOctave == 6 || noteInOctave == 8 || noteInOctave == 10;
            IsBlack[note] = isBlack;
            if (!isBlack)
            {
                WhiteIndex[note] = whiteKeyCount++;
            }
            else
            {
                WhiteIndex[note] = whiteKeyCount;
            }
        }

        WhiteKeyCount = whiteKeyCount;
    }

    public static int WhiteKeyCount { get; }

    public static bool IsBlackKey(int note)
        => (uint)note < KeyCount && IsBlack[note];

    public static Rect GetKeyRect(Rect bounds, int note)
    {
        if ((uint)note >= KeyCount || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return default;
        }

        double whiteKeyWidth = bounds.Width / WhiteKeyCount;
        if (!IsBlack[note])
        {
            double x = bounds.X + (WhiteIndex[note] * whiteKeyWidth);
            return new Rect(x, bounds.Y, whiteKeyWidth, bounds.Height);
        }

        double blackKeyWidth = whiteKeyWidth * BlackKeyWidthRatio;
        double blackKeyHeight = bounds.Height * BlackKeyHeightRatio;
        double blackX = bounds.X + (WhiteIndex[note] * whiteKeyWidth) - (blackKeyWidth / 2d);
        return new Rect(blackX, bounds.Y, blackKeyWidth, blackKeyHeight);
    }

    public static Rect GetLaneRect(Rect bounds, int note)
    {
        Rect keyRect = GetKeyRect(bounds, note);
        return keyRect.Width <= 0 || keyRect.Height <= 0
            ? default
            : new Rect(keyRect.X, bounds.Y, keyRect.Width, bounds.Height);
    }
}
