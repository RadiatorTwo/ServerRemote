using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.ViewModels;
using ServerRemote.Contracts;

namespace ServerRemote.App.Services;

/// <summary>
/// Central, shared data layer for all pages: polls the ServerRemote API on a
/// single interval and exposes the observable system state (connection, CPU,
/// RAM, drives, services, sensors). Also encapsulates the power and service
/// control actions together with their confirmation dialog.
/// </summary>
public sealed partial class SystemMonitor : ObservableObject
{
    private readonly ServerApiClient _api;
    private readonly ISettingsService _settings;
    private IDispatcherTimer? _timer;
    private bool _settingsLoaded;
    private bool _isLoading;

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _serverReachable;
    [ObservableProperty] private string _statusText = "Not connected";
    [ObservableProperty] private string? _hostname;
    [ObservableProperty] private DateTimeOffset? _lastUpdated;
    [ObservableProperty] private string? _lastError;

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _ramUsedPercent;
    [ObservableProperty] private long _ramUsedMb;
    [ObservableProperty] private long _ramTotalMb;

    [ObservableProperty] private ArgusDataDto? _argus;

    // When the Argus snapshot changes, also refresh the derived sensor tiles.
    partial void OnArgusChanged(ArgusDataDto? value) => RaiseSensorSelection();

    /// <summary>
    /// Call after the sensor selection changes in the settings so that the
    /// dashboard immediately shows the newly chosen temperature/power sensors.
    /// </summary>
    public void NotifySensorSelectionChanged() => RaiseSensorSelection();

    private void RaiseSensorSelection()
    {
        OnPropertyChanged(nameof(HasTemperatureSensor));
        OnPropertyChanged(nameof(TemperatureValueText));
        OnPropertyChanged(nameof(TemperatureCaption));
        OnPropertyChanged(nameof(HasPowerSensor));
        OnPropertyChanged(nameof(PowerValueText));
        OnPropertyChanged(nameof(PowerCaption));
    }

    private static ArgusSensorDto? Find(IReadOnlyList<ArgusSensorDto>? list, string label) =>
        string.IsNullOrEmpty(label) || list is null
            ? null
            : list.FirstOrDefault(s => s.Label == label);

    private ArgusSensorDto? TemperatureSensor => Find(Argus?.Temperatures, _settings.Current.TemperatureSensorLabel);
    private ArgusSensorDto? PowerSensor => Find(Argus?.Powers, _settings.Current.PowerSensorLabel);

    public bool HasTemperatureSensor => TemperatureSensor is not null;
    public string TemperatureValueText => Format(TemperatureSensor, "0.0");
    public string TemperatureCaption => TemperatureSensor?.Label ?? string.Empty;

    public bool HasPowerSensor => PowerSensor is not null;
    public string PowerValueText => Format(PowerSensor, "0");
    public string PowerCaption => PowerSensor?.Label ?? string.Empty;

    private static string Format(ArgusSensorDto? s, string numberFormat)
    {
        if (s is null) return "—";
        var unit = string.IsNullOrWhiteSpace(s.Unit) ? "" : $" {s.Unit}";
        return s.Value.ToString(numberFormat, System.Globalization.CultureInfo.CurrentCulture) + unit;
    }

    public ObservableCollection<DriveItemViewModel> Drives { get; } = new();
    public ObservableCollection<ServiceItemViewModel> Services { get; } = new();

    public SystemMonitor(ServerApiClient api, ISettingsService settings)
    {
        _api = api;
        _settings = settings;
    }

    /// <summary>Starts the polling (idempotent) and triggers an immediate refresh.</summary>
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

    /// <summary>Manual refresh (pull-to-refresh). Shows the RefreshView spinner.</summary>
    [RelayCommand]
    private async Task ManualRefreshAsync()
    {
        await RefreshAsync();
        IsRefreshing = false;
    }

    /// <summary>
    /// Refreshes the system state. Called by the background timer and deliberately
    /// does NOT touch <see cref="IsRefreshing"/> — otherwise the RefreshView would
    /// show its spinner on every poll and jerk the content up/down.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        LastError = null;

        try
        {
            if (!_settingsLoaded)
            {
                await _settings.LoadAsync();
                _settingsLoaded = true;
            }

            var health = await _api.GetHealthAsync();
            ServerReachable = health is not null;
            Hostname = health?.Hostname;
            StatusText = ServerReachable ? $"Connected — {Hostname}" : "Unreachable";

            if (!ServerReachable)
                return;

            var metrics = await _api.GetMetricsAsync();
            if (metrics is not null)
            {
                CpuPercent = metrics.CpuPercent;
                RamUsedPercent = metrics.RamUsedPercent;
                RamUsedMb = metrics.RamUsedMb;
                RamTotalMb = metrics.RamTotalMb;

                SyncDrives(metrics.Drives);
            }

            await RefreshServicesAsync();

            Argus = await _api.GetArgusAsync();

            LastUpdated = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            ServerReachable = false;
            StatusText = "Error";
            LastError = ex.Message;
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    private Task ReconnectAsync() => RefreshAsync();

    /// <summary>Updates the drive list in-place (stable instances, no rebuild).</summary>
    private void SyncDrives(IReadOnlyList<DriveDto> incoming)
    {
        var keys = incoming.Select(d => d.Name).ToHashSet();
        for (int i = Drives.Count - 1; i >= 0; i--)
            if (!keys.Contains(Drives[i].Key))
                Drives.RemoveAt(i);

        foreach (var dto in incoming)
        {
            var existing = Drives.FirstOrDefault(d => d.Key == dto.Name);
            if (existing is null)
                Drives.Add(new DriveItemViewModel(dto));
            else
                existing.Update(dto);
        }
    }

    private async Task RefreshServicesAsync()
    {
        var services = await _api.GetServicesAsync();

        // Update existing items, add new ones, remove gone ones.
        var keys = services.Select(s => s.Key).ToHashSet();
        for (int i = Services.Count - 1; i >= 0; i--)
            if (!keys.Contains(Services[i].Key))
                Services.RemoveAt(i);

        foreach (var dto in services)
        {
            var existing = Services.FirstOrDefault(s => s.Key == dto.Key);
            if (existing is null)
                Services.Add(new ServiceItemViewModel(dto, ControlServiceAsync));
            else
                existing.Update(dto);
        }
    }

    private async Task ControlServiceAsync(string key, ServiceControlAction action)
    {
        try
        {
            var result = await _api.ControlServiceAsync(key, action);

            // Server reports an error (e.g. missing permissions, service not controllable) –
            // surface it instead of silently swallowing it.
            if (result is { Success: false })
                LastError = result.Message ?? "Service control failed.";

            await RefreshServicesAsync();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ShutdownAsync()
    {
        if (!await ConfirmAsync("Shut down", $"Really shut down {Hostname ?? "Server"}?", "Shut down"))
            return;
        await PowerAsync(SystemPowerAction.Shutdown);
    }

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!await ConfirmAsync("Restart", $"Really restart {Hostname ?? "Server"}?", "Restart"))
            return;
        await PowerAsync(SystemPowerAction.Reboot);
    }

    private async Task PowerAsync(SystemPowerAction action)
    {
        try
        {
            var result = await _api.PowerAsync(action, delaySeconds: 15);
            StatusText = result?.Message ?? "Action sent.";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
    }

    private static async Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return false;
        return await page.DisplayAlertAsync(title, message, accept, "Cancel");
    }
}
