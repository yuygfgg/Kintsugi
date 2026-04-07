using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MidiPlayer.App.Services;

namespace MidiPlayer.App;

public partial class BuiltinEqWindow : Window, INotifyPropertyChanged
{
    private BassMidiPlayer? _player;
    private bool _isEqEnabled;

    public BuiltinEqWindow()
    {
        InitializeComponent();
        App.Current.SkinManager.ApplySkinToWindow(this);
        DataContext = this;
    }

    public BuiltinEqWindow(BassMidiPlayer player) : this()
    {
        ArgumentNullException.ThrowIfNull(player);

        _player = player;
        _isEqEnabled = player.IsEqEnabled;

        _player.EqStateChanged += OnPlayerEqStateChanged;
        Closed += OnWindowClosed;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public BassMidiPlayer? Player => _player;

    public bool IsEqEnabled
    {
        get => _isEqEnabled;
        set
        {
            if (!SetField(ref _isEqEnabled, value))
            {
                return;
            }

            if (_player is not null)
            {
                _player.IsEqEnabled = value;
            }

            OnPropertyChanged(nameof(EqStatusText));
            OnPropertyChanged(nameof(ToggleEqToolTip));
        }
    }

    public string EqStatusText => IsEqEnabled ? "ON" : "OFF";

    public string ToggleEqToolTip => IsEqEnabled ? "Turn EQ off" : "Turn EQ on";

    private void OnToggleEqClicked(object? sender, RoutedEventArgs e)
    {
        IsEqEnabled = !IsEqEnabled;
        e.Handled = true;
    }

    private void OnPlayerEqStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshEqBindings);
    }

    private void RefreshEqBindings()
    {
        if (_player is null)
        {
            return;
        }

        if (_isEqEnabled != _player.IsEqEnabled)
        {
            _isEqEnabled = _player.IsEqEnabled;
            OnPropertyChanged(nameof(IsEqEnabled));
        }

        OnPropertyChanged(nameof(EqStatusText));
        OnPropertyChanged(nameof(ToggleEqToolTip));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        Closed -= OnWindowClosed;
        if (_player is not null)
        {
            _player.EqStateChanged -= OnPlayerEqStateChanged;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
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
}
