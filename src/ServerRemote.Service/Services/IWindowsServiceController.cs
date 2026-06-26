using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

public interface IWindowsServiceController
{
    /// <summary>Status of all configured services.</summary>
    IReadOnlyList<ServiceStatusDto> GetAllStatus();

    /// <summary>Status of a single configured service (by logical key).</summary>
    ServiceStatusDto? GetStatus(string key);

    /// <summary>Executes a control action on the service.</summary>
    Task<ServiceActionResultDto> ControlAsync(string key, ServiceControlAction action, CancellationToken ct);
}
