using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public sealed class ChannelMixStrip : INotifyPropertyChanged
{
    private readonly BassMidiPlayer _player;
    private readonly int _channel;

    public ChannelMixStrip(BassMidiPlayer player, int channel)
    {
        _player = player;
        _channel = channel;
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
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeText));
        }
    }

    public string VolumeText => FormatPercent(VolumePercent);

    public void ResetVolume()
        => VolumePercent = BassMidiPlayer.DefaultMixPercent;

    public double ReverbSendPercent
    {
        get => _player.GetChannelReverbSendPercent(_channel);
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.GetChannelReverbSendPercent(_channel) == intValue)
            {
                return;
            }

            _player.SetChannelReverbSendPercent(_channel, intValue);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReverbSendText));
        }
    }

    public string ReverbSendText => FormatPercent(ReverbSendPercent);

    public void ResetReverbSend()
        => ReverbSendPercent = BassMidiPlayer.DefaultMixPercent;

    public double ChorusSendPercent
    {
        get => _player.GetChannelChorusSendPercent(_channel);
        set
        {
            var intValue = (int)Math.Round(value);
            if (_player.GetChannelChorusSendPercent(_channel) == intValue)
            {
                return;
            }

            _player.SetChannelChorusSendPercent(_channel, intValue);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChorusSendText));
        }
    }

    public string ChorusSendText => FormatPercent(ChorusSendPercent);

    public void ResetChorusSend()
        => ChorusSendPercent = BassMidiPlayer.DefaultMixPercent;

    private static string FormatPercent(double percent)
        => $"{Math.Round(percent):0}%";

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
