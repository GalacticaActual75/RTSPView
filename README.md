# SpotMonitor

SpotMonitor is a production-oriented Windows RTSP camera wall with nine independently supervised streams, hardware-accelerated decoding, a full-screen appliance viewer, and an authenticated LAN administration dashboard.

## Features

- Fixed 3×3 native video wall with AMD, NVIDIA, and Intel hardware-decoding support through LibVLC/D3D11VA.
- Independent reconnect, stall detection, exponential backoff, and player recreation for every camera.
- Persistent camera naming, ordering, transport, cache, overlay, monitor, cursor, and always-on-top settings.
- Automatic connection-state overlays and per-camera feed thumbnails.
- LAN web administration with PBKDF2 password hashing, CSRF protection, login throttling, audit logs, and Private-network-only firewall configuration.
- Separate Controller watchdog that restores a failed Viewer.
- Self-contained Windows x64 deployment; the target computer does not need the .NET runtime installed.
- Web-triggered, SHA-256-verified updates from the private LAN channel at `UPDATE_CHANNEL_DIRECTORY`.
- Branded Windows executables, shortcuts, and web interface.

Settings, logs, password state, and thumbnails live under `%LOCALAPPDATA%\SpotMonitor`. Installing or upgrading the application does not remove them.

## Install or upgrade

Download `SpotMonitor-Setup-<version>-win-x64.exe` from the repository's **Releases** page and run it as administrator.

The installer uses a stable application identity and installation directory (`C:\Program Files\SpotMonitor`). Running a newer installer:

1. Stops the current Controller and Viewer.
2. Replaces the installed program files.
3. Preserves the existing camera layout and administrator configuration.
4. Refreshes the Private-LAN firewall rules and optional logon watchdog task.
5. Offers to launch the upgraded camera wall.

TCP 5080 is allowed only from Windows' `LocalSubnet`, regardless of whether the host NIC is classified Private or Public. The installer is currently unsigned, so Windows SmartScreen may show an unknown-publisher warning until a code-signing certificate is added.

## Build from source

Prerequisites: Windows 10/11 x64 and the .NET 8 SDK.

```powershell
dotnet restore SpotMonitor.sln
dotnet build SpotMonitor.sln -c Release
dotnet test tests/SpotMonitor.ConfigurationChecks/SpotMonitor.ConfigurationChecks.csproj -c Release
```

The projects are:

- `src/SpotMonitor.Viewer` — WPF/LibVLC camera wall.
- `src/SpotMonitor.Controller` — ASP.NET Core LAN dashboard and watchdog.
- `src/SpotMonitor.Core` — shared configuration and telemetry contracts.
- `src/SpotMonitor.Infrastructure` — atomic JSON persistence and rotating logs.
- `installer/SpotMonitor.iss` — upgrade-safe Inno Setup installer definition.
- `.github/workflows/release.yml` — GitHub Release build pipeline.

## Publish a release

Push a semantic version tag:

```powershell
.\tools\publish-release.ps1 -Version 1.0.0
```

GitHub Actions publishes both the installer and its SHA-256 checksum to a GitHub Release. The workflow can also be started manually from the repository's **Actions** page.

For LAN deployment, copy the generated installer and `update.json` from the release into `UPDATE_CHANNEL_DIRECTORY`. Installed hosts can then check and start the verified update from the web dashboard. Windows displays one elevation prompt on the camera-wall host before installation.

## Administration

The dashboard listens on TCP 5080. On first start, SpotMonitor creates a random administrator password and writes the one-time readable value to `%LOCALAPPDATA%\SpotMonitor\initial-admin-password.txt`. After the password is changed, the readable file is removed.

RTSP URLs are stored locally in plain JSON for this LAN-only deployment. Credentials are redacted from logs and sanitized configuration exports.
