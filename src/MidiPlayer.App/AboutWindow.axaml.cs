using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MidiPlayer.App;

public partial class AboutWindow : Window
{
    public string VersionText { get; }

    public AboutWindow()
    {
        InitializeComponent();
        App.Current.SkinManager.ApplySkinToWindow(this);
        VersionText = BuildVersionText();
        DataContext = this;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string BuildVersionText()
    {
        var assembly = typeof(AboutWindow).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informationalVersion?.Split('+', 2)[0];

        if (!string.IsNullOrWhiteSpace(version))
        {
            return $"Version {version}";
        }

        return assembly.GetName().Version is Version assemblyVersion
            ? $"Version {assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
            : "Version unavailable";
    }
}
