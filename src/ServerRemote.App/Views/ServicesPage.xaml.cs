using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

public partial class ServicesPage : ContentPage
{
    private readonly ServicesViewModel _vm;

    public ServicesPage(ServicesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Monitor.EnsureStarted();
    }
}
