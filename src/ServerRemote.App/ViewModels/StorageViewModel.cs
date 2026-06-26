using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ServerRemote.App.Services;

namespace ServerRemote.App.ViewModels;

/// <summary>VM for the storage page. Binds directly to the stable, shared drive list.</summary>
public sealed partial class StorageViewModel : ObservableObject
{
    public SystemMonitor Monitor { get; }

    public ObservableCollection<DriveItemViewModel> Drives => Monitor.Drives;

    public StorageViewModel(SystemMonitor monitor)
    {
        Monitor = monitor;
        Monitor.Drives.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDrives));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public bool HasDrives => Drives.Count > 0;
    public bool IsEmpty => Drives.Count == 0;
}
