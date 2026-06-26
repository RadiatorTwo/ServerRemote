using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

/// <summary>
/// Reads sensor data from the shared-memory data API of Argus Monitor.
///
/// Layout (from argus_monitor_data_api.h, #pragma pack(1)):
///   ArgusMonitorData-Header  =  240 Bytes
///     +  0  Signature                       u32
///     +  4  ArgusMajor/MinorA/MinorB/Extra  4 * u8
///     +  8  ArgusBuild                      u32
///     + 12  Version                         u32
///     + 16  CycleCounter                    u32
///     + 20  OffsetForSensorType[27]         27 * u32
///     +128  SensorCount[27]                 27 * u32
///     +236  TotalSensorCount                u32
///   Jeder ArgusMonitorSensorData            = 212 Bytes
///     +  0  SensorType                      u32
///     +  4  Label                           wchar_t[64]   (128 Bytes UTF-16)
///     +132  UnitString                      wchar_t[32]   ( 64 Bytes UTF-16)
///     +196  Value                           f64
///     +204  DataIndex                       u32
///     +208  SensorIndex                     u32
///
/// Reading happens on demand (the app polls ~every 5 s): the mapping file stays open
/// between calls and is closed and reconnected when an error occurs or Argus exits.
/// </summary>
public sealed class ArgusService : IArgusService, IDisposable
{
    private const string MappingName = "Global\\ARGUSMONITOR_DATA_INTERFACE";
    private const string MutexName = "Global\\ARGUSMONITOR_DATA_INTERFACE_MUTEX";
    private const long MappingSize = 1024 * 1024;

    private const int SensorEntrySize = 212;
    private const int MaxSensorCount = 512;

    private const int OffsetTotalSensorCount = 236;
    private const int OffsetSensorData = 240;

    private readonly object _gate = new();
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private Mutex? _mutex;

    public ArgusDataDto GetSnapshot()
    {
        if (!OperatingSystem.IsWindows())
            return Unavailable("Argus Monitor is only supported on Windows.");

        lock (_gate)
        {
            if (_accessor == null && !TryOpen())
                return Unavailable("Argus Monitor is not running or the shared-memory interface is not active.");

            try
            {
                if (TryRead(out var sensors))
                    return Map(sensors!);

                return Unavailable("Could not read sensor data from Argus Monitor.");
            }
            catch (Exception ex)
            {
                Close();
                return Unavailable($"Reading from the Argus shared memory failed: {ex.Message}");
            }
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MappingName, MemoryMappedFileRights.Read);
            _accessor = _mmf.CreateViewAccessor(0, MappingSize, MemoryMappedFileAccess.Read);
            _mutex = Mutex.OpenExisting(MutexName);
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    private void Close()
    {
        try { _accessor?.Dispose(); } catch { }
        try { _mmf?.Dispose(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _accessor = null;
        _mmf = null;
        _mutex = null;
    }

    private unsafe bool TryRead(out IReadOnlyList<(ArgusSensorType Type, string Label, string Unit, double Value)>? sensors)
    {
        sensors = null;

        if (_accessor == null || _mutex == null)
            return false;

        var acquired = false;
        try
        {
            acquired = _mutex.WaitOne(TimeSpan.FromMilliseconds(500), false);
            if (!acquired)
                return false;

            byte* basePtr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            try
            {
                if (basePtr == null)
                    return false;

                var view = new ReadOnlySpan<byte>(basePtr, (int)MappingSize);

                var totalSensorCount = BinaryPrimitives.ReadUInt32LittleEndian(view.Slice(OffsetTotalSensorCount, 4));
                if (totalSensorCount > MaxSensorCount)
                    totalSensorCount = MaxSensorCount;

                var list = new List<(ArgusSensorType, string, string, double)>((int)totalSensorCount);
                for (var i = 0u; i < totalSensorCount; i++)
                {
                    var entry = view.Slice(OffsetSensorData + (int)i * SensorEntrySize, SensorEntrySize);

                    var type = (ArgusSensorType)BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(0, 4));
                    var label = ReadWideString(entry.Slice(4, 128));
                    var unit = ReadWideString(entry.Slice(132, 64));
                    var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(entry.Slice(196, 8)));

                    list.Add((type, label, unit, value));
                }

                sensors = list;
                return true;
            }
            finally
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died while holding the mutex — discard the state.
            return false;
        }
        finally
        {
            if (acquired)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }
        }
    }

    private static ArgusDataDto Map(
        IReadOnlyList<(ArgusSensorType Type, string Label, string Unit, double Value)> sensors)
    {
        var temperatures = new List<ArgusSensorDto>();
        var fanSpeeds = new List<ArgusSensorDto>();
        var loads = new List<ArgusSensorDto>();
        var powers = new List<ArgusSensorDto>();

        foreach (var s in sensors)
        {
            var dto = new ArgusSensorDto { Label = s.Label, Value = s.Value, Unit = s.Unit };
            switch (s.Type)
            {
                case ArgusSensorType.Temperature:
                case ArgusSensorType.SyntheticTemperature:
                case ArgusSensorType.CpuTemperature:
                case ArgusSensorType.CpuTemperatureAdditional:
                case ArgusSensorType.GpuTemperature:
                case ArgusSensorType.DiskTemperature:
                    temperatures.Add(dto);
                    break;

                case ArgusSensorType.FanSpeedRpm:
                case ArgusSensorType.GpuFanSpeedRpm:
                    fanSpeeds.Add(dto);
                    break;

                case ArgusSensorType.CpuLoad:
                case ArgusSensorType.GpuLoad:
                case ArgusSensorType.RamUsage:
                case ArgusSensorType.GpuMemoryUsedPercent:
                    loads.Add(dto);
                    break;

                case ArgusSensorType.CpuPower:
                case ArgusSensorType.GpuPower:
                    powers.Add(dto);
                    break;
            }
        }

        return new ArgusDataDto
        {
            Available = true,
            Temperatures = temperatures,
            FanSpeeds = fanSpeeds,
            Loads = loads,
            Powers = powers,
            SampledAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static ArgusDataDto Unavailable(string error) => new()
    {
        Available = false,
        SampledAtUtc = DateTimeOffset.UtcNow,
        Error = error,
    };

    private static string ReadWideString(ReadOnlySpan<byte> bytes)
    {
        // UTF-16 little-endian, NUL-terminated. Find the first 0x0000 character.
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            if (bytes[i] == 0 && bytes[i + 1] == 0)
                return Encoding.Unicode.GetString(bytes.Slice(0, i));
        }
        return Encoding.Unicode.GetString(bytes);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Close();
        }
    }
}
