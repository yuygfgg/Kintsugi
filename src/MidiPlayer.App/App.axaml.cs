using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class App : Application
{
    public AppSkinManager SkinManager { get; private set; } = null!;

    public new static App Current => (App)Application.Current!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        SkinManager = new AppSkinManager(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        SkinManager.Initialize(AppSettings.Load().UiSkinId);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
