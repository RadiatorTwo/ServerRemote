using System.Diagnostics;
using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

/// <summary>
/// Schedules shutdown/reboot via shutdown.exe. As a Windows service the
/// process runs as LocalSystem and therefore holds the SeShutdownPrivilege.
/// The delay allows cancellation via "shutdown /a".
/// </summary>
public sealed class SystemPowerService : ISystemPowerService
{
    private readonly ILogger<SystemPowerService> _logger;

    public SystemPowerService(ILogger<SystemPowerService> logger) => _logger = logger;

    public SystemPowerResultDto Schedule(SystemPowerRequest request)
    {
        if (!request.Confirm)
            return new SystemPowerResultDto { Scheduled = false, Message = "Confirmation (Confirm) is missing." };

        int delay = Math.Clamp(request.DelaySeconds, 0, 3600);
        string flag = request.Action == SystemPowerAction.Reboot ? "/r" : "/s";
        string args = $"{flag} /t {delay} /c \"ServerRemote: {request.Action}\"";

        try
        {
            var psi = new ProcessStartInfo("shutdown.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            _logger.LogWarning("Power action {Action} scheduled in {Delay}s.", request.Action, delay);

            return new SystemPowerResultDto
            {
                Scheduled = true,
                Message = $"{request.Action} scheduled in {delay}s. Can be cancelled with 'shutdown /a'."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Power action {Action} failed.", request.Action);
            return new SystemPowerResultDto { Scheduled = false, Message = ex.Message };
        }
    }
}
