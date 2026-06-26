using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

public partial class SensorsPage : ContentPage
{
    private readonly SensorsViewModel _vm;

    public SensorsPage(SensorsViewModel vm)
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
