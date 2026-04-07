using Avalonia.Media;

namespace MidiPlayer.App.Controls;

internal static class ChannelVisualPalette
{
    public static readonly Color[] ChannelColors =
    [
        Colors.Red, Colors.Orange, Colors.Yellow, Colors.LimeGreen,
        Colors.Cyan, Colors.DodgerBlue, Colors.Indigo, Colors.Violet,
        Colors.Magenta, Colors.Purple, Colors.DeepPink, Colors.Goldenrod,
        Colors.Teal, Colors.Navy, Colors.Maroon, Colors.Olive
    ];

    public static readonly IBrush[] ChannelBrushes =
    [
        new SolidColorBrush(ChannelColors[0]),
        new SolidColorBrush(ChannelColors[1]),
        new SolidColorBrush(ChannelColors[2]),
        new SolidColorBrush(ChannelColors[3]),
        new SolidColorBrush(ChannelColors[4]),
        new SolidColorBrush(ChannelColors[5]),
        new SolidColorBrush(ChannelColors[6]),
        new SolidColorBrush(ChannelColors[7]),
        new SolidColorBrush(ChannelColors[8]),
        new SolidColorBrush(ChannelColors[9]),
        new SolidColorBrush(ChannelColors[10]),
        new SolidColorBrush(ChannelColors[11]),
        new SolidColorBrush(ChannelColors[12]),
        new SolidColorBrush(ChannelColors[13]),
        new SolidColorBrush(ChannelColors[14]),
        new SolidColorBrush(ChannelColors[15])
    ];

    public static IBrush DefaultTextBrush => App.Current.SkinManager.GetBrush("Theme.ChannelDefaultTextBrush", "#D7DEE6");
    public static IBrush NeutralCurrentTextBrush => App.Current.SkinManager.GetBrush("Theme.ChannelNeutralCurrentTextBrush", "#DCEEFF");
    public static IBrush InactiveBrush => App.Current.SkinManager.GetBrush("Theme.ChannelInactiveBrush", "#303030");
    public static IBrush MutedBrush => App.Current.SkinManager.GetBrush("Theme.ChannelMutedBrush", "#1A1A1A");
    public static IBrush FocusBackgroundBrush => App.Current.SkinManager.GetBrush("Theme.ChannelFocusBackgroundBrush", "#16293B");
    public static IBrush NeutralActiveBackgroundBrush => App.Current.SkinManager.GetBrush("Theme.ChannelNeutralActiveBackgroundBrush", "#132131");

    private static readonly IBrush[] ActiveBackgroundBrushes =
    [
        CreateOverlayBrush(ChannelColors[0], 0x22),
        CreateOverlayBrush(ChannelColors[1], 0x22),
        CreateOverlayBrush(ChannelColors[2], 0x22),
        CreateOverlayBrush(ChannelColors[3], 0x22),
        CreateOverlayBrush(ChannelColors[4], 0x22),
        CreateOverlayBrush(ChannelColors[5], 0x22),
        CreateOverlayBrush(ChannelColors[6], 0x22),
        CreateOverlayBrush(ChannelColors[7], 0x22),
        CreateOverlayBrush(ChannelColors[8], 0x22),
        CreateOverlayBrush(ChannelColors[9], 0x22),
        CreateOverlayBrush(ChannelColors[10], 0x22),
        CreateOverlayBrush(ChannelColors[11], 0x22),
        CreateOverlayBrush(ChannelColors[12], 0x22),
        CreateOverlayBrush(ChannelColors[13], 0x22),
        CreateOverlayBrush(ChannelColors[14], 0x22),
        CreateOverlayBrush(ChannelColors[15], 0x22)
    ];

    private static readonly IBrush[] FocusBackgroundBrushes =
    [
        CreateOverlayBrush(ChannelColors[0], 0x35),
        CreateOverlayBrush(ChannelColors[1], 0x35),
        CreateOverlayBrush(ChannelColors[2], 0x35),
        CreateOverlayBrush(ChannelColors[3], 0x35),
        CreateOverlayBrush(ChannelColors[4], 0x35),
        CreateOverlayBrush(ChannelColors[5], 0x35),
        CreateOverlayBrush(ChannelColors[6], 0x35),
        CreateOverlayBrush(ChannelColors[7], 0x35),
        CreateOverlayBrush(ChannelColors[8], 0x35),
        CreateOverlayBrush(ChannelColors[9], 0x35),
        CreateOverlayBrush(ChannelColors[10], 0x35),
        CreateOverlayBrush(ChannelColors[11], 0x35),
        CreateOverlayBrush(ChannelColors[12], 0x35),
        CreateOverlayBrush(ChannelColors[13], 0x35),
        CreateOverlayBrush(ChannelColors[14], 0x35),
        CreateOverlayBrush(ChannelColors[15], 0x35)
    ];

    public static IBrush GetChannelBrush(int channel)
        => (uint)channel < ChannelBrushes.Length ? ChannelBrushes[channel] : DefaultTextBrush;

    public static IBrush GetActiveBackgroundBrush(int channel)
        => (uint)channel < ActiveBackgroundBrushes.Length ? ActiveBackgroundBrushes[channel] : NeutralActiveBackgroundBrush;

    public static IBrush GetFocusBackgroundBrush(int channel)
        => (uint)channel < FocusBackgroundBrushes.Length ? FocusBackgroundBrushes[channel] : FocusBackgroundBrush;

    private static IBrush CreateOverlayBrush(Color baseColor, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
}
