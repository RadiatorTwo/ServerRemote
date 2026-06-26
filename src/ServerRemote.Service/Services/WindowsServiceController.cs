using System.ServiceProcess;
using System.Runtime.Versioning;
using Microsoft.Extensions.Options;
using ServerRemote.Contracts;
using ServerRemote.Service.Configuration;

namespace ServerRemote.Service.Services;

/// <summary>
/// Queries monitored Windows services and controls them via
/// <see cref="ServiceController"/>. Only configured services are accessible.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceController : IWindowsServiceController
{
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<WindowsServiceController> _logger;
    private readonly IReadOnlyDictionary<string, MonitoredServiceOptions> _services;

    public WindowsServiceController(
        IOptions<ServerRemoteOptions> options,
        ILogger<WindowsServiceController> logger)
    {
        _logger = logger;
        _services = options.Value.MonitoredServices
            .Where(s => !string.IsNullOrWhiteSpace(s.Key))
            .ToDictionary(s => s.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ServiceStatusDto> GetAllStatus() =>
        _services.Values.Select(BuildStatus).ToList();

    public ServiceStatusDto? GetStatus(string key) =>
        _services.TryGetValue(key, out var cfg) ? BuildStatus(cfg) : null;

    public async Task<ServiceActionResultDto> ControlAsync(
        string key, ServiceControlAction action, CancellationToken ct)
    {
        if (!_services.TryGetValue(key, out var cfg))
            return new ServiceActionResultDto { Success = false, Message = $"Unknown service '{key}'." };

        if (!cfg.Controllable)
            return new ServiceActionResultDto { Success = false, Message = "This service is not allowed to be controlled." };

        try
        {
            // Keep blocking ServiceController calls off the request thread.
            return await Task.Run(() => ExecuteControl(cfg, action), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Control of {Service} ({Action}) failed.", cfg.WindowsServiceName, action);
            return new ServiceActionResultDto
            {
                Success = false,
                State = ServiceState.Unknown,
                Message = ex.Message
            };
        }
    }

    private ServiceActionResultDto ExecuteControl(MonitoredServiceOptions cfg, ServiceControlAction action)
    {
        using var sc = new ServiceController(cfg.WindowsServiceName);

        switch (action)
        {
            case ServiceControlAction.Start:
                if (sc.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, ControlTimeout);
                }
                break;

            case ServiceControlAction.Stop:
                if (sc.CanStop && sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, ControlTimeout);
                }
                break;

            case ServiceControlAction.Restart:
                if (sc.CanStop && sc.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, ControlTimeout);
                }
                sc.Refresh();
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, ControlTimeout);
                break;
        }

        sc.Refresh();
        return new ServiceActionResultDto
        {
            Success = true,
            State = Map(sc.Status),
            Message = $"Action '{action}' executed."
        };
    }

    private ServiceStatusDto BuildStatus(MonitoredServiceOptions cfg)
    {
        try
        {
            using var sc = new ServiceController(cfg.WindowsServiceName);
            var status = sc.Status; // throws if the service is not installed
            return new ServiceStatusDto
            {
                Key = cfg.Key,
                DisplayName = cfg.DisplayName,
                WindowsServiceName = cfg.WindowsServiceName,
                State = Map(status),
                CanControl = cfg.Controllable
            };
        }
        catch (InvalidOperationException)
        {
            return new ServiceStatusDto
            {
                Key = cfg.Key,
                DisplayName = cfg.DisplayName,
                WindowsServiceName = cfg.WindowsServiceName,
                State = ServiceState.NotInstalled,
                CanControl = false,
                Error = "Service is not installed."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the status of {Service}.", cfg.WindowsServiceName);
            return new ServiceStatusDto
            {
                Key = cfg.Key,
                DisplayName = cfg.DisplayName,
                WindowsServiceName = cfg.WindowsServiceName,
                State = ServiceState.Unknown,
                CanControl = false,
                Error = ex.Message
            };
        }
    }

    private static ServiceState Map(ServiceControllerStatus status) => status switch
    {
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.ContinuePending => ServiceState.ContinuePending,
        ServiceControllerStatus.PausePending => ServiceState.PausePending,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        _ => ServiceState.Unknown
    };
}
