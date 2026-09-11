# RTSPView

RTSPView (formerly SpotMonitor) is a Windows viewer for RTSP streams: security cameras, encoder feeds, rebroadcasts, composite feeds, and other compatible RTSP sources. Arrange up to 16 main streams into configurable layouts, add picture-in-picture overlays, and manage playback through a web dashboard. It supports hardware decoding through LibVLC and automatic stream recovery. Controller supervises the WPF Viewer; both run as the signed-in Windows user.

## Install and first setup

Use Windows 10/11 x64 with a current graphics driver. Download the installer and checksum from this repository's Releases page, verify the checksum, and run the installer. Installation requires elevation; normal operation should use a standard Windows account.

Open [local administration](http://127.0.0.1:5080) on the camera-wall computer. **The initial administrator password is `admin`. Change it immediately.** The account name is `admin`; the dashboard asks only for its password. All administration APIs and controls remain blocked until a different password of 12–1024 characters is saved. Sign in again with the new password. The initial password then stops working and earlier sessions are revoked. No readable administrator password file is created.

**Fresh installations have no camera URLs configured**, including every main camera, legacy camera field and overlay. Enter your own URLs after setup. Existing installations retain their streams and password, but legacy security state requires a password change after login. Back up before upgrading.

The dashboard listens on loopback TCP 5080 by default. Remote access is an explicit deployment choice: configure ASP.NET Core HTTPS with a trusted certificate before binding a LAN interface. `ASPNETCORE_URLS` controls bindings. HTTP LAN bindings transmit credentials and cookies without encryption. Do not expose them to the Internet. Complete initial setup locally. Firewall rules do not provide encryption.

## Access the admin panel from the LAN

LAN access is optional. By default, the admin panel is available only at `http://127.0.0.1:5080` on the RTSPView computer. On another computer or phone, `localhost` and `127.0.0.1` refer to that device, not the RTSPView host. Opening a firewall port alone does not enable remote access.

Complete initial setup and change the default password locally first. Then configure a trusted HTTPS endpoint using the steps below. These settings configure the Controller's web server; they do not belong in `settings.json` and are not loaded from a `.env` file.

1. Choose a stable LAN hostname for the RTSPView computer and make sure your other devices can resolve it to that computer's LAN address. A DHCP reservation can help keep the address stable. In the example below, **replace `rtspview.example` with your actual hostname**; it is only a placeholder.
2. Obtain a server certificate whose Subject Alternative Name includes that hostname, with its private key, from a certificate authority trusted by your client devices. A private CA works if its root is installed and trusted on those devices. Import the server certificate into **Current User → Personal → Certificates** for the Windows account running RTSPView (`certmgr.msc`). The certificate must be valid for server authentication and that account must be able to use its private key. The example assumes its subject contains the chosen hostname. Do not commit certificates/private keys or bypass browser certificate warnings.
3. Open PowerShell as that same Windows user and set the persistent user environment variables below. This retains local HTTP access on 5080 and adds HTTPS on 5081. `AllowedHosts` contains hostnames only, without schemes or ports. Replace both hostname placeholders before running:

```powershell
[Environment]::SetEnvironmentVariable('ASPNETCORE_URLS', 'http://127.0.0.1:5080;https://0.0.0.0:5081', 'User')
[Environment]::SetEnvironmentVariable('AllowedHosts', 'localhost;127.0.0.1;[::1];rtspview.example', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Subject', 'rtspview.example', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Store', 'My', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__Location', 'CurrentUser', 'User')
[Environment]::SetEnvironmentVariable('Kestrel__Certificates__Default__AllowInvalid', 'false', 'User')
```

The certificate-store configuration uses ASP.NET Core's [Kestrel HTTPS configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-8.0). No certificate password is needed in this example because the private key is accessed through the Windows certificate store. HTTPS startup fails if the configured certificate cannot be found or used.

4. On a trusted LAN, confirm the Windows network connection uses the **Private** profile. In an **administrator PowerShell** window, allow the example HTTPS port from the local subnet:

```powershell
New-NetFirewallRule -DisplayName 'RTSPView Admin HTTPS' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5081 -RemoteAddress LocalSubnet -Profile Private
```

The installer's existing port-5080 rule does not open port 5081. If you choose another HTTPS port, change both the binding and firewall rule. Routed management networks need a deliberately scoped source-subnet rule; the example only allows the local subnet. Do not add a router port-forward for the admin panel.

5. Sign out and back in so the application and startup task inherit the updated environment. If automatic startup is disabled, launch RTSPView after signing back in. Restarting only the Viewer does not restart the Controller or change its listener.
6. From another device on the permitted LAN, open **`https://rtspview.example:5081`**, substituting your hostname, and sign in with your changed admin password. Use that HTTPS address for remote administration. The local Web configuration shortcut still opens the loopback address. Update installation and other Windows elevation prompts still require approval on the RTSPView computer.

For access by IP address instead of hostname, the certificate must contain that IP as an IP Subject Alternative Name and the exact address must also appear in `AllowedHosts`. A certificate for a DNS name does not automatically validate an IP-address URL. Never browse to `0.0.0.0`: it is the listener binding, not the host's address.

If access fails:

- **Timeout/refused connection:** check Controller startup, the chosen port, firewall/network profile, DNS, and Wi-Fi client isolation. From another Windows computer, `Test-NetConnection rtspview.example -Port 5081` checks connectivity after substituting your hostname.
- **HTTP 400:** check the exact requested hostname/IP is listed in `AllowedHosts`, then restart with the updated environment.
- **Certificate warning:** check the hostname, expiry and issuing CA's trust on the client; correct the certificate/trust configuration rather than clicking through the warning.
- **Local access works but LAN access does not:** confirm the Controller inherited the HTTPS binding, not just the default loopback URL. HTTP access to the host's LAN address on 5080 remains disabled in this example.

To return to local-only access, set `ASPNETCORE_URLS` back to `http://127.0.0.1:5080` and `AllowedHosts` back to `localhost;127.0.0.1;[::1]` in the user environment, sign out/in, and remove the `RTSPView Admin HTTPS` firewall rule if no longer needed.


## What RTSPView does

RTSPView turns a Windows display into a wall of live RTSP video. The desktop Viewer plays the video; the browser dashboard configures the wall and shows status and snapshot previews. A source does not need to be a physical camera: any compatible RTSP video stream can fill a main slot or an overlay. Playback depends on the source codec and LibVLC support. RTSPView is intended for live viewing, not recording or NVR playback.

The dashboard currently calls stream slots **Cameras** and the display a **Camera wall**. These are interface labels, not restrictions on the source: substitute your encoder, rebroadcast or other RTSP feed wherever the guide says camera.

| Feature | What you can do |
| --- | --- |
| Main streams | Configure up to 16 RTSP streams with individual names, enable switches, transport, buffering and recovery settings. |
| Layout designer | Build landscape or portrait walls, choose presets, resize/rearrange tiles and keep up to 32 saved layouts. |
| Picture-in-picture | Show Doorbell, Garage and up to 14 additional overlay streams over selected camera tiles. These labels are defaults; use your own names and streams. |
| Overlay styling | Adjust shape, size, placement, opacity, zoom and pan; draw a custom mask or import supported SVG paths. |
| Dedicated display | Choose a monitor, launch full screen, keep the viewer on top and hide the idle mouse cursor. |
| Stream resilience | Request hardware decoding, reconnect failed or stalled streams, and restart individual streams or the whole viewer. Actual capacity depends on resolution, frame rate, codecs and hardware. |
| Administration | Check stream health, view logs, manage updates, change the admin password and export/import configuration. |

## Set up streams and layouts

1. Open **Cameras** after completing the initial password change. Enter a descriptive stream name and its RTSP URL, enable the slot, and choose **Save camera**. Use the URL supplied by your camera, encoder or RTSP server; no network discovery or preconfigured camera addresses are provided.
2. Use **Add camera** when you need more than the initial nine entries, up to 16. An enabled camera also needs an assignment in the active layout to appear on the wall.
3. Open **Layouts**, choose **Landscape · 16:9** or **Portrait · 9:16**, and pick a starting preset. Presets include Single, Split, Quad, Six, Nine, Sixteen, Featured, Sidebar, Cinema, Dual focus, Center stage and Strip.
4. Assign cameras, drag tiles to move or swap them, and use a tile's corner handle to resize it. The grid supports one to four rows and columns. Layout previews use snapshots; watch the desktop Viewer for live video.
5. Choose **Apply to wall** to save and display the draft. Edits stay in draft until applied. Under **Manage layout**, duplicate and name a layout to keep an alternative; **Save layout** saves an inactive layout without switching the wall. **Discard changes** abandons the draft. The active layout cannot be deleted.

Camera settings save per slot and apply live. Changing layout geometry or overlay placement does not require reinstalling the application.

## Always-on-top and full-screen behavior

**Keep viewer always on top is enabled by default.** In **System → Wall behavior**, clear **Keep viewer always on top** and choose **Save display settings** when you want to use other applications normally on the same monitor. While enabled, RTSPView periodically reasserts its topmost position, so a browser or another ordinary window can appear behind the camera wall even after you switch to it. This is intentional for a dedicated camera display.

Always-on-top and full-screen mode are separate settings. **Exit full screen** restores the window border and local controls, but does **not** turn off always-on-top. To work comfortably on the same computer, disable always-on-top and exit full screen. **Launch full screen** controls the saved behavior; later configuration reloads can restore that saved mode, so clear it as well if you want the viewer to stay windowed.

The System page provides immediate **Enter full screen** and **Exit full screen** commands. For a local escape from full screen, click the upper-right corner of the selected display **five times within three seconds**, then confirm. The click area is the upper-right 64 × 64 pixels. This gesture exits full screen; it does not stop the viewer or disable always-on-top. This is a display convenience, not a secure Windows kiosk lock.

**Preferred monitor** uses a zero-based display index: `0` selects the first display in Windows' enumerated list, `1` the next. Check the chosen screen after rearranging or reconnecting monitors. **Hide mouse cursor** hides the pointer after the configured idle time while in full screen; moving the pointer makes it available again.

Picture-in-picture overlays belong to the viewer and track their host tiles. They are hidden when the viewer is minimized, hidden or cloaked by Windows, and when their host tile is absent from the active layout. They are not independent desktop widgets.

## Picture-in-picture overlays

Open **Overlays**, select Doorbell or Garage, or use **+** to add another overlay. Each overlay has its own RTSP stream and enabled switch. Choose the main camera tile that will host it, configure the stream, adjust the preview and save. Overlay streams stay visible while enabled and their host tile is displayed; the Doorbell label does not imply an automatic doorbell-press trigger.

| Control | Effect |
| --- | --- |
| Host camera | Attaches the overlay to that camera's tile. Moving or resizing the host tile moves/scales the overlay with it. |
| Viewport width/height | Sets the visible overlay area as a percentage of the host tile. |
| Viewport horizontal/vertical position | Moves that area within the host tile; 0 is left/top and 100 is right/bottom within the available space. |
| Shape | Chooses Native, Square, Rounded square, Circle, Oval or a custom mask. |
| Opacity | Controls transparency from 20% to 100%; lower values let more of the underlying camera show through. |
| Zoom | Enlarges the video inside the viewport from 100% to 300%, preserving its aspect ratio. |
| Image horizontal/vertical position | Pans the video inside the viewport without moving the viewport itself. |

Place and size the viewport first, then zoom and pan the image to frame the area you want. For example, place a small circular doorbell view in the corner of a driveway tile, or place an encoder feed over a larger rebroadcast stream, then zoom and pan to frame the subject. Changing the mask or viewport position is different from moving the image inside it. Preview edits are applied to the camera wall when saved.

The custom shape editor lets you draw a mask. SVG imports must be 256 KB or smaller and define a valid `viewBox` and path geometry. Convert text/basic shapes to paths and flatten transforms in your SVG editor before importing. Imported paths define the video mask; this is not an arbitrary SVG artwork renderer. Custom-mask rotation is also available.

## Stream tuning and everyday controls

Start with the default streaming settings. For an unreliable connection, try TCP and increase **Cache (ms)** to trade latency for smoother playback. **Startup timeout**, **Stall timeout** and **Maximum backoff** control connection/recovery timing; **Low latency** changes playback tuning. **Composite stream compatibility** forces TCP with a 3000 ms buffer and disables low-latency tuning for rebroadcast/composite streams. More streams and larger resolutions increase network, decoder and graphics load.

Use **Restart stream** on a camera for a single-feed problem, **Restart all cameras** for all feeds, or **Restart viewer** for the display process. **Reboot Windows** restarts the entire host. The dashboard also shows health information and recent logs. Browser thumbnails and layout/overlay previews are snapshots, not full-motion browser video.

**Show camera names** and **Show stream stats** control the information drawn over camera tiles. If hidden, diagnostic information appears during connection trouble and remains visible for 15 seconds after recovery. These text/status overlays are separate from picture-in-picture video overlays.

If startup supervision was enabled during installation, RTSPView starts at sign-in and the Controller relaunches a missing Viewer. Closing only the viewer can therefore cause it to return. To stop the installed wall deliberately, run the **Stop RTSPView** shortcut with administrator rights; it stops supervision and the camera processes until a manual start or the next sign-in.


## Configuration and persistent data

The main configuration file is `settings.json`:

- **Existing SpotMonitor installations:** `%LOCALAPPDATA%\SpotMonitor\settings.json`
- **Fresh RTSPView installations:** `%LOCALAPPDATA%\RTSPView\settings.json`

Paste the applicable path into File Explorer's address bar on the computer running RTSPView, signed in as the Windows user that runs the application. `%LOCALAPPDATA%` belongs to that user, so another Windows account has a different folder.

If set, `RTSPVIEW_DATA_DIR` overrides the default folder; the older `SPOTMONITOR_DATA_DIR` variable is also supported as a fallback. The same folder contains password state (`web-security.json`), logs, backups and cookie-protection keys. **Keep the folder private: stream credentials are stored in the settings.** Stop both processes before manually editing or backing up these files.

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

See the [bridge release report](docs/bridge-release.md) for publication status and remaining validation limits. The [initial release audit](docs/release-readiness.md) and [history cleanup procedure](docs/history-cleanup.md) retain the detailed audit record.
