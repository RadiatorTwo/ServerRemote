using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.Models;

namespace ServerRemote.App.Services;

/// <summary>
/// Central data layer for the NanoKVM device: polls the LED state (and HDMI) on an
/// interval, holds connection and device-info state, and wraps the power/reset/
/// reboot/HDMI actions along with a confirmation dialog. Analogous to <see cref="SystemMonitor"/>,
/// but leaner (a standalone device, not via the ServerRemote API).
/// </summary>
public sealed partial class NanoKvmMonitor : ObservableObject
{
    // Power-button hold duration (ms): short = normal power on/shutdown, long = hard off.
    private const int PowerShortMs = 800;
    private const int PowerLongMs = 5000;

    private readonly NanoKvmApiClient _api;
    private readonly ISettingsService _settings;
    private IDispatcherTimer? _timer;
    private bool _settingsLoaded;
    private bool _isLoading;
    private bool _deviceInfoLoaded;

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _statusText = "Not connected";
    [ObservableProperty] private bool _powerLedOn;
    [ObservableProperty] private bool _hddLedOn;
    [ObservableProperty] private DateTimeOffset? _lastUpdated;
    [ObservableProperty] private string? _lastError;

    // Device info / HDMI (phase 2)
    [ObservableProperty] private NanoKvmInfo? _info;
    [ObservableProperty] private NanoKvmHardware? _hardware;
    [ObservableProperty] private NanoKvmVersion? _version;
    [ObservableProperty] private NanoKvmHdmiState? _hdmi;

    public bool HasInfo => Info is not null;
    public bool HasHdmi => Hdmi is not null;

    partial void OnInfoChanged(NanoKvmInfo? value) => OnPropertyChanged(nameof(HasInfo));
    partial void OnHdmiChanged(NanoKvmHdmiState? value) => OnPropertyChanged(nameof(HasHdmi));

    public NanoKvmMonitor(NanoKvmApiClient api, ISettingsService settings)
    {
        _api = api;
        _settings = settings;
    }

    private bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Current.NanoKvmHost);

    /// <summary>Starts polling (idempotent) and triggers a refresh immediately.</summary>
    public void EnsureStarted()
    {
        if (_timer is null)
        {
            _timer = Application.Current?.Dispatcher.CreateTimer();
            if (_timer is not null)
            {
                _timer.Interval = TimeSpan.FromSeconds(5);
                _timer.Tick += async (_, _) => await RefreshAsync();
                _timer.Start();
            }
        }

        _ = RefreshAsync();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    [RelayCommand]
    private async Task ManualRefreshAsync()
    {
        _deviceInfoLoaded = false; // re-fetch device info on a manual refresh
        await RefreshAsync();
        IsRefreshing = false;
    }

    [RelayCommand]
    private Task ReconnectAsync() => RefreshAsync();

    // Updates the HDMI state only on a real change (prevents rebind flicker)
    // and swallows errors from non-PCIe devices without affecting the connection.
    private async Task UpdateHdmiAsync()
    {
        try
        {
            var hdmi = await _api.GetHdmiAsync();
            if (!HdmiEquals(Hdmi, hdmi))
                Hdmi = hdmi;
        }
        catch
        {
            // HDMI endpoint not available (e.g. non-PCIe) — ignore.
        }
    }

    private static bool HdmiEquals(NanoKvmHdmiState? a, NanoKvmHdmiState? b)
        => a is null
            ? b is null
            : b is not null && a.State == b.State && a.Width == b.Width && a.Height == b.Height;

    public async Task RefreshAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            if (!_settingsLoaded)
            {
                await _settings.LoadAsync();
                _settingsLoaded = true;
            }

            if (!IsConfigured)
            {
                IsConnected = false;
                StatusText = "Not configured";
                return;
            }

            // The LED is the connection indicator — only this call controls IsConnected.
            var led = await _api.GetLedAsync();
            IsConnected = led is not null;
            StatusText = IsConnected ? "Connected" : "Unreachable";

            if (led is not null)
            {
                PowerLedOn = led.Pwr;
                HddLedOn = led.Hdd;
            }

            if (!IsConnected)
                return;

            // Load device info only once (or after a manual refresh) — its own
            // try/catch so an error here neither drops the connection nor causes flicker.
            if (!_deviceInfoLoaded)
            {
                try
                {
                    Info = await _api.GetInfoAsync();
                    Hardware = await _api.GetHardwareAsync();
                    Version = await _api.GetVersionAsync();
                }
                catch { /* optional — do not affect the connection */ }
                finally { _deviceInfoLoaded = true; }
            }

            // HDMI is optional (PCIe only). Its own try/catch + value comparison so that
            // neither a 404 flips the connection nor a new object rebinds the card.
            await UpdateHdmiAsync();

            // Clear only on success — no toggling null↔message per poll (was a cause of flicker).
            LastError = null;
            LastUpdated = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusText = "Error";
            LastError = ex.Message;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ----- Power / Reset / Reboot (phase 1) -----

    [RelayCommand]
    private Task PowerShortAsync() => RunActionAsync(
        () => _api.PowerAsync(PowerShortMs), "Power button pressed.");

    [RelayCommand]
    private async Task PowerLongAsync()
    {
        if (!await ConfirmAsync("Power long", "Hold the power button for 5 seconds (hard power off)?", "Hold"))
            return;
        await RunActionAsync(() => _api.PowerAsync(PowerLongMs), "Power button held.");
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (!await ConfirmAsync("Reset", "Trigger the reset button on the target machine?", "Reset"))
            return;
        await RunActionAsync(() => _api.ResetAsync(), "Reset triggered.");
    }

    [RelayCommand]
    private async Task RebootDeviceAsync()
    {
        if (!await ConfirmAsync("Restart NanoKVM", "Restart the NanoKVM device itself?", "Restart"))
            return;
        await RunActionAsync(() => _api.RebootDeviceAsync(), "NanoKVM is restarting …");
    }

    // ----- HDMI (phase 2) -----

    [RelayCommand]
    private Task HdmiResetAsync() => RunActionAsync(
        () => _api.HdmiResetAsync(), "HDMI reset.");

    [RelayCommand]
    private Task HdmiEnableAsync() => RunActionAsync(
        () => _api.HdmiEnableAsync(), "HDMI enabled.");

    [RelayCommand]
    private async Task HdmiDisableAsync()
    {
        if (!await ConfirmAsync("Disable HDMI", "Really disable HDMI capture?", "Disable"))
            return;
        await RunActionAsync(() => _api.HdmiDisableAsync(), "HDMI disabled.");
    }

    private async Task RunActionAsync(Func<Task> action, string successText)
    {
        try
        {
            await action();
            StatusText = successText;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            StatusText = "Error";
        }
    }

    private static async Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return false;
        return await page.DisplayAlertAsync(title, message, accept, "Cancel");
    }
}
