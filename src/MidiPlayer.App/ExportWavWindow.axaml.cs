using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class ExportWavWindow : Window, INotifyPropertyChanged
{
    private string _formatSummary = string.Empty;
    private string _playbackModifiersSummary = string.Empty;
    private string _outputPathDisplay;

    public ExportWavWindow()
    {
        InitializeComponent();
        DataContext = this;

        TrackDisplayName = "No Track Loaded";
        SoundFontDisplayName = "SoundFont · Not loaded";
        PlaybackModifiersSummary = FormatPlaybackModifiersSummary(
            BassMidiPlayer.DefaultPlaybackSpeedPercent,
            BassMidiPlayer.DefaultTransposeSemitones);
        _outputPathDisplay = Path.Combine(Directory.GetCurrentDirectory(), "export.wav");
        SampleRateComboBox.SelectedIndex = 0;
        BitDepthComboBox.SelectedIndex = 1;
        UpdateFormatSummary();
    }

    public ExportWavWindow(
        string midiPath,
        string? soundFontPath,
        int defaultSampleRate,
        int playbackSpeedPercent,
        int transposeSemitones) : this()
    {
        TrackDisplayName = Path.GetFileNameWithoutExtension(midiPath);
        SoundFontDisplayName = string.IsNullOrWhiteSpace(soundFontPath)
            ? "SoundFont · Not loaded"
            : $"SoundFont · {Path.GetFileName(soundFontPath)}";
        PlaybackModifiersSummary = FormatPlaybackModifiersSummary(playbackSpeedPercent, transposeSemitones);
        OutputPathDisplay = SuggestOutputPath(midiPath);

        SampleRateComboBox.SelectedIndex = defaultSampleRate switch
        {
            48000 => 1,
            88200 => 2,
            96000 => 3,
            _ => 0
        };

        OnPropertyChanged(nameof(TrackDisplayName));
        OnPropertyChanged(nameof(SoundFontDisplayName));
        OnPropertyChanged(nameof(PlaybackModifiersSummary));
        UpdateFormatSummary();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public bool WasConfirmed { get; private set; }

    public WavExportOptions? ExportOptions { get; private set; }

    public string TrackDisplayName { get; private set; }

    public string SoundFontDisplayName { get; private set; }

    public string OutputPathDisplay
    {
        get => _outputPathDisplay;
        private set
        {
            if (_outputPathDisplay == value)
            {
                return;
            }

            _outputPathDisplay = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExportReadyText));
        }
    }

    public string FormatSummary
    {
        get => _formatSummary;
        private set
        {
            if (_formatSummary == value)
            {
                return;
            }

            _formatSummary = value;
            OnPropertyChanged();
        }
    }

    public string PlaybackModifiersSummary
    {
        get => _playbackModifiersSummary;
        private set
        {
            if (_playbackModifiersSummary == value)
            {
                return;
            }

            _playbackModifiersSummary = value;
            OnPropertyChanged();
        }
    }

    public string ExportReadyText => $"Output · {Path.GetFileName(OutputPathDisplay)}";

    private int SelectedSampleRate => SampleRateComboBox.SelectedIndex switch
    {
        1 => 48000,
        2 => 88200,
        3 => 96000,
        _ => 44100
    };

    private WavBitDepth SelectedBitDepth => BitDepthComboBox.SelectedIndex switch
    {
        0 => WavBitDepth.Pcm16,
        2 => WavBitDepth.Float32,
        _ => WavBitDepth.Pcm24
    };

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || !StorageProvider.CanSave)
        {
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export WAV",
            SuggestedFileName = Path.GetFileName(OutputPathDisplay),
            DefaultExtension = "wav",
            FileTypeChoices =
            [
                new FilePickerFileType("WAV Audio")
                {
                    Patterns = ["*.wav"],
                    MimeTypes = ["audio/wav"]
                }
            ]
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputPathDisplay = path;
        }
    }

    private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFormatSummary();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OutputPathDisplay))
        {
            return;
        }

        ExportOptions = new WavExportOptions(OutputPathDisplay, SelectedSampleRate, SelectedBitDepth);
        WasConfirmed = true;
        Close();
    }

    private void UpdateFormatSummary()
    {
        FormatSummary = $"{SelectedSampleRate / 1000d:0.###} kHz / {GetBitDepthLabel(SelectedBitDepth)}";
    }

    private static string GetBitDepthLabel(WavBitDepth bitDepth)
        => bitDepth switch
        {
            WavBitDepth.Pcm16 => "16-bit PCM",
            WavBitDepth.Pcm24 => "24-bit PCM",
            WavBitDepth.Float32 => "32-bit Float",
            _ => "24-bit PCM"
        };

    private static string FormatPlaybackModifiersSummary(int playbackSpeedPercent, int transposeSemitones)
    {
        var transposeText = transposeSemitones > 0
            ? $"+{transposeSemitones}"
            : transposeSemitones.ToString();
        return $"Playback · {playbackSpeedPercent}% / {transposeText} st";
    }

    private static string SuggestOutputPath(string midiPath)
    {
        var directory = Path.GetDirectoryName(midiPath);
        var fileName = Path.GetFileNameWithoutExtension(midiPath);
        var baseDirectory = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        var candidate = Path.Combine(baseDirectory, fileName + ".wav");

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 1;
        while (true)
        {
            candidate = Path.Combine(baseDirectory, $"{fileName} ({index}).wav");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
