using ServerRemote.App.Models;

namespace ServerRemote.App.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync();
    Task SaveAsync(AppSettings settings);
}
