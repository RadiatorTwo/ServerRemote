# ServerRemote

Home/LAN server remote management built from two .NET 10 applications plus a shared
contracts library and a tray companion.

## Projects

| Project | Type | Purpose |
| --- | --- | --- |
| `src/ServerRemote.Contracts` | classlib (net10.0) | Shared DTOs/enums between service and app |
| `src/ServerRemote.Service` | ASP.NET Core / Windows service (net10.0-windows) | HTTPS API, metrics, service control, power actions |
| `src/ServerRemote.Tray` | WinForms (net10.0-windows) | NotifyIcon companion: status display, service restart |
| `src/ServerRemote.App` | .NET MAUI (Windows + Android) | Dashboard for monitoring & control |

## API endpoints

| Method | Path | Auth | Description |
| --- | --- | --- | --- |
| GET | `/api/health` | open | Liveness, version, hostname, uptime |
| GET | `/api/system/metrics` | Bearer | CPU, RAM, disk usage |
| GET | `/api/services` | Bearer | Status of the configured services |
| POST | `/api/services/{key}/{start\|stop\|restart}` | Bearer | Control a service |
| POST | `/api/system/power` | Bearer | Shutdown/restart (`confirm:true` required) |
| GET | `/api/argus` | Bearer | Argus Monitor data (placeholder) |

Auth: header `Authorization: Bearer <ApiKey>`. HTTPS with a self-signed certificate
(generated on first start; the SHA-256 fingerprint is logged — it can be entered in the app
for pinning).

## Configuration (service)

`src/ServerRemote.Service/appsettings.json`, section `ServerRemote`:
- `ApiKey` — **do not commit in clear text**; during development use User Secrets or the
  `ServerRemote__ApiKey` environment variable, in production use a machine environment variable.
- `Network.HttpsPort` (default 9443), `Network.BindAddress` (`0.0.0.0` for LAN).
- `Certificate.PfxPath` / `PfxPassword` — empty = self-signed.
- `MonitoredServices` — list of `Key`, `DisplayName`, `WindowsServiceName`, `Controllable`.

## Quick start (development)

```powershell
# Run the service locally as a console app
$env:ServerRemote__ApiKey = "test-key-123"
dotnet run --project src/ServerRemote.Service

# Test from a second terminal
curl -k https://localhost:9443/api/health
curl -k -H "Authorization: Bearer test-key-123" https://localhost:9443/api/system/metrics
```

Run the app (Windows):
```powershell
dotnet build src/ServerRemote.App -f net10.0-windows10.0.19041.0
# then enter host/port/API key in the app under "Settings"
```

## Install as a Windows service (production)

In an **administrator PowerShell**:

```powershell
# 1. Publish the service
dotnet publish src/ServerRemote.Service -c Release

# 2. Set the API key as a machine environment variable
[Environment]::SetEnvironmentVariable("ServerRemote__ApiKey", "my-secret-key", "Machine")

# 3. Register and start the service (run from the publish output folder,
#    or copy install-service.ps1 next to ServerRemote.Service.exe)
.\scripts\install-service.ps1
```

See [docs/setup-service-permissions.md](docs/setup-service-permissions.md) for the full setup,
required privileges, firewall rule, and a least-privilege alternative.

## Building the Android APK

See [docs/android-apk.md](docs/android-apk.md) for signing setup and building a signed
release APK.

## License

Released under the [MIT License](LICENSE).

## Open phases

- **Argus Monitor**: shared-memory integration in the service (`/api/argus`) — adopt the
  struct layout from the official Argus Monitor documentation.
- **Backlog**: SignalR live push, threshold alerts/push notifications, Wake-on-LAN, top
  processes, API versioning/rate limiting, MSI installer.
