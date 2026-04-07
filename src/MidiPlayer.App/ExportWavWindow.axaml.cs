using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class ExportWavWindow : Window, INotifyPropertyChanged
{
    private static readonly ExportChoice<AudioExportFormat>[] AllFormatChoices =
    [
        new(AudioExportFormat.Wav, "WAV"),
        new(AudioExportFormat.Flac, "FLAC"),
        new(AudioExportFormat.Opus, "Opus")
    ];

    private static readonly ExportChoice<AudioExportFormat>[] WavOnlyFormatChoices =
    [
        new(AudioExportFormat.Wav, "WAV")
    ];

    private static readonly ExportChoice<int>[] SampleRateChoices =
    [
        new(44100, "44100 Hz"),
        new(48000, "48000 Hz"),
        new(88200, "88200 Hz"),
        new(96000, "96000 Hz")
    ];

    private static readonly ExportChoice<AudioBitDepth>[] WavBitDepthChoices =
    [
        new(AudioBitDepth.Pcm16, "16-bit PCM"),
        new(AudioBitDepth.Pcm24, "24-bit PCM"),
        new(AudioBitDepth.Float32, "32-bit Float")
    ];

    private static readonly ExportChoice<AudioBitDepth>[] FlacBitDepthChoices =
    [
        new(AudioBitDepth.Pcm16, "16-bit PCM"),
        new(AudioBitDepth.Pcm24, "24-bit PCM")
    ];

    private string _formatSummary = string.Empty;
    private string _playbackModifiersSummary = string.Empty;
    private string _outputPathDisplay;
    private string _opusBitrateDisplay = BassMidiPlayer.DefaultOpusBitrateKbps.ToString();
    private readonly ExportChoice<AudioExportFormat>[] _availableFormatChoices;

    public ExportWavWindow()
    {
        InitializeComponent();
        App.Current.SkinManager.ApplySkinToWindow(this);
        DataContext = this;
        _availableFormatChoices = BassMidiPlayer.SupportsCompressedAudioExport ? AllFormatChoices : WavOnlyFormatChoices;

        TrackDisplayName = "No Track Loaded";
        SoundFontDisplayName = "SoundFont · Not loaded";
        PlaybackModifiersSummary = FormatPlaybackModifiersSummary(
            BassMidiPlayer.DefaultPlaybackSpeedPercent,
            BassMidiPlayer.DefaultTransposeSemitones);
        _outputPathDisplay = Path.Combine(Directory.GetCurrentDirectory(), "export.wav");

        FormatComboBox.ItemsSource = _availableFormatChoices;
        SampleRateComboBox.ItemsSource = SampleRateChoices;
        FormatComboBox.SelectedItem = _availableFormatChoices[0];
        SampleRateComboBox.SelectedItem = SampleRateChoices[0];
        UpdateBitDepthChoices(AudioExportFormat.Wav, preserveSelection: false);
        FormatPanel.IsVisible = _availableFormatChoices.Length > 1;
        SampleRatePanel.SetValue(Grid.ColumnProperty, _availableFormatChoices.Length > 1 ? 1 : 0);
        SampleRatePanel.SetValue(Grid.ColumnSpanProperty, _availableFormatChoices.Length > 1 ? 1 : 2);
        UpdateFormatControls(rewriteOutputPath: false);
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
        SampleRateComboBox.SelectedItem = SampleRateChoices.FirstOrDefault(choice => choice.Value == defaultSampleRate) ?? SampleRateChoices[0];
        OutputPathDisplay = SuggestOutputPath(midiPath, SelectedFormat);

        OnPropertyChanged(nameof(TrackDisplayName));
        OnPropertyChanged(nameof(SoundFontDisplayName));
        OnPropertyChanged(nameof(PlaybackModifiersSummary));
        UpdateFormatControls(rewriteOutputPath: false);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public bool WasConfirmed { get; private set; }

    public AudioExportOptions? ExportOptions { get; private set; }

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

    public string OpusBitrateDisplay
    {
        get => _opusBitrateDisplay;
        set
        {
            if (_opusBitrateDisplay == value)
            {
                return;
            }

            _opusBitrateDisplay = value;
            OnPropertyChanged();
            UpdateFormatSummary();
        }
    }

    private AudioExportFormat SelectedFormat
        => (FormatComboBox.SelectedItem as ExportChoice<AudioExportFormat>)?.Value ?? AudioExportFormat.Wav;

    private int SelectedSampleRate
        => (SampleRateComboBox.SelectedItem as ExportChoice<int>)?.Value ?? 44100;

    private AudioBitDepth SelectedBitDepth
        => (BitDepthComboBox.SelectedItem as ExportChoice<AudioBitDepth>)?.Value ?? AudioBitDepth.Pcm24;

    private async void OnBrowseOutputClicked(object? sender, RoutedEventArgs e)
    {
        if (StorageProvider is null || !StorageProvider.CanSave)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

        var selectedFormat = SelectedFormat;
        var fileType = CreateFilePickerType(selectedFormat);
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {GetFormatLabel(selectedFormat)}",
            SuggestedFileName = Path.GetFileName(OutputPathDisplay),
            DefaultExtension = GetFileExtension(selectedFormat).TrimStart('.'),
            FileTypeChoices = [fileType]
        });

        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            OutputPathDisplay = path;
        }
    }

    private void OnFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFormatControls(rewriteOutputPath: true);
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

        var opusBitrateKbps = BassMidiPlayer.DefaultOpusBitrateKbps;

        if (SelectedFormat == AudioExportFormat.Opus && !TryGetOpusBitrate(out opusBitrateKbps))
        {
            FormatSummary = $"Enter an Opus bitrate between {BassMidiPlayer.MinOpusBitrateKbps} and {BassMidiPlayer.MaxOpusBitrateKbps} kbps.";
            return;
        }

        ExportOptions = new AudioExportOptions(
            OutputPathDisplay,
            GetEffectiveSampleRate(),
            SelectedFormat,
            SelectedBitDepth,
            opusBitrateKbps);
        WasConfirmed = true;
        Close();
    }

    private void UpdateFormatSummary()
    {
        FormatSummary = SelectedFormat switch
        {
            AudioExportFormat.Wav => $"WAV · {GetEffectiveSampleRate() / 1000d:0.###} kHz / {GetBitDepthLabel(SelectedBitDepth)}",
            AudioExportFormat.Flac => $"FLAC · {GetEffectiveSampleRate() / 1000d:0.###} kHz / {GetBitDepthLabel(SelectedBitDepth)}",
            AudioExportFormat.Opus when TryGetOpusBitrate(out var bitrate)
                => $"Opus · {BassMidiPlayer.OpusExportSampleRate / 1000d:0.###} kHz / {bitrate} kbps",
            _ => $"Opus · {BassMidiPlayer.OpusExportSampleRate / 1000d:0.###} kHz"
        };
    }

    private void UpdateFormatControls(bool rewriteOutputPath)
    {
        var selectedFormat = SelectedFormat;
        UpdateBitDepthChoices(selectedFormat, preserveSelection: true);
        BitDepthPanel.IsVisible = selectedFormat != AudioExportFormat.Opus;
        OpusBitratePanel.IsVisible = selectedFormat == AudioExportFormat.Opus;
        SampleRateComboBox.IsEnabled = selectedFormat != AudioExportFormat.Opus;

        if (selectedFormat == AudioExportFormat.Opus)
        {
            SampleRateComboBox.SelectedItem = SampleRateChoices.First(choice => choice.Value == BassMidiPlayer.OpusExportSampleRate);
        }

        if (rewriteOutputPath)
        {
            OutputPathDisplay = ReplaceKnownExportExtension(OutputPathDisplay, GetFileExtension(selectedFormat));
        }

        UpdateFormatSummary();
    }

    private void UpdateBitDepthChoices(AudioExportFormat format, bool preserveSelection)
    {
        var currentSelection = preserveSelection ? (BitDepthComboBox.SelectedItem as ExportChoice<AudioBitDepth>)?.Value : null;
        var choices = format == AudioExportFormat.Flac ? FlacBitDepthChoices : WavBitDepthChoices;
        BitDepthComboBox.ItemsSource = choices;

        var selectedChoice = currentSelection is AudioBitDepth bitDepth
            ? choices.FirstOrDefault(choice => choice.Value == bitDepth)
            : null;

        BitDepthComboBox.SelectedItem = selectedChoice ?? choices[Math.Min(1, choices.Length - 1)];
    }

    private bool TryGetOpusBitrate(out int bitrateKbps)
    {
        if (!int.TryParse(OpusBitrateDisplay, out bitrateKbps))
        {
            bitrateKbps = BassMidiPlayer.DefaultOpusBitrateKbps;
            return false;
        }

        if (bitrateKbps < BassMidiPlayer.MinOpusBitrateKbps || bitrateKbps > BassMidiPlayer.MaxOpusBitrateKbps)
        {
            return false;
        }

        return true;
    }

    private int GetEffectiveSampleRate()
        => SelectedFormat == AudioExportFormat.Opus
            ? BassMidiPlayer.OpusExportSampleRate
            : SelectedSampleRate;

    private static string GetBitDepthLabel(AudioBitDepth bitDepth)
        => bitDepth switch
        {
            AudioBitDepth.Pcm16 => "16-bit PCM",
            AudioBitDepth.Pcm24 => "24-bit PCM",
            AudioBitDepth.Float32 => "32-bit Float",
            _ => "24-bit PCM"
        };

    private static string FormatPlaybackModifiersSummary(int playbackSpeedPercent, int transposeSemitones)
    {
        var transposeText = transposeSemitones > 0
            ? $"+{transposeSemitones}"
            : transposeSemitones.ToString();
        return $"Playback · {playbackSpeedPercent}% / {transposeText} st";
    }

    private static string SuggestOutputPath(string midiPath, AudioExportFormat format)
    {
        var directory = Path.GetDirectoryName(midiPath);
        var fileName = Path.GetFileNameWithoutExtension(midiPath);
        var baseDirectory = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        var extension = GetFileExtension(format);
        var candidate = Path.Combine(baseDirectory, fileName + extension);

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 1;
        while (true)
        {
            candidate = Path.Combine(baseDirectory, $"{fileName} ({index}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static string ReplaceKnownExportExtension(string path, string targetExtension)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.Combine(Directory.GetCurrentDirectory(), "export" + targetExtension);
        }

        var currentExtension = Path.GetExtension(path);
        if (!string.Equals(currentExtension, ".wav", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentExtension, ".flac", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentExtension, ".opus", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return Path.ChangeExtension(path, targetExtension);
    }

    private static string GetFileExtension(AudioExportFormat format)
        => format switch
        {
            AudioExportFormat.Flac => ".flac",
            AudioExportFormat.Opus => ".opus",
            _ => ".wav"
        };

    private static string GetFormatLabel(AudioExportFormat format)
        => format switch
        {
            AudioExportFormat.Flac => "FLAC",
            AudioExportFormat.Opus => "Opus",
            _ => "WAV"
        };

    private static FilePickerFileType CreateFilePickerType(AudioExportFormat format)
        => format switch
        {
            AudioExportFormat.Flac => new FilePickerFileType("FLAC Audio")
            {
                Patterns = ["*.flac"],
                MimeTypes = ["audio/flac"]
            },
            AudioExportFormat.Opus => new FilePickerFileType("Opus Audio")
            {
                Patterns = ["*.opus"],
                MimeTypes = ["audio/opus"]
            },
            _ => new FilePickerFileType("WAV Audio")
            {
                Patterns = ["*.wav"],
                MimeTypes = ["audio/wav"]
            }
        };

    private sealed record ExportChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
