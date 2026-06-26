using ServerRemote.App.Services;

namespace ServerRemote.App.Components;

public partial class StatusHeader : ContentView
{
    public static readonly BindableProperty MonitorProperty =
        BindableProperty.Create(nameof(Monitor), typeof(SystemMonitor), typeof(StatusHeader));

    public StatusHeader() => InitializeComponent();

    public SystemMonitor? Monitor
    {
        get => (SystemMonitor?)GetValue(MonitorProperty);
        set => SetValue(MonitorProperty, value);
    }
}
