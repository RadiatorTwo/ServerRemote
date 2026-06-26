namespace ServerRemote.App.Models;

/// <summary>Assessment of drive usage for color highlighting.</summary>
public enum DriveStatus
{
    Normal,
    Warning,
    Critical
}

public static class DriveStatusExtensions
{
    private const double GiB = 1024.0 * 1024.0 * 1024.0;

    // Percentage thresholds
    private const double WarnPercent = 85;
    private const double CriticalPercent = 90;

    // Absolute free-space thresholds (in GiB) – prevent false alarms on large disks,
    // where e.g. 90 % still means hundreds of GB free.
    private const double WarnFreeGib = 25;
    private const double CriticalFreeGib = 10;

    /// <summary>
    /// Hybrid assessment: a status is only triggered when both the percentage
    /// fill level and the absolute free space reach the respective threshold.
    /// This way a large disk at 90 % does not raise a false alarm, while a
    /// small disk becomes critical sooner at the same percentage.
    /// </summary>
    public static DriveStatus FromUsage(double percent, long freeBytes)
    {
        var freeGib = freeBytes / GiB;

        if (percent >= CriticalPercent && freeGib < CriticalFreeGib)
            return DriveStatus.Critical;

        if (percent >= WarnPercent && freeGib < WarnFreeGib)
            return DriveStatus.Warning;

        return DriveStatus.Normal;
    }

    public static string ToLabel(this DriveStatus status) => status switch
    {
        DriveStatus.Critical => "Critical",
        DriveStatus.Warning => "Warning",
        _ => "OK"
    };
}
