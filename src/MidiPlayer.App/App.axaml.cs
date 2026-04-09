using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class App : Application
{
    public AppSkinManager SkinManager { get; private set; } = null!;

    public new static App Current => (App)Application.Current!;

    public App()
    {
        Name = "Kintsugi Midi Player";
    }

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

    private async void OnAboutRequested(object? sender, EventArgs e)
    {
        var aboutWindow = new AboutWindow();
        var owner = GetMainWindow();

        if (owner is null)
        {
            aboutWindow.Show();
            return;
        }

        await aboutWindow.ShowDialog(owner);
    }

    private void OnPreferencesRequested(object? sender, EventArgs e)
    {
        GetMainWindow()?.OpenSettingsWindow();
    }

    private MainWindow? GetMainWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.MainWindow as MainWindow;
    }
}
