using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

/// <summary>
/// Reads CPU, RAM, and disk space metrics on Windows.
/// Registered as a singleton: the CPU PerformanceCounter is kept between
/// calls (the first value of a counter is always 0).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MetricsService : IMetricsService, IDisposable
{
    private readonly ILogger<MetricsService> _logger;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly object _gate = new();

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // Warm-up — first value is 0.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize the CPU PerformanceCounter.");
            _cpuCounter = null;
        }
    }

    public SystemMetricsDto GetMetrics()
    {
        return new SystemMetricsDto
        {
            CpuPercent = ReadCpuPercent(),
            RamUsedMb = ReadRamUsedMb(out long totalMb),
            RamTotalMb = totalMb,
            Drives = ReadDrives(),
            SampledAtUtc = DateTimeOffset.UtcNow
        };
    }

    private double ReadCpuPercent()
    {
        if (_cpuCounter is null)
            return 0;

        lock (_gate)
        {
            try
            {
                return Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the CPU value.");
                return 0;
            }
        }
    }

    private long ReadRamUsedMb(out long totalMb)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            totalMb = (long)(status.ullTotalPhys / (1024 * 1024));
            long availMb = (long)(status.ullAvailPhys / (1024 * 1024));
            return totalMb - availMb;
        }

        totalMb = 0;
        _logger.LogWarning("GlobalMemoryStatusEx failed.");
        return 0;
    }

    private List<DriveDto> ReadDrives()
    {
        var drives = new List<DriveDto>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                drives.Add(new DriveDto
                {
                    Name = drive.Name,
                    VolumeLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read drive {Drive}.", drive.Name);
            }
        }
        return drives;
    }

    public void Dispose() => _cpuCounter?.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
