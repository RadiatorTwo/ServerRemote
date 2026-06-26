using System.Text.Json;

namespace ServerRemote.Tray;

/// <summary>Configuration for the tray app, loaded from appsettings.json.</summary>
public sealed class TrayConfig
{
    public string HealthUrl { get; set; } = "https://localhost:9443/api/health";
    public string DashboardUrl { get; set; } = "";
    public string WindowsServiceName { get; set; } = "ServerRemoteService";
    public int PollSeconds { get; set; } = 10;

    public static TrayConfig Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<TrayConfig>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new TrayConfig();
            }
        }
        catch
        {
            // Fall back to defaults
        }
        return new TrayConfig();
    }
}
