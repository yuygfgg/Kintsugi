using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Themes.Simple;
using MidiPlayer.App.Styles;
using MidiPlayer.App.Styles.Skins;

namespace MidiPlayer.App.Services;

public sealed class AppSkinManager
{
    public const string ModernDarkSkinId = "modern-dark";
    public const string WindowsClassicSkinId = "windows-classic";

    private static readonly AppSkinDefinition[] BuiltInSkins =
    [
        new(
            ModernDarkSkinId,
            "Modern Dark",
            AppSkinTheme.Fluent,
            ThemeVariant.Dark),
        new(
            WindowsClassicSkinId,
            "Windows 95 / 98",
            AppSkinTheme.Simple,
            ThemeVariant.Light)
    ];

    private readonly Application _application;
    private readonly CommonStyles _commonStyles = new();

    private IStyle? _frameworkTheme;
    private IStyle? _skinStyles;

    public AppSkinManager(Application application)
    {
        _application = application;
    }

    public event EventHandler? SkinChanged;

    public IReadOnlyList<AppSkinDefinition> AvailableSkins => BuiltInSkins;

    public string CurrentSkinId { get; private set; } = string.Empty;

    public bool IsWindowsClassic => string.Equals(CurrentSkinId, WindowsClassicSkinId, StringComparison.Ordinal);

    public void Initialize(string? skinId)
    {
        if (!_application.Styles.Contains(_commonStyles))
        {
            _application.Styles.Add(_commonStyles);
        }

        ApplySkin(skinId);
    }

    public bool ApplySkin(string? skinId)
    {
        var skin = ResolveSkin(skinId);
        if (_skinStyles is not null && string.Equals(CurrentSkinId, skin.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (_frameworkTheme is not null)
        {
            _application.Styles.Remove(_frameworkTheme);
        }

        _frameworkTheme = skin.Theme switch
        {
            AppSkinTheme.Simple => new SimpleTheme(),
            _ => new FluentTheme()
        };

        _application.Styles.Insert(0, _frameworkTheme);

        if (_skinStyles is not null)
        {
            _application.Styles.Remove(_skinStyles);
        }

        _skinStyles = CreateSkinStyles(skin.Id);
        _application.Styles.Add(_skinStyles);
        _application.RequestedThemeVariant = skin.ThemeVariant;
        CurrentSkinId = skin.Id;
        SkinChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IBrush GetBrush(string key, string fallbackColor)
        => GetResource(key) as IBrush ?? new SolidColorBrush(Color.Parse(fallbackColor));

    public Color GetColor(string key, string fallbackColor)
    {
        var resource = GetResource(key);
        return resource switch
        {
            Color color => color,
            ISolidColorBrush brush => brush.Color,
            _ => Color.Parse(fallbackColor)
        };
    }

    private object? GetResource(string key)
    {
        _application.TryGetResource(key, _application.ActualThemeVariant, out var resource);
        return resource;
    }

    private static AppSkinDefinition ResolveSkin(string? skinId)
    {
        if (string.IsNullOrWhiteSpace(skinId))
        {
            return BuiltInSkins[0];
        }

        return BuiltInSkins.FirstOrDefault(skin => string.Equals(skin.Id, skinId.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? BuiltInSkins[0];
    }

    private static IStyle CreateSkinStyles(string skinId)
        => string.Equals(skinId, WindowsClassicSkinId, StringComparison.Ordinal)
            ? new WindowsClassicStyles()
            : new ModernDarkStyles();
}

public sealed record AppSkinDefinition(
    string Id,
    string DisplayName,
    AppSkinTheme Theme,
    ThemeVariant ThemeVariant);

public enum AppSkinTheme
{
    Fluent,
    Simple
}
