using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ManagedBass.Midi;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private readonly BassMidiPlayer? _player;
    private readonly AppSettings _settings;
    private string _soundFontDisplayName = "No SoundFont loaded";
    private bool _isInitializing = true;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContext = this;
        _settings = new AppSettings();
    }

    public SettingsWindow(BassMidiPlayer player) : this()
    {
        _player = player;
        _settings = AppSettings.Load();
        UpdateSoundFontDisplay();

        SystemModeComboBox.SelectedIndex = _player.SystemMode switch
        {
            MidiSystem.GM1 => 1,
            MidiSystem.GM2 => 2,
            MidiSystem.XG => 3,
            MidiSystem.GS => 4,
            _ => 0
        };
        SampleRateComboBox.SelectedIndex = _player.SampleRate switch
        {
            48000 => 1,
            88200 => 2,
            96000 => 3,
            _ => 0
        };
        _isInitializing = false;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public string SoundFontDisplayName
    {
        get => _soundFontDisplayName;
        private set
        {
            if (_soundFontDisplayName != value)
            {
                _soundFontDisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    private void UpdateSoundFontDisplay()
    {
        if (_player?.SoundFontPath != null)
        {
            SoundFontDisplayName = Path.GetFileName(_player.SoundFontPath);
        }
        else
        {
            SoundFontDisplayName = "No SoundFont loaded";
        }
    }

    private async void OnBrowseSoundFontClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || !StorageProvider.CanOpen || _player is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a SoundFont",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("SoundFont") { Patterns = ["*.sf2", "*.sfz"] }]
        });

        if (files.Count > 0)
        {
            var path = files[0].TryGetLocalPath();
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    _player.LoadSoundFont(path);
                    _settings.SoundFontPath = path;
                    _settings.Save();
                    UpdateSoundFontDisplay();
                }
                catch (Exception ex)
                {
                    SoundFontDisplayName = "Error: " + ex.Message;
                }
            }
        }
    }

    private void OnSystemModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _player == null || SystemModeComboBox == null) return;

        var mode = SystemModeComboBox.SelectedIndex switch
        {
            1 => MidiSystem.GM1,
            2 => MidiSystem.GM2,
            3 => MidiSystem.XG,
            4 => MidiSystem.GS,
            _ => MidiSystem.Default
        };

        _player.SystemMode = mode;
        _settings.SystemMode = mode;
        _settings.Save();
    }

    private void OnSampleRateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || _player == null || SampleRateComboBox == null) return;

        var rate = SampleRateComboBox.SelectedIndex switch
        {
            1 => 48000,
            2 => 88200,
            3 => 96000,
            _ => 44100
        };

        _player.SampleRate = rate;
        _settings.SampleRate = rate;
        _settings.Save();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
