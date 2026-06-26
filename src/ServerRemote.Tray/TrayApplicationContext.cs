using System.Diagnostics;
using System.ServiceProcess;

namespace ServerRemote.Tray;

/// <summary>
/// Tray companion without a main window: shows the health status of the local
/// ServerRemote service as a NotifyIcon and provides a context menu.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayConfig _config;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HttpClient _http;
    private bool _lastHealthy;

    public TrayApplicationContext()
    {
        _config = TrayConfig.Load();

        // Accept the local service's self-signed certificate (localhost only).
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Dashboard", null, OnOpenDashboard);
        menu.Items.Add("Restart Service", null, OnRestartService);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "ServerRemote — checking status …",
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += OnOpenDashboard;

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(3, _config.PollSeconds) * 1000 };
        _timer.Tick += async (_, _) => await PollHealthAsync();
        _timer.Start();

        _ = PollHealthAsync();
    }

    private async Task PollHealthAsync()
    {
        bool healthy;
        try
        {
            using var resp = await _http.GetAsync(_config.HealthUrl);
            healthy = resp.IsSuccessStatusCode;
        }
        catch
        {
            healthy = false;
        }

        _lastHealthy = healthy;
        _notifyIcon.Icon = healthy ? SystemIcons.Information : SystemIcons.Error;
        _notifyIcon.Text = healthy
            ? "ServerRemote — service reachable"
            : "ServerRemote — service NOT reachable";
    }

    private void OnOpenDashboard(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_config.DashboardUrl))
        {
            _notifyIcon.ShowBalloonTip(3000, "ServerRemote",
                _lastHealthy ? "Service is running. No dashboard URL configured." : "Service is not reachable.",
                ToolTipIcon.Info);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_config.DashboardUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open the dashboard: {ex.Message}");
        }
    }

    private void OnRestartService(object? sender, EventArgs e)
    {
        try
        {
            using var sc = new ServiceController(_config.WindowsServiceName);
            if (sc.CanStop && sc.Status != ServiceControllerStatus.Stopped)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            sc.Refresh();
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            _notifyIcon.ShowBalloonTip(3000, "ServerRemote", "Service restarted.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            ShowError($"Restart failed (admin rights required?): {ex.Message}");
        }
    }

    private void ShowError(string message) =>
        _notifyIcon.ShowBalloonTip(4000, "ServerRemote — Error", message, ToolTipIcon.Error);

    private void OnExit(object? sender, EventArgs e)
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _http.Dispose();
        ExitThread();
    }
}
