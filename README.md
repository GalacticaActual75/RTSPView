# RTSPView

RTSPView (formerly SpotMonitor) is a Windows RTSP camera wall with up to 16 main cameras, configurable layouts, picture-in-picture overlays, hardware decoding through LibVLC, automatic stream recovery, and a web administration dashboard. Controller supervises the WPF Viewer; both run as the signed-in Windows user.

## Install and first setup

Use Windows 10/11 x64 with a current graphics driver. Download the installer and checksum from this repository's Releases page, verify the checksum, and run the installer. Installation requires elevation; normal operation should use a standard Windows account.

Open [local administration](http://127.0.0.1:5080) on the camera-wall computer. **The initial administrator password is `admin`. Change it immediately.** The account name is `admin`; the dashboard asks only for its password. All administration APIs and controls remain blocked until a different password of 12–1024 characters is saved. Sign in again with the new password. The initial password then stops working and earlier sessions are revoked. No readable administrator password file is created.

**Fresh installations have no camera URLs configured**, including every main camera, legacy camera field and overlay. Enter your own URLs after setup. Existing installations retain their streams and password, but legacy security state requires a password change after login. Back up before upgrading.

The dashboard listens on loopback TCP 5080 by default. Remote access is an explicit deployment choice: configure ASP.NET Core HTTPS with a trusted certificate before binding a LAN interface. `ASPNETCORE_URLS` controls bindings. HTTP LAN bindings transmit credentials and cookies without encryption. Do not expose them to the Internet. Complete initial setup locally. Firewall rules do not provide encryption.

## Configuration and persistent data

No environment variables are required. `.env.example` is a reference; the application does **not** automatically load `.env` files. Set variables in the Windows user environment and restart both processes (sign out/in for scheduled startup).

| Variable | Default | Purpose |
| --- | --- | --- |
| `RTSPVIEW_DATA_DIR` | `%LOCALAPPDATA%\RTSPView` (existing installations retain SpotMonitor data) | Settings, password hash, logs, thumbnails, update staging and cookie keys. Use a private absolute directory. |
| `RTSPVIEW_GITHUB_REPOSITORY` | `GalacticaActual75/RTSPView` | Public GitHub release repository. No token/key is required or supported. |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | Controller bindings. HTTPS additionally requires ASP.NET Core certificate configuration. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Semicolon-separated permitted request hostnames/IPs. Add the exact LAN name when enabling remote access; wildcard hosts are not accepted. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Keep deployments in Production. |

The same data-directory setting must reach Controller and Viewer. Camera URLs and camera credentials are runtime configuration in `settings.json`, outside source control. Restrict this directory to the application user and administrators and protect backups with disk encryption. Administrator passwords use salted PBKDF2-HMAC-SHA256 with 600,000 iterations; cookie keys use Windows DPAPI for the current user. Camera credentials remain plaintext locally because LibVLC needs them at runtime.

Composite stream compatibility enables TCP, a 3000 ms buffer, and disables low-latency tuning for rebroadcast streams. It works with any configured host. Upgrading users who relied on automatic host-specific behavior should enable this checkbox for their composite streams.

## Backup, upgrade and recovery

Stop both processes before copying the entire data directory to a protected backup. Thumbnails, logs, settings and automatic backups can contain private information. DPAPI cookie keys are tied to the Windows account and are not portable login credentials.

Web configuration export removes URL user information, query strings and fragments, but retains camera names, hosts and paths. Keep exports private and re-enter camera credentials after import. A full private data-directory backup preserves credentials. Import saves a backup before replacing settings.

Install a newer release over the existing installation; settings are preserved. Back up before switching channels or downgrading, because older versions may not understand newer schemas. Restore a compatible pre-upgrade backup when rolling back.

Updates now come from the public [RTSPView GitHub Releases](https://github.com/GalacticaActual75/RTSPView/releases). Stable uses GitHub's latest stable release; Beta selects the highest published beta version and excludes drafts/stable releases. Release metadata is cached for two minutes to reduce unauthenticated API traffic. The installer is downloaded over HTTPS, with restricted redirects, size limits and SHA-256 verification against the release manifest and GitHub asset digest when available. No GitHub token is needed, stored or sent. The elevated helper verifies the checksum again. A compromised release-publisher account is still trusted; protect GitHub maintainers with strong authentication and consider publisher signing.

The final SMB bridge releases are 1.0.32 (Stable) and 1.0.32-beta.1 (Beta). Install either from the old channel once; all later update checks use GitHub. Existing AppId, executable names, IPC and scheduled-task identifiers remain compatible with the old updater, so upgrades happen in place. New installations use RTSPView branding and its default data directory; existing settings and update-channel selection remain in the legacy directory. The old data-directory environment variable remains a compatibility alias. Complete any required administrator password change on the camera-wall host; administration now defaults to localhost.

For forgotten administrator passwords, stop both processes and, as the owning Windows user, move `web-security.json` and the `data-protection` directory into a private backup outside the active data directory. Restart locally and complete setup with `admin`. Camera settings remain intact. Do not perform recovery while the dashboard is reachable by untrusted users. Normal login throttling clears after five minutes; five attempts are allowed across the administrator account per window.

## Build and tests

Prerequisites: Windows x64, .NET 8 SDK with current servicing patches, Node.js, and Inno Setup 6 for installers. Releases are self-contained, so runtime security updates require rebuilding and installing a new release.

```powershell
dotnet restore SpotMonitor.sln -r win-x64
dotnet build SpotMonitor.sln -c Release --no-restore
dotnet run --project tests/SpotMonitor.ConfigurationChecks -c Release
$env:DOTNET_HOST_PATH = (Get-Command dotnet).Source
node tests/admin-security.checks.cjs
node tests/shape-editor.checks.cjs
node tests/wall-layout-presets.checks.cjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/UpdateHelper.Checks.ps1
dotnet run --project tests/SpotMonitor.LayoutVisibilityChecks -c Release
dotnet run --project tests/SpotMonitor.OpacityChecks -c Release -- --auto
```

Rendering checks require an interactive Windows desktop and video support. The HTTP test uses an isolated directory and disables the watchdog. These are console checks; `dotnet test` does not execute them.

After committing and reviewing a release, use `tools/publish-release.ps1 -Version <version>`. GitHub Actions builds the applications and installer and uploads checksum/manifest assets. Rebuild from reviewed source; do not publish existing local build directories.

Docker is not supported: WPF requires an interactive Windows desktop and graphics stack. There are no Dockerfiles or container mounts to configure.

## Troubleshooting and security

- Dashboard unavailable: open it on the same host, verify Controller is running, and check bindings and port conflicts. Configure HTTPS and firewall access deliberately for remote use.
- Blank cameras: fresh installations intentionally have empty URLs. Check credentials, RTSP reachability, transport, codecs, and layout assignments.
- Update unavailable: check Internet connectivity, GitHub rate limits, repository visibility, manifest and checksum. Manual installer upgrades remain available.
- Configuration recovery: preserve the data directory before inspecting `settings.json.bak` or pre-import backups. Do not share raw settings, screenshots or logs in bug reports.
- Run both processes as the same user; named pipes restrict connections to that user. Local administrators and same-user processes remain trusted.

See the [release audit](docs/release-readiness.md) and [history cleanup procedure](docs/history-cleanup.md) before making the repository public. External releases, issue attachments, forks and hosting caches need separate review.
