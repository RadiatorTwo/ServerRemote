using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

public partial class NanoKvmSettingsPage : ContentPage
{
    private readonly NanoKvmSettingsViewModel _vm;

    public NanoKvmSettingsPage(NanoKvmSettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
