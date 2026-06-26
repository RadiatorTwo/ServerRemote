using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.Models;
using ServerRemote.App.Services;

namespace ServerRemote.App.ViewModels;

/// <summary>
/// Overview VM for the dashboard. Holds no data of its own; instead it derives
/// summaries from the shared <see cref="SystemMonitor"/>.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    public SystemMonitor Monitor { get; }

    public System.Collections.ObjectModel.ObservableCollection<DriveItemViewModel> CriticalDrives { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ServiceItemViewModel> CriticalServices { get; } = new();

    public DashboardViewModel(SystemMonitor monitor)
    {
        Monitor = monitor;
        Monitor.PropertyChanged += OnMonitorPropertyChanged;
        Monitor.Drives.CollectionChanged += OnDrivesCollectionChanged;
        Monitor.Services.CollectionChanged += OnServicesCollectionChanged;
    }

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SystemMonitor.CpuPercent):
                OnPropertyChanged(nameof(CpuStatus));
                break;
            case nameof(SystemMonitor.RamUsedPercent):
                OnPropertyChanged(nameof(RamStatus));
                break;
            case nameof(SystemMonitor.RamUsedMb):
            case nameof(SystemMonitor.RamTotalMb):
                OnPropertyChanged(nameof(RamCaption));
                break;
            case nameof(SystemMonitor.Argus):
                OnPropertyChanged(nameof(ArgusUnavailable));
                break;
        }
    }

    public MetricStatus CpuStatus => MetricStatusExtensions.FromPercent(Monitor.CpuPercent);
    public MetricStatus RamStatus => MetricStatusExtensions.FromPercent(Monitor.RamUsedPercent);

    public string RamCaption => $"{Monitor.RamUsedMb / 1024.0:0.0} / {Monitor.RamTotalMb / 1024.0:0.0} GB";

    public bool HasCriticalDrives => CriticalDrives.Count > 0;
    public bool HasCriticalServices => CriticalServices.Count > 0;

    public string StorageSummary => CriticalDrives.Count switch
    {
        0 => "All drives in the green zone",
        1 => "1 drive critical",
        var n => $"{n} drives critical"
    };

    public string ServicesSummary => CriticalServices.Count switch
    {
        0 => "All monitored services are running",
        1 => "1 service needs attention",
        var n => $"{n} services need attention"
    };

    public bool ArgusUnavailable => Monitor.Argus is null || Monitor.Argus.Available == false;

    private void OnDrivesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // React to usage changes of individual drives so the critical list stays correct.
        if (e.NewItems is not null)
            foreach (DriveItemViewModel d in e.NewItems)
                d.PropertyChanged += (_, _) => RecomputeDrives();
        RecomputeDrives();
    }

    private void OnServicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // React to state changes of individual services to keep the critical list up to date.
        if (e.NewItems is not null)
            foreach (ServiceItemViewModel s in e.NewItems)
                s.PropertyChanged += (_, _) => RecomputeServices();
        RecomputeServices();
    }

    // Synchronizes a subset in-place (stable instances) instead of rebuilding it.
    private static void SyncSubset<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> desired)
    {
        var wanted = desired.ToList();
        for (int i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i]))
                target.RemoveAt(i);
        foreach (var item in wanted)
            if (!target.Contains(item))
                target.Add(item);
    }

    private void RecomputeDrives()
    {
        SyncSubset(CriticalDrives, Monitor.Drives.Where(d => d.Status != DriveStatus.Normal));
        OnPropertyChanged(nameof(HasCriticalDrives));
        OnPropertyChanged(nameof(StorageSummary));
    }

    private void RecomputeServices()
    {
        SyncSubset(CriticalServices, Monitor.Services.Where(s => s.CanControl && !s.IsRunning));
        OnPropertyChanged(nameof(HasCriticalServices));
        OnPropertyChanged(nameof(ServicesSummary));
        OnPropertyChanged(nameof(ArgusUnavailable));
    }

    [RelayCommand]
    private static Task GoToServices() => Shell.Current.GoToAsync("//services");

    [RelayCommand]
    private static Task GoToStorage() => Shell.Current.GoToAsync("//storage");
}
