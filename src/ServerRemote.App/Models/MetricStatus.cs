namespace ServerRemote.App.Models;

/// <summary>Assessment of a utilization value (CPU/RAM) for color highlighting.</summary>
public enum MetricStatus
{
    Normal,
    High,
    Critical
}

public static class MetricStatusExtensions
{
    /// <summary>CPU/RAM thresholds: high from 75 %, critical from 90 %.</summary>
    public static MetricStatus FromPercent(double percent) => percent switch
    {
        >= 90 => MetricStatus.Critical,
        >= 75 => MetricStatus.High,
        _ => MetricStatus.Normal
    };

    public static string ToLabel(this MetricStatus status) => status switch
    {
        MetricStatus.Critical => "Critical",
        MetricStatus.High => "High",
        _ => "Normal"
    };
}
