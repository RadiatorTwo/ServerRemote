using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

public interface ISystemPowerService
{
    SystemPowerResultDto Schedule(SystemPowerRequest request);
}
