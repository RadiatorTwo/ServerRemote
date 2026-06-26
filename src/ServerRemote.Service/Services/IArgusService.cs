using ServerRemote.Contracts;

namespace ServerRemote.Service.Services;

/// <summary>Reads sensor data from the Argus Monitor shared-memory API.</summary>
public interface IArgusService
{
    /// <summary>
    /// Returns a current snapshot of the Argus sensors. If Argus Monitor is not
    /// active, <see cref="ArgusDataDto.Available"/> is false and <see cref="ArgusDataDto.Error"/> is set.
    /// </summary>
    ArgusDataDto GetSnapshot();
}
