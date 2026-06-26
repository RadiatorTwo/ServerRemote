namespace ServerRemote.Contracts;

/// <summary>
/// Snapshot of the server load (CPU, RAM, disk space).
/// </summary>
public sealed record SystemMetricsDto
{
    /// <summary>Total CPU usage in percent (0-100).</summary>
    public double CpuPercent { get; init; }

    /// <summary>Optional: usage per logical core in percent.</summary>
    public IReadOnlyList<double> PerCorePercent { get; init; } = Array.Empty<double>();

    public long RamUsedMb { get; init; }
    public long RamTotalMb { get; init; }

    public double RamUsedPercent =>
        RamTotalMb <= 0 ? 0 : Math.Round(RamUsedMb * 100.0 / RamTotalMb, 1);

    public IReadOnlyList<DriveDto> Drives { get; init; } = Array.Empty<DriveDto>();

    public DateTimeOffset SampledAtUtc { get; init; }
}

/// <summary>
/// Usage of a single drive.
/// </summary>
public sealed record DriveDto
{
    /// <summary>Drive letter / mount point, e.g. "C:\".</summary>
    public string Name { get; init; } = string.Empty;

    public string? VolumeLabel { get; init; }

    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public long UsedBytes => TotalBytes - FreeBytes;

    public double UsedPercent =>
        TotalBytes <= 0 ? 0 : Math.Round(UsedBytes * 100.0 / TotalBytes, 1);
}
