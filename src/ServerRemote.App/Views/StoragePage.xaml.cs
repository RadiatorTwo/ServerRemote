using ServerRemote.App.ViewModels;

namespace ServerRemote.App.Views;

public partial class StoragePage : ContentPage
{
    private readonly StorageViewModel _vm;

    public StoragePage(StorageViewModel vm)
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
