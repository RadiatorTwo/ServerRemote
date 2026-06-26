using ServerRemote.App.Services;

namespace ServerRemote.App.Components;

public partial class DangerZone : ContentView
{
    public static readonly BindableProperty MonitorProperty =
        BindableProperty.Create(nameof(Monitor), typeof(SystemMonitor), typeof(DangerZone));

    public DangerZone() => InitializeComponent();

    public SystemMonitor? Monitor
    {
        get => (SystemMonitor?)GetValue(MonitorProperty);
        set => SetValue(MonitorProperty, value);
    }
}
