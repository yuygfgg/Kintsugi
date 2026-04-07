using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MidiPlayer.App;

public partial class AudioPluginPickerWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] CommonMacPluginRoots =
    [
        "/Library/Audio/Plug-Ins/VST3",
        "/Library/Audio/Plug-Ins/Components",
        "/System/Library/Components",
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Library",
            "Audio",
            "Plug-Ins",
            "VST3"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal),
            "Library",
            "Audio",
            "Plug-Ins",
            "Components")
    ];

    private AudioPluginChoice? _selectedPlugin;
    private string _manualPath = string.Empty;
    private string _statusText = "Scanning standard macOS plug-in folders...";

    public AudioPluginPickerWindow()
    {
        InitializeComponent();
        App.Current.SkinManager.ApplySkinToWindow(this);
        DataContext = this;
        ReloadPluginList();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AudioPluginChoice> AvailablePlugins { get; } = [];

    public string? SelectedPath { get; private set; }

    public AudioPluginChoice? SelectedPlugin
    {
        get => _selectedPlugin;
        set
        {
            if (!SetField(ref _selectedPlugin, value))
            {
                return;
            }

            if (value is not null && !string.Equals(_manualPath, value.Path, StringComparison.Ordinal))
            {
                _manualPath = value.Path;
                OnPropertyChanged(nameof(ManualPath));
            }

            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public string ManualPath
    {
        get => _manualPath;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (!SetField(ref _manualPath, normalized))
            {
                return;
            }

            if (_selectedPlugin is not null &&
                !string.Equals(_selectedPlugin.Path, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _selectedPlugin = null;
                OnPropertyChanged(nameof(SelectedPlugin));
            }

            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool CanConfirm => TryNormalizePluginPath(ManualPath, out _);

    private void ReloadPluginList()
    {
        var discoveredPlugins = FindAvailablePlugins().ToArray();

        AvailablePlugins.Clear();
        foreach (var plugin in discoveredPlugins)
        {
            AvailablePlugins.Add(plugin);
        }

        StatusText = discoveredPlugins.Length switch
        {
            0 => "No VST3 or Audio Unit bundles were found in the standard macOS plug-in folders.",
            1 => $"Found 1 plug-in: {discoveredPlugins[0].DisplayName}",
            _ => $"Found {discoveredPlugins.Length} plug-ins in the standard macOS VST3 / Components folders."
        };
    }

    private void OnRefreshClicked(object? sender, RoutedEventArgs e)
    {
        ReloadPluginList();
    }

    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        if (!TryNormalizePluginPath(ManualPath, out var selectedPath))
        {
            StatusText = "Choose an existing local .vst3 or .component bundle.";
            return;
        }

        SelectedPath = selectedPath;
        Close();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        SelectedPath = null;
        Close();
    }

    private static IEnumerable<AudioPluginChoice> FindAvailablePlugins()
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in CommonMacPluginRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var path in EnumeratePluginBundles(root))
            {
                if (!seenPaths.Add(path))
                {
                    continue;
                }

                yield return new AudioPluginChoice(
                    Path.GetFileNameWithoutExtension(path),
                    path.EndsWith(".component", StringComparison.OrdinalIgnoreCase) ? "AU" : "VST3",
                    path);
            }
        }
    }

    private static IEnumerable<string> EnumeratePluginBundles(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            yield break;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(root);

        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            IEnumerable<string> childDirectories;

            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories)
            {
                if (IsSupportedPluginBundle(childDirectory))
                {
                    yield return childDirectory;
                    continue;
                }

                pendingDirectories.Push(childDirectory);
            }
        }
    }

    private static bool TryNormalizePluginPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        normalizedPath = path.Trim().Trim('"');
        return IsSupportedPluginBundle(normalizedPath);
    }

    private static bool IsSupportedPluginBundle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (!path.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".component", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Directory.Exists(path) || File.Exists(path);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public sealed record AudioPluginChoice(string DisplayName, string FormatLabel, string Path);
}
