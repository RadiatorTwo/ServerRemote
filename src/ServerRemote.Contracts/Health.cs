namespace ServerRemote.Contracts;

/// <summary>
/// Liveness information for the service. Returned by the open /api/health endpoint.
/// </summary>
public sealed record HealthDto
{
    public string Status { get; init; } = "ok";
    public string Version { get; init; } = "0.0.0";
    public string Hostname { get; init; } = string.Empty;

    /// <summary>System uptime in seconds.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>Server time in UTC, ISO 8601.</summary>
    public DateTimeOffset ServerTimeUtc { get; init; }
}
