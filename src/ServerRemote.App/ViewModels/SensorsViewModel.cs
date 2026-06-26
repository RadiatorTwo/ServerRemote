using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServerRemote.App.Services;
using ServerRemote.Contracts;

namespace ServerRemote.App.ViewModels;

/// <summary>VM for the sensors page (Argus Monitor). Shows values or an empty state.</summary>
public sealed partial class SensorsViewModel : ObservableObject
{
    public SystemMonitor Monitor { get; }

    public SensorsViewModel(SystemMonitor monitor)
    {
        Monitor = monitor;
        Monitor.PropertyChanged += OnMonitorPropertyChanged;
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemMonitor.Argus))
        {
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(IsUnavailable));
            OnPropertyChanged(nameof(Temperatures));
            OnPropertyChanged(nameof(FanSpeeds));
            OnPropertyChanged(nameof(Loads));
            OnPropertyChanged(nameof(HasTemperatures));
            OnPropertyChanged(nameof(HasFanSpeeds));
            OnPropertyChanged(nameof(HasLoads));
        }
    }

    public bool IsAvailable => Monitor.Argus?.Available == true;
    public bool IsUnavailable => !IsAvailable;

    public IReadOnlyList<ArgusSensorDto> Temperatures => Monitor.Argus?.Temperatures ?? Array.Empty<ArgusSensorDto>();
    public IReadOnlyList<ArgusSensorDto> FanSpeeds => Monitor.Argus?.FanSpeeds ?? Array.Empty<ArgusSensorDto>();
    public IReadOnlyList<ArgusSensorDto> Loads => Monitor.Argus?.Loads ?? Array.Empty<ArgusSensorDto>();

    public bool HasTemperatures => Temperatures.Count > 0;
    public bool HasFanSpeeds => FanSpeeds.Count > 0;
    public bool HasLoads => Loads.Count > 0;
}
