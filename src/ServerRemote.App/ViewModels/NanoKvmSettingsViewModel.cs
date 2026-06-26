using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.Models;
using ServerRemote.App.Services;

namespace ServerRemote.App.ViewModels;

/// <summary>
/// Manages the NanoKVM credentials (its own settings page). Stores them separately from the
/// server settings and leaves those fields untouched when saving.
/// </summary>
public sealed partial class NanoKvmSettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly NanoKvmApiClient _api;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _username = "admin";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public bool NotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(NotBusy));

    public NanoKvmSettingsViewModel(ISettingsService settings, NanoKvmApiClient api)
    {
        _settings = settings;
        _api = api;
    }

    public async Task LoadAsync()
    {
        await _settings.LoadAsync();
        var s = _settings.Current;
        Host = s.NanoKvmHost;
        Username = s.NanoKvmUsername;
        Password = s.NanoKvmPassword;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await PersistAsync();
        StatusMessage = "Saved.";
    }

    [RelayCommand]
    private async Task TestLoginAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Connecting …";
        try
        {
            // Save first so the client uses the current data.
            await PersistAsync();
            var ok = await _api.LoginAsync();
            StatusMessage = ok ? "Login successful." : "Login failed — check the credentials.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Writes the settings and passes the server/sensor fields through unchanged.
    private Task PersistAsync()
    {
        var current = _settings.Current;
        return _settings.SaveAsync(new AppSettings
        {
            Host = current.Host,
            Port = current.Port,
            ApiKey = current.ApiKey,
            CertFingerprint = current.CertFingerprint,
            TemperatureSensorLabel = current.TemperatureSensorLabel,
            PowerSensorLabel = current.PowerSensorLabel,
            NanoKvmHost = Host.Trim(),
            NanoKvmUsername = Username.Trim(),
            NanoKvmPassword = Password,
            NanoKvmKeyboardLayout = current.NanoKvmKeyboardLayout
        });
    }
}
