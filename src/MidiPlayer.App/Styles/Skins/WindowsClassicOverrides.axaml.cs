using Avalonia.Markup.Xaml;

namespace MidiPlayer.App.Styles.Skins;

public partial class WindowsClassicOverrideStyles : Avalonia.Styling.Styles
{
    public WindowsClassicOverrideStyles()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
