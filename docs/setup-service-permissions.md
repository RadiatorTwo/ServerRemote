# Setting Up the ServerRemote Service with Sufficient Permissions

For the ServerRemote service to **control other Windows services** (start/stop/restart)
and to perform **power actions** (shutting down/restarting the machine), it must run in
a context with the appropriate permissions. This guide shows the recommended setup as well
as a variant following the principle of least privilege.

## Why elevated permissions at all?

| Function | Code | Required permission |
| --- | --- | --- |
| Service start/stop/restart | `ServiceController.Start/Stop` (`WindowsServiceController.cs`) | Access (`SERVICE_START` / `SERVICE_STOP`) to the respective target service |
| Shutdown / restart | `shutdown.exe /s` or `/r` (`SystemPowerService.cs`) | `SeShutdownPrivilege` |

A normal, **non-elevated** user process has none of these accesses — which is why
"nothing happens" when you trigger start/stop from the app while the service is running as a
console, e.g. via `dotnet run` without admin rights.

---

## Recommended: As a Windows service under `LocalSystem`

The **`LocalSystem`** account has, by default, both the `SeShutdownPrivilege` and the
accesses needed to control other services. The bundled installation script sets up exactly
that.

### Installation

In an **administrator PowerShell** (right-click → "Run as administrator"):

```powershell
cd C:\!Code2\ServerRemote
[Environment]::SetEnvironmentVariable("ServerRemote__ApiKey", "my-secret-key", "Machine")
.\scripts\install-service.ps1
```

The script:
- publishes the service to `C:\ProgramData\ServerRemote\Service`,
- registers it via `sc.exe create … obj= LocalSystem start= auto`,
- starts the service.

The API key is not set by the script — set it separately as the machine environment
variable `ServerRemote__ApiKey` beforehand (as shown above).

With this, both service control **and** power actions work without any further configuration.

### Checking which account the service runs under

```powershell
sc.exe qc ServerRemoteService
# The "SERVICE_START_NAME" line must show "LocalSystem"
```

or:

```powershell
Get-CimInstance Win32_Service -Filter "Name='ServerRemoteService'" |
  Select-Object Name, StartName, State, StartMode
```

### Testing functionality

```powershell
# Status of the monitored services
curl -k -H "Authorization: Bearer my-secret-key" https://localhost:9443/api/services

# Example: stop / start MSSQL (key from appsettings.json)
curl -k -X POST -H "Authorization: Bearer my-secret-key" https://localhost:9443/api/services/mssql/stop
curl -k -X POST -H "Authorization: Bearer my-secret-key" https://localhost:9443/api/services/mssql/start
```

> The services to be controlled must be entered in `src/ServerRemote.Service/appsettings.json`
> under `ServerRemote:MonitoredServices` with `"Controllable": true` and the correct
> `WindowsServiceName`.

---

## Opening the firewall for LAN access

So that the app can reach the service from the LAN, open the HTTPS port (default **9443**) —
in an administrator PowerShell:

```powershell
New-NetFirewallRule -DisplayName "ServerRemote (9443/TCP)" `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 9443
```

(Adjust the port to `ServerRemote:Network:HttpsPort` if necessary.)

---

## Development: Starting the console **elevated**

When testing locally without installing the service, `dotnet run` runs in the context of the
logged-in user. For working service control/power actions, open PowerShell **as administrator**
and start it there:

```powershell
$env:ServerRemote__ApiKey = "test-key-123"
dotnet run --project src/ServerRemote.Service
```

Without elevation, `Start/Stop` fail with "Access denied" and `shutdown.exe` fails due to the
missing privilege. Thanks to the prior error handling, the message now appears visibly in the app
(status bar) instead of being silently swallowed.

---

## Alternative (advanced): Least privilege instead of `LocalSystem`

If you don't want to run the service as the all-powerful `LocalSystem`, you can use a
**dedicated account with minimal permissions** and grant it only the accesses it specifically
needs. This is considerably more involved and only worthwhile when security requirements are
elevated.

### 1. Create a service account

```powershell
# Local account (choose a secure password)
$pw = Read-Host -AsSecureString "Password for svc-serverremote"
New-LocalUser -Name "svc-serverremote" -Password $pw -PasswordNeverExpires `
  -Description "ServerRemote service account"
```

### 2. Grant the "Log on as a service" right

In `secpol.msc` → *Local Policies* → *User Rights Assignment* →
**"Log on as a service"**, add the `svc-serverremote` account (or use a tool such as
`ntrights`/the `carbon` PowerShell module).

### 3. Grant the "Shut down the system" right

Also in `secpol.msc` under *User Rights Assignment* →
**"Shut down the system"** (`SeShutdownPrivilege`), add the account. Without this
right, the power actions fail.

### 4. Grant start/stop rights per target service

For **each** controllable service (e.g. `MSSQLSERVER`), the account must be granted the right
to start/stop it. This is done via the service's security descriptor with `sc sdset`.
Procedure:

```powershell
# Read out and back up the current descriptor
sc.exe sdshow MSSQLSERVER
```

Add an ACE to the SDDL string that is output, granting the service account
`RP` (start), `WP` (stop), and `LC`/`RC` (read status) — using the account's **SID**:

```powershell
# Determine the SID of the service account
(New-Object System.Security.Principal.NTAccount("svc-serverremote")
  ).Translate([System.Security.Principal.SecurityIdentifier]).Value
```

Insert an ACE of the form `(A;;RPWPCR;;;<SID>)` into the `D:` part of the existing SDDL and
write it back (use the **complete**, augmented string):

```powershell
sc.exe sdset MSSQLSERVER "<complete-augmented-SDDL-string>"
```

> ⚠️ Faulty SDDL strings can render a service uncontrollable. Always back up the original
> value from `sc sdshow` beforehand. Repeat this step for **every** monitored service.

### 5. Switch the service to the account

```powershell
sc.exe config ServerRemoteService obj= ".\svc-serverremote" password= "YOUR_PASSWORD"
sc.exe stop  ServerRemoteService
sc.exe start ServerRemoteService
```

---

## Troubleshooting

| Symptom | Cause / Solution |
| --- | --- |
| Start/stop "does nothing", app shows "Access denied" | The service isn't running as `LocalSystem`/elevated. Use the recommended installation or set permissions as described in the least-privilege section. |
| Power action fails with a privilege error | The service account is missing `SeShutdownPrivilege` (always present with `LocalSystem`). |
| Service doesn't start after the account switch | The account is missing the "Log on as a service" right, or the password in `sc config` is wrong. |
| `api/services` shows "Not installed" | Wrong `WindowsServiceName` in `appsettings.json`, or the service doesn't exist on the server. |
| App can't reach the server | The firewall rule for port 9443 is missing, or `BindAddress`/`HttpsPort` is wrong. |

---

## Quick reference (recommended path)

```powershell
# As administrator
cd C:\!Code2\ServerRemote
[Environment]::SetEnvironmentVariable("ServerRemote__ApiKey", "my-secret-key", "Machine")
.\scripts\install-service.ps1

# Open the firewall
New-NetFirewallRule -DisplayName "ServerRemote (9443/TCP)" `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 9443

# Check the account (must be LocalSystem)
sc.exe qc ServerRemoteService
```
