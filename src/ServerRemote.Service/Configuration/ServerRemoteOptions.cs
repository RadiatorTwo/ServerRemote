namespace ServerRemote.Service.Configuration;

/// <summary>
/// Main configuration from the "ServerRemote" section in appsettings.json.
/// </summary>
public sealed class ServerRemoteOptions
{
    public const string SectionName = "ServerRemote";

    /// <summary>API key expected in the "Authorization: Bearer" header.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public NetworkOptions Network { get; set; } = new();
    public CertificateOptions Certificate { get; set; } = new();

    /// <summary>List of monitored/controllable Windows services.</summary>
    public List<MonitoredServiceOptions> MonitoredServices { get; set; } = new();
}

public sealed class NetworkOptions
{
    /// <summary>HTTPS port of the API.</summary>
    public int HttpsPort { get; set; } = 9443;

    /// <summary>Bind address. "0.0.0.0" for LAN access, "127.0.0.1" for local only.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";
}

public sealed class CertificateOptions
{
    /// <summary>Path to the PFX file. Empty = generate a self-signed certificate at startup.</summary>
    public string? PfxPath { get; set; }

    public string? PfxPassword { get; set; }
}

public sealed class MonitoredServiceOptions
{
    /// <summary>Stable logical key, e.g. "mssql".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Display name for the UI.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Actual Windows service name, e.g. "MSSQLSERVER".</summary>
    public string WindowsServiceName { get; set; } = string.Empty;

    /// <summary>If false, only status display is allowed (no start/stop).</summary>
    public bool Controllable { get; set; } = true;
}
