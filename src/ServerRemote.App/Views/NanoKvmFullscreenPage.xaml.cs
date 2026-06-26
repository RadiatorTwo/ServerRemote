using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

/// <summary>
/// Fullscreen view of the NanoKVM live image. Shares the (singleton) ViewModel with
/// <see cref="NanoKvmPage"/> so that the stream and HID input keep running seamlessly. Image +
/// input surface + semi-transparent on-screen keyboard overlay + overlay control bar, without shell chrome.
/// Keyboard input goes through the dedicated <see cref="Components.OnScreenKeyboard"/> (instead of the
/// Android soft keyboard) so that the image stays visible.
/// </summary>
public partial class NanoKvmFullscreenPage : ContentPage
{
    private readonly NanoKvmViewModel _vm;

    public NanoKvmFullscreenPage(NanoKvmViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Show/hide the overlay keyboard.
    private void OnKeyboardToggle(object? sender, EventArgs e)
        => _vm.KeyboardVisible = !_vm.KeyboardVisible;

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.KeyboardVisible = false;
    }
}
