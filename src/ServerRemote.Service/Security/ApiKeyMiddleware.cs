using Microsoft.Extensions.Options;
using ServerRemote.Service.Configuration;

namespace ServerRemote.Service.Security;

/// <summary>
/// Validates the "Authorization: Bearer &lt;key&gt;" header against the configured API key.
/// Paths in <see cref="_openPaths"/> (e.g. /api/health) remain open.
/// </summary>
public sealed class ApiKeyMiddleware
{
    private static readonly string[] _openPaths = { "/api/health" };

    private readonly RequestDelegate _next;
    private readonly string _apiKey;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<ServerRemoteOptions> options, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _apiKey = options.Value.ApiKey ?? string.Empty;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (_openPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogError("No API key configured — protected endpoints are blocked.");
            await Deny(context, "API key not configured on the server.");
            return;
        }

        string? provided = ExtractBearer(context.Request.Headers.Authorization.ToString());
        if (provided is null || !FixedTimeEquals(provided, _apiKey))
        {
            await Deny(context, "Invalid or missing API key.");
            return;
        }

        await _next(context);
    }

    private static string? ExtractBearer(string? header)
    {
        const string prefix = "Bearer ";
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        return header[prefix.Length..].Trim();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static async Task Deny(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = message });
    }
}
