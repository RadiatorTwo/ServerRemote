using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

public partial class NanoKvmPage : ContentPage
{
    private readonly NanoKvmViewModel _vm;

    public NanoKvmPage(NanoKvmViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        KeyboardEntry.TextChanged += OnKeyboardTextChanged;
    }

    // Sends each newly typed character live over the HID WebSocket and clears the field,
    // so the next character arrives as a delta again.
    private async void OnKeyboardTextChanged(object? sender, TextChangedEventArgs e)
    {
        var added = e.NewTextValue;
        if (string.IsNullOrEmpty(added))
            return;

        KeyboardEntry.TextChanged -= OnKeyboardTextChanged;
        KeyboardEntry.Text = "";
        KeyboardEntry.TextChanged += OnKeyboardTextChanged;

        foreach (var c in added)
            await _vm.SendCharAsync(c);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Monitor.EnsureStarted();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // When switching to fullscreen (same VM) do NOT tear down — otherwise the fullscreen
        // would be black and input disconnected. Consume the flag once.
        if (_vm.SuppressTeardownOnce)
        {
            _vm.SuppressTeardownOnce = false;
            return;
        }

        // Release sockets when the page is actually being left.
        if (_vm.StopStreamCommand.CanExecute(null))
            _vm.StopStreamCommand.Execute(null);
        if (_vm.DisconnectInputCommand.CanExecute(null))
            _vm.DisconnectInputCommand.Execute(null);
    }
}
