namespace ServerRemote.Contracts;

/// <summary>
/// Runtime status of a monitored Windows service (e.g. MSSQL, PostgreSQL).
/// </summary>
public sealed record ServiceStatusDto
{
    /// <summary>Stable logical key (from the configuration), e.g. "mssql".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display name for the UI, e.g. "MSSQL Server".</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Actual Windows service name, e.g. "MSSQLSERVER".</summary>
    public string WindowsServiceName { get; init; } = string.Empty;

    public ServiceState State { get; init; } = ServiceState.Unknown;

    /// <summary>True if the service exists and may be controlled.</summary>
    public bool CanControl { get; init; }

    /// <summary>Error text if the status could not be determined.</summary>
    public string? Error { get; init; }
}

public enum ServiceState
{
    Unknown = 0,
    Stopped,
    StartPending,
    StopPending,
    Running,
    ContinuePending,
    PausePending,
    Paused,
    NotInstalled
}

/// <summary>Control action for a service.</summary>
public enum ServiceControlAction
{
    Start,
    Stop,
    Restart
}

/// <summary>Result of a service control action.</summary>
public sealed record ServiceActionResultDto
{
    public bool Success { get; init; }
    public ServiceState State { get; init; }
    public string? Message { get; init; }
}
