using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

public interface IMetricsService
{
    SystemMetricsDto GetMetrics();
}
