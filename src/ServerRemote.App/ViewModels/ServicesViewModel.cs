using CommunityToolkit.Mvvm.ComponentModel;
using ServerRemote.App.Services;

namespace ServerRemote.App.ViewModels;

/// <summary>VM for the services page. Passes through the shared service list.</summary>
public sealed partial class ServicesViewModel : ObservableObject
{
    public SystemMonitor Monitor { get; }

    public ServicesViewModel(SystemMonitor monitor)
    {
        Monitor = monitor;
        Monitor.Services.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasServices));
            OnPropertyChanged(nameof(IsEmpty));
        };
    }

    public bool HasServices => Monitor.Services.Count > 0;
    public bool IsEmpty => Monitor.Services.Count == 0;
}
