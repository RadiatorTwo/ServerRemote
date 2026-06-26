using ServerRemote.App.Models;

namespace ServerRemote.App.Services;

/// <summary>
/// Persists connection settings. Host/Port/Fingerprint via <see cref="Preferences"/>,
/// the API key via <see cref="SecureStorage"/> (encrypted).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string ApiKeyStorageKey = "serverremote_api_key";
    private const string NanoKvmPasswordStorageKey = "nanokvm_password";

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        var settings = new AppSettings
        {
            Host = Preferences.Get(nameof(AppSettings.Host), Current.Host),
            Port = Preferences.Get(nameof(AppSettings.Port), Current.Port),
            CertFingerprint = Preferences.Get(nameof(AppSettings.CertFingerprint), ""),
            TemperatureSensorLabel = Preferences.Get(nameof(AppSettings.TemperatureSensorLabel), ""),
            PowerSensorLabel = Preferences.Get(nameof(AppSettings.PowerSensorLabel), ""),
            NanoKvmHost = Preferences.Get(nameof(AppSettings.NanoKvmHost), ""),
            NanoKvmUsername = Preferences.Get(nameof(AppSettings.NanoKvmUsername), Current.NanoKvmUsername),
            NanoKvmKeyboardLayout = Preferences.Get(nameof(AppSettings.NanoKvmKeyboardLayout), Current.NanoKvmKeyboardLayout)
        };

        try
        {
            settings.ApiKey = await SecureStorage.GetAsync(ApiKeyStorageKey) ?? "";
        }
        catch
        {
            settings.ApiKey = "";
        }

        try
        {
            settings.NanoKvmPassword = await SecureStorage.GetAsync(NanoKvmPasswordStorageKey) ?? "";
        }
        catch
        {
            settings.NanoKvmPassword = "";
        }

        Current = settings;
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Preferences.Set(nameof(AppSettings.Host), settings.Host);
        Preferences.Set(nameof(AppSettings.Port), settings.Port);
        Preferences.Set(nameof(AppSettings.CertFingerprint), settings.CertFingerprint ?? "");
        Preferences.Set(nameof(AppSettings.TemperatureSensorLabel), settings.TemperatureSensorLabel ?? "");
        Preferences.Set(nameof(AppSettings.PowerSensorLabel), settings.PowerSensorLabel ?? "");
        Preferences.Set(nameof(AppSettings.NanoKvmHost), settings.NanoKvmHost ?? "");
        Preferences.Set(nameof(AppSettings.NanoKvmUsername), settings.NanoKvmUsername ?? "");
        Preferences.Set(nameof(AppSettings.NanoKvmKeyboardLayout), settings.NanoKvmKeyboardLayout ?? "de");

        try
        {
            await SecureStorage.SetAsync(ApiKeyStorageKey, settings.ApiKey ?? "");
        }
        catch
        {
            // SecureStorage can fail on some platforms — key then lives only in memory.
        }

        try
        {
            await SecureStorage.SetAsync(NanoKvmPasswordStorageKey, settings.NanoKvmPassword ?? "");
        }
        catch
        {
            // SecureStorage can fail on some platforms — password then lives only in memory.
        }

        Current = settings;
    }
}
