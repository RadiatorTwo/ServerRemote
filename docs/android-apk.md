# Android APK: Setting Up Signing & Building the APK

This guide describes how to build the **ServerRemote.App** (`src/ServerRemote.App`) as a
signed release APK. Signing is already preconfigured in the `ServerRemote.App.csproj`
(Release block) — only the **keystore** and the **password** are missing.

## Overview: What's already set up?

In `src/ServerRemote.App/ServerRemote.App.csproj`, the following applies for **Release + Android**:

| Setting | Value |
| --- | --- |
| Package format | `apk` (`AndroidPackageFormat`) |
| Keystore path | `src/ServerRemote.App/serverremote.keystore` |
| Key alias | `serverremote` |
| Store & key password | from the MSBuild property `AndroidKeystorePassword` |
| App ID | `com.radiatortwo.serverremote` |
| Target Framework | `net10.0-android` |

> **Important:** The store password and the key password are both read from the same
> property `AndroidKeystorePassword` — so when creating the keystore, they must be
> **identical**.

The password is **deliberately not** stored in the project. It is passed during the build
via an environment variable or via `-p:AndroidKeystorePassword=...`.

---

## 1. Prerequisites (one-time)

- **.NET 10 SDK** (see `Directory.Build.props` / `global` version)
- **MAUI Android workload**:
  ```powershell
  dotnet workload install maui-android
  ```
- **JDK** (for `keytool`). Microsoft ships an OpenJDK with it; `keytool` is located, for
  example, under `C:\Program Files\Microsoft\jdk-<version>\bin\` or in the Android SDK
  under `…\jbr\bin\`. Check:
  ```powershell
  keytool -help
  ```
  If `keytool` is not in the PATH, call it with its full path.

---

## 2. Create the keystore (one-time)

The keystore is the app's **private signing key**. It is created **once** and then reused
for **every** release.

> ⚠️ **If the keystore is lost, no update APK can be published** that Android accepts as the
> same app. Keep it safe (back it up!) and never check it in.

Run this in the `src/ServerRemote.App` folder (replace the password `YOUR_PASSWORD`):

```powershell
keytool -genkeypair -v `
  -keystore serverremote.keystore `
  -alias serverremote `
  -keyalg RSA -keysize 2048 `
  -validity 10000 `
  -storepass YOUR_PASSWORD `
  -keypass  YOUR_PASSWORD `
  -dname "CN=ServerRemote, O=radiatortwo, C=DE"
```

- `-alias serverremote` and the file name `serverremote.keystore` **must** match the values
  in the `.csproj`.
- `-storepass` and `-keypass` **must be identical** (see the note above).
- `-validity 10000` ≈ 27 years of validity.

The `serverremote.keystore` file is excluded from the repo by `.gitignore` (`*.keystore`) —
this is intentional.

---

## 3. Provide the password

Set the password as an environment variable for the current PowerShell session:

```powershell
$env:AndroidKeystorePassword = "YOUR_PASSWORD"
```

Alternatively, pass it directly with the build call:
`-p:AndroidKeystorePassword=YOUR_PASSWORD` (see the next step).

> Do not write the password permanently into scripts or checked-in files.

---

## 4. Build the APK

**Always specify the app project explicitly** (from the repo root). Without a project path,
`dotnet` would otherwise try to build the entire solution for Android — Tray/Service/Contracts
have no `net10.0-android` target and fail with `NETSDK1005` / `NETSDK1136`.

```powershell
dotnet publish src/ServerRemote.App -f net10.0-android -c Release
```

With the password directly on the call (instead of the environment variable):

```powershell
dotnet publish src/ServerRemote.App -f net10.0-android -c Release -p:AndroidKeystorePassword=YOUR_PASSWORD
```

`dotnet publish` builds, signs, and zips the APK in a single step (Release default).

### Force a clean rebuild (`-t:Rebuild`)

> ⚠️ **After a failed build, always do a clean rebuild.** If signing fails (e.g. wrong
> password → `java.exe … code 2`), an **unsigned/broken** APK is left behind in the output
> folder. A subsequent build with the correct password may run *incrementally* and complete
> "successfully" within a few seconds **without repackaging the APK** — and then you keep
> installing the broken file (the installation aborts).

Instead of deleting `bin`/`obj` by hand, use the **Rebuild target** — it does `Clean` +
`Build` in one and thereby forces the APK to be repackaged **and** re-signed. (For Android,
`dotnet build -c Release` produces the signed `-Signed.apk` just like `publish`.)

```powershell
dotnet build src/ServerRemote.App -f net10.0-android -c Release -t:Rebuild -p:AndroidKeystorePassword=YOUR_PASSWORD
```

> 💡 **Sanity check:** A real rebuild takes ~1–2 minutes. If `publish`/`build` completes
> "successfully" within a few seconds, it was an incremental skip — in which case the APK was
> *not* re-signed.

Alternatives, if you want to stick with `publish` (`publish` has no `--no-incremental`):

```powershell
# Option A: clean explicitly beforehand
dotnet clean   src/ServerRemote.App -f net10.0-android -c Release
dotnet publish src/ServerRemote.App -f net10.0-android -c Release -p:AndroidKeystorePassword=YOUR_PASSWORD

# Option B: dotnet build supports the flag directly
dotnet build src/ServerRemote.App -f net10.0-android -c Release --no-incremental -p:AndroidKeystorePassword=YOUR_PASSWORD
```

### Verify the signature (recommended before distributing)

Use `apksigner` from the Android SDK build tools to verify that the APK is actually signed:

```powershell
$apksigner = "C:\Program Files (x86)\Android\android-sdk\build-tools\36.0.0\apksigner.bat"
& $apksigner verify --verbose src/ServerRemote.App/bin/Release/net10.0-android/com.radiatortwo.serverremote-Signed.apk
```

Expected output is `Verifies` with `v2 scheme … : true` (and v1/v3). If you instead see
`DOES NOT VERIFY` / `Missing META-INF/MANIFEST.MF`, the APK is unsigned — do a clean rebuild
(see the warning above).

### Result

The finished, **signed** APK is then located at:

```
src/ServerRemote.App/bin/Release/net10.0-android/com.radiatortwo.serverremote-Signed.apk
```

(The unsigned variant without `-Signed` is also in the same folder.)

---

## 5. Install the APK on a device

**Via USB with USB debugging enabled** (ADB from the Android SDK):

```powershell
adb install -r "bin/Release/net10.0-android/com.radiatortwo.serverremote-Signed.apk"
```

`-r` replaces an already installed version (only if signed with the **same** keystore).

**Manually:** Copy the `-Signed.apk` to the device and open it there. For this, "Install from
unknown sources" must be allowed for the relevant app (e.g. the file manager).

---

## 6. Bump the version (before every release)

In `src/ServerRemote.App/ServerRemote.App.csproj`:

```xml
<!-- User-visible version, e.g. "1.1" -->
<ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
<!-- Internal versionCode: MUST increase strictly monotonically with every update -->
<ApplicationVersion>1</ApplicationVersion>
```

- Increase `ApplicationVersion` (= Android `versionCode`) by at least 1 with **every**
  published build, otherwise Android refuses the update.
- `ApplicationDisplayVersion` (= `versionName`) is the version shown to users.

---

## Troubleshooting

| Symptom | Cause / Solution |
| --- | --- |
| `Keystore was tampered with, or password was incorrect` | Wrong `AndroidKeystorePassword`, or the store and key passwords differ. |
| `keytool` not found | JDK not in the PATH — call `keytool` with its full path (see step 1). |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` during `adb install` | The device has a version signed with a **different** keystore. Uninstall the old app first. |
| `java.exe … exited with code 2` (`MSB6006`) during signing | Usually a wrong keystore password. Check with `keytool -list -keystore … -storepass …`, then use the correct `AndroidKeystorePassword`. |
| Installation aborts / `DOES NOT VERIFY` / `Missing META-INF/MANIFEST.MF` | An **unsigned** APK from a previously failed build is being installed. Delete the Android release output and do a clean rebuild (see the warning in step 4). |
| `NETSDK1005` / `NETSDK1136` (Tray/Service/Contracts on Android) | `dotnet publish` called without a project path → solution build. Always use `dotnet publish src/ServerRemote.App …` with the project path. |
| APK is unsigned / no `-Signed.apk` | Not built in the `Release` configuration, or the build did not run for `net10.0-android`. |
| `error XA…: Please install the Android SDK` | The MAUI Android workload/SDK is missing — `dotnet workload install maui-android`. |

---

## Quick reference

```powershell
# One-time: create the keystore (in the src/ServerRemote.App folder)
keytool -genkeypair -v -keystore serverremote.keystore -alias serverremote `
  -keyalg RSA -keysize 2048 -validity 10000 `
  -storepass YOUR_PASSWORD -keypass YOUR_PASSWORD -dname "CN=ServerRemote, O=radiatortwo, C=DE"

# Per release: clean rebuild (forces Clean + Build, reliably re-signs the APK)
dotnet build src/ServerRemote.App -f net10.0-android -c Release -t:Rebuild -p:AndroidKeystorePassword=YOUR_PASSWORD

# Result:
#   src/ServerRemote.App/bin/Release/net10.0-android/com.radiatortwo.serverremote-Signed.apk
```
