using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ServerRemote.App.Models;
using ServerRemote.App.Services;

namespace ServerRemote.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Picker entry that represents “no sensor selected”.</summary>
    public const string NoneOption = "— none —";

    private readonly ISettingsService _settings;
    private readonly SystemMonitor _monitor;

    [ObservableProperty] private string _host = "";
    [ObservableProperty] private int _port;
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _certFingerprint = "";
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty] private string _selectedTemperatureSensor = NoneOption;
    [ObservableProperty] private string _selectedPowerSensor = NoneOption;

    /// <summary>Available temperature sensors from Argus (plus “none”) for the picker.</summary>
    public ObservableCollection<string> TemperatureSensors { get; } = new();

    /// <summary>Available power sensors from Argus (plus “none”) for the picker.</summary>
    public ObservableCollection<string> PowerSensors { get; } = new();

    public SettingsViewModel(ISettingsService settings, SystemMonitor monitor)
    {
        _settings = settings;
        _monitor = monitor;

        // Refill the picker lists as soon as a (new) Argus snapshot arrives.
        _monitor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemMonitor.Argus))
                RefreshSensorOptions();
        };
    }

    public async Task LoadAsync()
    {
        await _settings.LoadAsync();
        var s = _settings.Current;
        Host = s.Host;
        Port = s.Port;
        ApiKey = s.ApiKey;
        CertFingerprint = s.CertFingerprint;

        PopulateSensorOptions(s.TemperatureSensorLabel, s.PowerSensorLabel);
    }

    private void PopulateSensorOptions(string tempLabel, string powerLabel)
    {
        var argus = _monitor.Argus;

        Fill(TemperatureSensors, argus?.Temperatures.Select(x => x.Label), tempLabel);
        Fill(PowerSensors, argus?.Powers.Select(x => x.Label), powerLabel);

        SelectedTemperatureSensor = string.IsNullOrEmpty(tempLabel) ? NoneOption : tempLabel;
        SelectedPowerSensor = string.IsNullOrEmpty(powerLabel) ? NoneOption : powerLabel;
    }

    // Rebuilds the picker lists from the current Argus snapshot while keeping
    // the selection just made (not yet saved).
    private void RefreshSensorOptions()
    {
        var temp = SelectedTemperatureSensor == NoneOption ? "" : SelectedTemperatureSensor;
        var power = SelectedPowerSensor == NoneOption ? "" : SelectedPowerSensor;
        PopulateSensorOptions(temp, power);
    }

    // Fills a picker list: “none” first, then the available labels; the
    // stored value is added if Argus does not currently provide it.
    private static void Fill(ObservableCollection<string> target, IEnumerable<string>? labels, string stored)
    {
        target.Clear();
        target.Add(NoneOption);

        var seen = new HashSet<string>();
        foreach (var label in labels ?? Enumerable.Empty<string>())
            if (!string.IsNullOrWhiteSpace(label) && seen.Add(label))
                target.Add(label);

        if (!string.IsNullOrEmpty(stored) && seen.Add(stored))
            target.Add(stored);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var tempLabel = SelectedTemperatureSensor == NoneOption ? "" : SelectedTemperatureSensor;
        var powerLabel = SelectedPowerSensor == NoneOption ? "" : SelectedPowerSensor;

        var current = _settings.Current;
        await _settings.SaveAsync(new AppSettings
        {
            Host = Host.Trim(),
            Port = Port,
            ApiKey = ApiKey.Trim(),
            CertFingerprint = CertFingerprint.Trim(),
            TemperatureSensorLabel = tempLabel,
            PowerSensorLabel = powerLabel,
            // Pass the NanoKVM fields through untouched (its own settings page).
            NanoKvmHost = current.NanoKvmHost,
            NanoKvmUsername = current.NanoKvmUsername,
            NanoKvmPassword = current.NanoKvmPassword,
            NanoKvmKeyboardLayout = current.NanoKvmKeyboardLayout
        });

        _monitor.NotifySensorSelectionChanged();
        StatusMessage = "Saved.";
    }
}
