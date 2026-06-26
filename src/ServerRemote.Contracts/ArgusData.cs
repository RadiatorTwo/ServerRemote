namespace ServerRemote.Contracts;

/// <summary>
/// Sensor data from the Argus Monitor shared memory API.
/// Placeholder structure for a later phase — field names/types will be
/// aligned with the official Argus Monitor struct once integrated.
/// </summary>
public sealed record ArgusDataDto
{
    /// <summary>True if Argus Monitor is running and has delivered data.</summary>
    public bool Available { get; init; }

    public IReadOnlyList<ArgusSensorDto> Temperatures { get; init; } = Array.Empty<ArgusSensorDto>();
    public IReadOnlyList<ArgusSensorDto> FanSpeeds { get; init; } = Array.Empty<ArgusSensorDto>();
    public IReadOnlyList<ArgusSensorDto> Loads { get; init; } = Array.Empty<ArgusSensorDto>();
    public IReadOnlyList<ArgusSensorDto> Powers { get; init; } = Array.Empty<ArgusSensorDto>();

    public DateTimeOffset SampledAtUtc { get; init; }
    public string? Error { get; init; }
}

public sealed record ArgusSensorDto
{
    public string Label { get; init; } = string.Empty;
    public double Value { get; init; }
    public string Unit { get; init; } = string.Empty;
}
