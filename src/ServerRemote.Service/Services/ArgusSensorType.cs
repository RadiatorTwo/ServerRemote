namespace ServerRemote.Service.Services;

// Mirrors ARGUS_MONITOR_SENSOR_TYPE from argus_monitor_data_api.h.
// The values are the ordinal positions of the original C++ enum and must NOT be reordered.
internal enum ArgusSensorType : uint
{
    Invalid = 0,
    Temperature,
    SyntheticTemperature,
    FanSpeedRpm,
    FanControlValue,
    NetworkSpeed,
    CpuTemperature,
    CpuTemperatureAdditional,
    CpuMultiplier,
    CpuFrequencyFsb,
    GpuTemperature,
    GpuName,
    GpuLoad,
    GpuCoreClk,
    GpuMemoryClk,
    GpuShaderClk,
    GpuFanSpeedPercent,
    GpuFanSpeedRpm,
    GpuMemoryUsedPercent,
    GpuMemoryUsedMb,
    GpuPower,
    DiskTemperature,
    DiskTransferRate,
    CpuLoad,
    RamUsage,
    Battery,
    CpuPower,
    Max,
}
