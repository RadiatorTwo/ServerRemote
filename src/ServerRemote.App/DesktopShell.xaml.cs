namespace ServerRemote.App;

public partial class DesktopShell : Shell
{
    public DesktopShell() => InitializeComponent();

    private async void OnNanoKvmSettingsClicked(object? sender, EventArgs e)
    {
        FlyoutIsPresented = false;
        await GoToAsync("nanokvm-settings");
    }
}
