namespace ServerRemote.App.Models;

/// <summary>Connection settings for the ServerRemote service.</summary>
public sealed class AppSettings
{
    public string Host { get; set; } = "192.168.1.10";
    public int Port { get; set; } = 9443;
    public string ApiKey { get; set; } = "";

    /// <summary>Optional SHA-256 fingerprint of the server certificate for pinning (hex).</summary>
    public string CertFingerprint { get; set; } = "";

    /// <summary>Label of the Argus sensor shown as the temperature tile on the dashboard (empty = off).</summary>
    public string TemperatureSensorLabel { get; set; } = "";

    /// <summary>Label of the Argus sensor shown as the power tile on the dashboard (empty = off).</summary>
    public string PowerSensorLabel { get; set; } = "";

    /// <summary>Host/IP of the NanoKVM device (standalone IP-KVM, addressed directly on the LAN).</summary>
    public string NanoKvmHost { get; set; } = "";

    /// <summary>Username for the NanoKVM login (default "admin").</summary>
    public string NanoKvmUsername { get; set; } = "admin";

    /// <summary>Password for the NanoKVM login (stored via SecureStorage).</summary>
    public string NanoKvmPassword { get; set; } = "";

    /// <summary>
    /// Keyboard layout of the HOST for live input (<c>"us"</c> or <c>"de"</c>). HID codes are
    /// position-based — the host applies its own layout, so the character→code
    /// mapping must match the host layout (otherwise e.g. y/z get swapped). Default: German.
    /// </summary>
    public string NanoKvmKeyboardLayout { get; set; } = "de";

    public string BaseUrl => $"https://{Host}:{Port}";

    /// <summary>Base URL of the NanoKVM (cleartext HTTP on the LAN).</summary>
    public string NanoKvmBaseUrl => $"http://{NanoKvmHost}";
}
