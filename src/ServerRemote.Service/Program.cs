using System.Net;
using System.Reflection;
using ServerRemote.Contracts;
using ServerRemote.Service.Configuration;
using ServerRemote.Service.Security;
using ServerRemote.Service.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Runnable as a Windows service (autostart, LocalSystem) ---
builder.Host.UseWindowsService(o => o.ServiceName = "ServerRemoteService");

// --- Serilog: file + Windows Event Log ---
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "serverremote-.log"),
        rollingInterval: RollingInterval.Day)
    .WriteTo.EventLog("ServerRemote", manageEventSource: false));

// --- Configuration ---
builder.Services.AddOptions<ServerRemoteOptions>()
    .Bind(builder.Configuration.GetSection(ServerRemoteOptions.SectionName));

var options = builder.Configuration.GetSection(ServerRemoteOptions.SectionName).Get<ServerRemoteOptions>()
              ?? new ServerRemoteOptions();

// --- Kestrel with HTTPS ---
builder.WebHost.ConfigureKestrel(kestrel =>
{
    var bindIp = IPAddress.TryParse(options.Network.BindAddress, out var ip) ? ip : IPAddress.Any;
    kestrel.Listen(bindIp, options.Network.HttpsPort, listen =>
    {
        using var bootstrapFactory = LoggerFactory.Create(b => b.AddConsole());
        var bootstrapLogger = bootstrapFactory.CreateLogger("Cert");
        var cert = DevelopmentCertificate.LoadOrCreate(
            options.Certificate.PfxPath, options.Certificate.PfxPassword, bootstrapLogger);
        listen.UseHttps(cert);
    });
});

// --- JSON: (de)serialize enums as readable strings ---
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// --- Services (DI) ---
builder.Services.AddSingleton<IMetricsService, MetricsService>();
builder.Services.AddSingleton<IWindowsServiceController, WindowsServiceController>();
builder.Services.AddSingleton<ISystemPowerService, SystemPowerService>();
builder.Services.AddSingleton<IArgusService, ArgusService>();

var app = builder.Build();

app.UseMiddleware<ApiKeyMiddleware>();

string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

// === Endpoints ===

// Open — reachability check
app.MapGet("/api/health", () => Results.Ok(new HealthDto
{
    Status = "ok",
    Version = version,
    Hostname = Environment.MachineName,
    UptimeSeconds = (long)(Environment.TickCount64 / 1000),
    ServerTimeUtc = DateTimeOffset.UtcNow
}));

// Protected
app.MapGet("/api/system/metrics", (IMetricsService metrics) => Results.Ok(metrics.GetMetrics()));

app.MapGet("/api/services", (IWindowsServiceController svc) => Results.Ok(svc.GetAllStatus()));

app.MapPost("/api/services/{key}/{action}", async (
    string key, string action, IWindowsServiceController svc, CancellationToken ct) =>
{
    if (!Enum.TryParse<ServiceControlAction>(action, ignoreCase: true, out var parsed))
        return Results.BadRequest(new { error = $"Unknown action '{action}'." });

    var result = await svc.ControlAsync(key, parsed, ct);
    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/system/power", (SystemPowerRequest request, ISystemPowerService power) =>
{
    var result = power.Schedule(request);
    return result.Scheduled ? Results.Ok(result) : Results.BadRequest(result);
});

// Argus Monitor — sensor data from shared memory
app.MapGet("/api/argus", (IArgusService argus) => Results.Ok(argus.GetSnapshot()));

app.Run();
