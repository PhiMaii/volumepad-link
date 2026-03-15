using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VolumePadLink.UI.Models;

public sealed class AudioSessionItem : INotifyPropertyChanged
{
    private string _display;
    private double _volumePercent;
    private bool _muted;

    public AudioSessionItem(string sessionId, string display, double volumePercent, bool muted)
    {
        SessionId = sessionId;
        _display = display;
        _volumePercent = volumePercent;
        _muted = muted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SessionId { get; }

    public string Display
    {
        get => _display;
        set
        {
            if (_display == value)
            {
                return;
            }

            _display = value;
            OnPropertyChanged();
        }
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (Math.Abs(_volumePercent - value) < 0.001)
            {
                return;
            }

            _volumePercent = value;
            OnPropertyChanged();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (_muted == value)
            {
                return;
            }

            _muted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MuteButtonText));
        }
    }

    public string MuteButtonText => Muted ? "Unmute" : "Mute";

    public override string ToString()
    {
        return $"{Display} ({Math.Round(VolumePercent)}%{(Muted ? ", muted" : string.Empty)})";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
