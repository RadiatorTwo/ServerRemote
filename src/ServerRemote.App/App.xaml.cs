using ServerRemote.App.Services;
using ServerRemote.App.Views;

namespace ServerRemote.App;

public partial class App : Application
{
    private readonly NanoKvmMonitor _nanoKvm;

    public App(NanoKvmMonitor nanoKvm)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Dark;

        _nanoKvm = nanoKvm;

        // Detail routes that do not appear in the flyout but are reachable via GoToAsync.
        Routing.RegisterRoute("nanokvm-settings", typeof(NanoKvmSettingsPage));
        Routing.RegisterRoute("nanokvm-fullscreen", typeof(NanoKvmFullscreenPage));
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var isDesktop = DeviceInfo.Idiom == DeviceIdiom.Desktop
                        || DeviceInfo.Platform == DevicePlatform.WinUI;

        Shell shell = isDesktop ? new DesktopShell() : new MobileShell();
        var window = new Window(shell);

        // Establish the NanoKVM connection at app startup, not only when the NanoKVM page
        // is opened. The monitor is idempotent when polling and checks for itself whether
        // a host is configured ("Not configured" instead of a connection attempt).
        _nanoKvm.EnsureStarted();

        return window;
    }
}
