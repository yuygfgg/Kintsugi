using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public sealed class ChannelMixStrip : INotifyPropertyChanged
{
    private readonly BassMidiPlayer _player;
    private readonly int _channel;
    private readonly Action? _onChanged;

    public ChannelMixStrip(BassMidiPlayer player, int channel, Action? onChanged = null)
    {
        _player = player;
        _channel = channel;
        _onChanged = onChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ChannelLabel => $"CH {_channel + 1:00}";

    public double VolumePercent
    {
        get => _player.GetChannelVolumePercent(_channel);
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.GetChannelVolumePercent(_channel) == intValue)
            {
                return;
            }

            _player.SetChannelVolumePercent(_channel, intValue);
            RefreshVolume();
            NotifyChanged();
        }
    }

    public string VolumeText => FormatPercent(VolumePercent);

    public void ResetVolume()
        => VolumePercent = BassMidiPlayer.DefaultMixPercent;

    public double ReverbSendValue
    {
        get => GetReverbSendValue();
        set
        {
            var intValue = (int)Math.Round(value);
            if (GetReverbSendValue() == intValue)
            {
                return;
            }

            if (ReverbSendMode == ChannelSendMode.Absolute)
            {
                _player.SetChannelReverbSendAbsoluteValue(_channel, intValue);
            }
            else if (ReverbSendMode == ChannelSendMode.Bias)
            {
                _player.SetChannelReverbSendBiasValue(_channel, intValue);
            }
            else
            {
                _player.SetChannelReverbSendPercent(_channel, intValue);
            }

            RefreshReverb();
            NotifyChanged();
        }
    }

    public double ReverbSendMinimum => ReverbSendMode == ChannelSendMode.Bias
        ? -BassMidiPlayer.MaxMidiControllerValue
        : 0;

    public double ReverbSendMaximum => ReverbSendMode == ChannelSendMode.Absolute
        ? BassMidiPlayer.MaxMidiControllerValue
        : ReverbSendMode == ChannelSendMode.Bias
            ? BassMidiPlayer.MaxMidiControllerValue
        : BassMidiPlayer.MaxMixPercent;

    public string ReverbSendText => ReverbSendMode switch
    {
        ChannelSendMode.Absolute => $"{GetReverbSendValue():0}",
        ChannelSendMode.Bias => FormatSignedValue(GetReverbSendValue()),
        _ => FormatPercent(GetReverbSendValue())
    };

    public string ReverbSendModeLabel => ReverbSendMode switch
    {
        ChannelSendMode.Absolute => "ABS",
        ChannelSendMode.Bias => "BIA",
        _ => "SCL"
    };

    public string ReverbSendModeToolTip => ReverbSendMode switch
    {
        ChannelSendMode.Absolute => "Absolute send value 0-127",
        ChannelSendMode.Bias => "Add or subtract from the MIDI file's send value",
        _ => "Scale the MIDI file's send value"
    };

    public ChannelSendMode ReverbSendMode => _player.GetChannelReverbSendMode(_channel);

    public void ToggleReverbSendMode()
    {
        _player.SetChannelReverbSendMode(_channel, GetNextMode(ReverbSendMode));
        RefreshReverb();
        NotifyChanged();
    }

    public void ResetReverbSend()
    {
        if (ReverbSendMode == ChannelSendMode.Absolute)
        {
            _player.SetChannelReverbSendAbsoluteValue(_channel, BassMidiPlayer.DefaultChannelReverb);
        }
        else if (ReverbSendMode == ChannelSendMode.Bias)
        {
            _player.SetChannelReverbSendBiasValue(_channel, MidiMixSettings.DefaultBiasValue);
        }
        else
        {
            _player.SetChannelReverbSendPercent(_channel, BassMidiPlayer.DefaultMixPercent);
        }

        RefreshReverb();
        NotifyChanged();
    }

    public double ChorusSendValue
    {
        get => GetChorusSendValue();
        set
        {
            var intValue = (int)Math.Round(value);
            if (GetChorusSendValue() == intValue)
            {
                return;
            }

            if (ChorusSendMode == ChannelSendMode.Absolute)
            {
                _player.SetChannelChorusSendAbsoluteValue(_channel, intValue);
            }
            else if (ChorusSendMode == ChannelSendMode.Bias)
            {
                _player.SetChannelChorusSendBiasValue(_channel, intValue);
            }
            else
            {
                _player.SetChannelChorusSendPercent(_channel, intValue);
            }

            RefreshChorus();
            NotifyChanged();
        }
    }

    public double ChorusSendMinimum => ChorusSendMode == ChannelSendMode.Bias
        ? -BassMidiPlayer.MaxMidiControllerValue
        : 0;

    public double ChorusSendMaximum => ChorusSendMode == ChannelSendMode.Absolute
        ? BassMidiPlayer.MaxMidiControllerValue
        : ChorusSendMode == ChannelSendMode.Bias
            ? BassMidiPlayer.MaxMidiControllerValue
        : BassMidiPlayer.MaxMixPercent;

    public string ChorusSendText => ChorusSendMode switch
    {
        ChannelSendMode.Absolute => $"{GetChorusSendValue():0}",
        ChannelSendMode.Bias => FormatSignedValue(GetChorusSendValue()),
        _ => FormatPercent(GetChorusSendValue())
    };

    public string ChorusSendModeLabel => ChorusSendMode switch
    {
        ChannelSendMode.Absolute => "ABS",
        ChannelSendMode.Bias => "BIA",
        _ => "SCL"
    };

    public string ChorusSendModeToolTip => ChorusSendMode switch
    {
        ChannelSendMode.Absolute => "Absolute send value 0-127",
        ChannelSendMode.Bias => "Add or subtract from the MIDI file's send value",
        _ => "Scale the MIDI file's send value"
    };

    public ChannelSendMode ChorusSendMode => _player.GetChannelChorusSendMode(_channel);

    public void ToggleChorusSendMode()
    {
        _player.SetChannelChorusSendMode(_channel, GetNextMode(ChorusSendMode));
        RefreshChorus();
        NotifyChanged();
    }

    public void ResetChorusSend()
    {
        if (ChorusSendMode == ChannelSendMode.Absolute)
        {
            _player.SetChannelChorusSendAbsoluteValue(_channel, BassMidiPlayer.DefaultChannelChorus);
        }
        else if (ChorusSendMode == ChannelSendMode.Bias)
        {
            _player.SetChannelChorusSendBiasValue(_channel, MidiMixSettings.DefaultBiasValue);
        }
        else
        {
            _player.SetChannelChorusSendPercent(_channel, BassMidiPlayer.DefaultMixPercent);
        }

        RefreshChorus();
        NotifyChanged();
    }

    public void Refresh()
    {
        RefreshVolume();
        RefreshReverb();
        RefreshChorus();
        OnPropertyChanged(nameof(ChannelLabel));
    }

    private int GetReverbSendValue()
        => ReverbSendMode switch
        {
            ChannelSendMode.Absolute => _player.GetChannelReverbSendAbsoluteValue(_channel),
            ChannelSendMode.Bias => _player.GetChannelReverbSendBiasValue(_channel),
            _ => _player.GetChannelReverbSendPercent(_channel)
        };

    private int GetChorusSendValue()
        => ChorusSendMode switch
        {
            ChannelSendMode.Absolute => _player.GetChannelChorusSendAbsoluteValue(_channel),
            ChannelSendMode.Bias => _player.GetChannelChorusSendBiasValue(_channel),
            _ => _player.GetChannelChorusSendPercent(_channel)
        };

    private void RefreshVolume()
    {
        OnPropertyChanged(nameof(VolumePercent));
        OnPropertyChanged(nameof(VolumeText));
    }

    private void RefreshReverb()
    {
        OnPropertyChanged(nameof(ReverbSendMinimum));
        OnPropertyChanged(nameof(ReverbSendValue));
        OnPropertyChanged(nameof(ReverbSendMaximum));
        OnPropertyChanged(nameof(ReverbSendText));
        OnPropertyChanged(nameof(ReverbSendMode));
        OnPropertyChanged(nameof(ReverbSendModeLabel));
        OnPropertyChanged(nameof(ReverbSendModeToolTip));
    }

    private void RefreshChorus()
    {
        OnPropertyChanged(nameof(ChorusSendMinimum));
        OnPropertyChanged(nameof(ChorusSendValue));
        OnPropertyChanged(nameof(ChorusSendMaximum));
        OnPropertyChanged(nameof(ChorusSendText));
        OnPropertyChanged(nameof(ChorusSendMode));
        OnPropertyChanged(nameof(ChorusSendModeLabel));
        OnPropertyChanged(nameof(ChorusSendModeToolTip));
    }

    private void NotifyChanged()
        => _onChanged?.Invoke();

    private static string FormatPercent(double percent)
        => $"{Math.Round(percent):0}%";

    private static string FormatSignedValue(double value)
    {
        var rounded = (int)Math.Round(value);
        return rounded > 0 ? $"+{rounded}" : rounded.ToString();
    }

    private static ChannelSendMode GetNextMode(ChannelSendMode mode)
        => mode switch
        {
            ChannelSendMode.Scale => ChannelSendMode.Absolute,
            ChannelSendMode.Absolute => ChannelSendMode.Bias,
            _ => ChannelSendMode.Scale
        };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
