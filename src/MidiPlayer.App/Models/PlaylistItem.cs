using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MidiPlayer.App.Models;

public class PlaylistItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private double _durationSeconds;
    private bool _isPlaying;
    private bool _isDurationParsed;
    private bool _isFailed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FilePath
    {
        get => _filePath;
        init => SetField(ref _filePath, value);
    }

    public string FileName
    {
        get => _fileName;
        init => SetField(ref _fileName, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set => SetField(ref _durationSeconds, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    public bool IsDurationParsed
    {
        get => _isDurationParsed;
        set => SetField(ref _isDurationParsed, value);
    }
    
    public bool IsFailed
    {
        get => _isFailed;
        set => SetField(ref _isFailed, value);
    }
    
    public string DurationText
    {
        get
        {
            if (IsFailed) return "Error";
            if (!IsDurationParsed) return "--:--";
            return FormatTime(DurationSeconds);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(DurationSeconds) || propertyName == nameof(IsDurationParsed) || propertyName == nameof(IsFailed))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DurationText)));
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
    
    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "00:00";
        var value = System.TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }
}
