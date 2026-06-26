namespace ServerRemote.App;

public partial class MobileShell : Shell
{
    public MobileShell() => InitializeComponent();

    private async void OnNanoKvmSettingsClicked(object? sender, EventArgs e)
    {
        // A MenuItem does not close the flyout by itself (unlike a FlyoutItem).
        FlyoutIsPresented = false;
        await GoToAsync("nanokvm-settings");
    }
}
