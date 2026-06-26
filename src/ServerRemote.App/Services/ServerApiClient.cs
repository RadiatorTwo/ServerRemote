using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using ServerRemote.Contracts;

namespace ServerRemote.App.Services;

/// <summary>
/// Typed HTTP client for the ServerRemote API. Reads the connection details from
/// <see cref="ISettingsService"/> on every call, sets the Bearer header and
/// validates the (self-signed) server certificate via fingerprint pinning.
/// </summary>
public sealed class ServerApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // Fast read calls (Health/Metrics/Status) must not stall the 5-second poll loop;
    // control actions, on the other hand, have to wait until the service reaches its
    // target state (the server blocks for up to 30 s while doing so).
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(40);

    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public ServerApiClient(ISettingsService settings)
    {
        _settings = settings;

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = ValidateCertificate
        };

        // No global timeout: each call sets an appropriate timeout via a linked
        // CancellationToken source (see SendAsync).
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// Sends the request with a call-specific timeout. The response is fully
    /// buffered (default CompletionOption), so the linked CTS may be disposed
    /// again after the call.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage req, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        return await _http.SendAsync(req, cts.Token);
    }

    private bool ValidateCertificate(HttpRequestMessage request, X509Certificate2? cert,
        X509Chain? chain, System.Net.Security.SslPolicyErrors errors)
    {
        if (cert is null)
            return false;

        var pinned = _settings.Current.CertFingerprint?.Replace(":", "").Trim();

        // Without a pinned fingerprint: accept any certificate (private LAN).
        if (string.IsNullOrWhiteSpace(pinned))
            return true;

        var actual = Convert.ToHexString(SHA256.HashData(cert.RawData));
        return string.Equals(actual, pinned, StringComparison.OrdinalIgnoreCase);
    }

    private HttpRequestMessage Build(HttpMethod method, string path)
    {
        var s = _settings.Current;
        var req = new HttpRequestMessage(method, $"{s.BaseUrl}{path}");
        if (!string.IsNullOrWhiteSpace(s.ApiKey))
            req.Headers.Authorization = new("Bearer", s.ApiKey);
        return req;
    }

    public async Task<HealthDto?> GetHealthAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(Build(HttpMethod.Get, "/api/health"), ReadTimeout, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<HealthDto>(JsonOptions, ct);
    }

    public async Task<SystemMetricsDto?> GetMetricsAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(Build(HttpMethod.Get, "/api/system/metrics"), ReadTimeout, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SystemMetricsDto>(JsonOptions, ct);
    }

    public async Task<List<ServiceStatusDto>> GetServicesAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(Build(HttpMethod.Get, "/api/services"), ReadTimeout, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<ServiceStatusDto>>(JsonOptions, ct) ?? new();
    }

    public async Task<ServiceActionResultDto?> ControlServiceAsync(
        string key, ServiceControlAction action, CancellationToken ct = default)
    {
        var req = Build(HttpMethod.Post, $"/api/services/{key}/{action}");
        using var resp = await SendAsync(req, ControlTimeout, ct);
        return await resp.Content.ReadFromJsonAsync<ServiceActionResultDto>(JsonOptions, ct);
    }

    public async Task<SystemPowerResultDto?> PowerAsync(
        SystemPowerAction action, int delaySeconds, CancellationToken ct = default)
    {
        var req = Build(HttpMethod.Post, "/api/system/power");
        req.Content = JsonContent.Create(new SystemPowerRequest
        {
            Action = action,
            DelaySeconds = delaySeconds,
            Confirm = true
        }, options: JsonOptions);
        using var resp = await SendAsync(req, ControlTimeout, ct);
        return await resp.Content.ReadFromJsonAsync<SystemPowerResultDto>(JsonOptions, ct);
    }

    public async Task<ArgusDataDto?> GetArgusAsync(CancellationToken ct = default)
    {
        using var resp = await SendAsync(Build(HttpMethod.Get, "/api/argus"), ReadTimeout, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<ArgusDataDto>(JsonOptions, ct);
    }
}
