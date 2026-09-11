# RTSPView

RTSPView (formerly SpotMonitor) is a Windows viewer for RTSP streams: security cameras, encoder feeds, rebroadcasts, composite feeds, and other compatible RTSP sources. Arrange up to 16 main streams into configurable layouts, add picture-in-picture overlays, and manage playback through a web dashboard. It supports hardware decoding through LibVLC and automatic stream recovery. Controller supervises the WPF Viewer; both run as the signed-in Windows user.

## Install and first setup

Use Windows 10/11 x64 with a current graphics driver. Download the installer and checksum from this repository's Releases page, verify the checksum, and run the installer. Installation requires elevation; normal operation should use a standard Windows account.

### First time on a new host

1. Install and launch RTSPView on the Windows computer that will display the streams. Complete setup **on that computer first**, not through its LAN IP address.
2. Open **http://127.0.0.1:5080** in a browser on that computer, or use the **Web configuration** shortcut. If the viewer covers the browser, minimize it or exit full screen using five clicks in the upper-right corner within three seconds; exiting full screen alone does not disable always-on-top.
3. Sign in with the initial password **`admin`**. The account name is also `admin`, but the login form asks only for the password.
4. On the required password-change screen, enter **`admin` in Current password**, and enter your chosen replacement in **New password**. In version **1.0.33 and later**, there is no minimum length: the replacement must be nonblank, at most 1024 characters, different from the current password and not `admin`. Choose a password that is difficult to guess, especially before enabling LAN access.
5. Submit the change, then **sign in again with your new password**. The initial password stops working and earlier sessions are revoked. No readable administrator password file is created. All administration remains blocked until the password change succeeds.
6. Add your RTSP sources under **Cameras**, configure a layout and apply it. For administration from another computer or phone, follow [Enable LAN access](#access-the-admin-panel-from-the-lan) below; installation alone leaves administration local-only.

**Upgrading an existing host:** use your existing administrator password, not `admin`. If a password change is required, enter that existing password in Current password. If version 1.0.32 rejects your desired password or leaves you stuck in setup, manually install [the latest stable release](https://github.com/GalacticaActual75/RTSPView/releases/latest) over it before retrying. The password-policy fix is in 1.0.33; your existing streams/settings are retained.

**Fresh installations have no camera URLs configured**, including every main camera, legacy camera field and overlay. Enter your own URLs after setup. Existing installations retain their streams and password, but legacy security state requires a password change after login. Back up before upgrading.

The dashboard starts local-only on TCP 5080. After initial setup, use **System → LAN access** to enable HTTP access on your trusted private LAN. HTTPS is optional; see the LAN instructions below.

## Access the admin panel from the LAN

**Version 1.0.34 adds a built-in LAN switch. No certificates, environment variables or manual firewall commands are needed for normal trusted-LAN use.**

1. On the RTSPView host, open **http://127.0.0.1:5080**, sign in, and finish the required first-time password change.
2. Open **System → LAN access**, select **Enable LAN access**, and click **Save LAN access**.
3. Accept the Windows administrator approval prompt **on the RTSPView host**. If asked, set that host's trusted connection to **Private** under Windows Settings → Network & Internet → your connection's properties, then try again. The app does not automatically mark an unfamiliar network as trusted.
4. Wait a few seconds for the listener to update. The System tab lists clickable **`http://HOST-IP:5080`** addresses. Open one from another computer or phone on the same LAN and sign in with your admin password. Use **HTTP and port 5080** for this mode, not HTTPS or port 5081.

The switch saves its setting in `lan-access.json` alongside `settings.json`, allows the host's current LAN IP addresses/hostname, and configures a Windows firewall rule for **Private networks and LocalSubnet only**. The application also rejects off-subnet connections in this mode. Local access stays available, and the Viewer/streams do not restart. A brief browser disconnect while the web listener changes is normal. Approval cancelled or firewall setup failed? LAN access stays off and the panel explains the error.

**HTTP is unencrypted. Use this option only on a trusted private LAN; do not port-forward the admin panel to the Internet.** HTTPS remains available as an [optional advanced setup](docs/https-administration.md). If custom `ASPNETCORE_URLS` or Kestrel endpoints are already configured, the switch is disabled with an explanation; remove those custom bindings and restart the Controller to return to the built-in switch.

To turn LAN access off, clear the switch and save. No administrator prompt is needed for disabling: the listener returns to loopback and remote requests are blocked. A remote browser will lose access; re-enable locally on the host if needed. The scoped firewall rule remains dormant for the next enable and is removed on uninstall.

If another device cannot connect, check that both devices are on the same LAN, the host's network profile is Private, and Wi-Fi client/guest isolation is not blocking them. Use an address shown in the panel; `localhost` and `127.0.0.1` on your phone/laptop refer to that device, not the RTSPView host. VPNs or multiple adapters may produce several addresses—choose the one reachable from your device. Update installation still requires Windows approval on the host.

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

Use **Restart stream** on a camera for a single-feed problem, **Restart all cameras** for all feeds, or **Restart viewer** for the display process. **Reboot host** restarts the entire host. The dashboard also shows health information and recent logs. Browser thumbnails and layout/overlay previews are snapshots, not full-motion browser video.

**Show camera names** and **Show stream stats** control the information drawn over camera tiles. If hidden, diagnostic information appears during connection trouble and remains visible for 15 seconds after recovery. These text/status overlays are separate from picture-in-picture video overlays.

If startup supervision was enabled during installation, RTSPView starts at sign-in and the Controller relaunches a missing Viewer. Closing only the viewer can therefore cause it to return. To stop the installed wall deliberately, run the **Stop RTSPView** shortcut with administrator rights; it stops supervision and the camera processes until a manual start or the next sign-in.


## Configuration and persistent data

The main configuration file is `settings.json`:

- **Existing SpotMonitor installations:** `%LOCALAPPDATA%\SpotMonitor\settings.json`
- **Fresh RTSPView installations:** `%LOCALAPPDATA%\RTSPView\settings.json`

Paste the applicable path into File Explorer's address bar on the computer running RTSPView, signed in as the Windows user that runs the application. `%LOCALAPPDATA%` belongs to that user, so another Windows account has a different folder.

If set, `RTSPVIEW_DATA_DIR` overrides the default folder; the older `SPOTMONITOR_DATA_DIR` variable is also supported as a fallback. The same folder contains LAN access state (`lan-access.json`), password state (`web-security.json`), logs, backups and cookie-protection keys. **Keep the folder private: stream credentials are stored in the settings.** Stop both processes before manually editing or backing up these files.

No environment variables are required. `.env.example` is a reference; the application does **not** automatically load `.env` files. Set variables in the Windows user environment and restart both processes (sign out/in for scheduled startup).

| Variable | Default | Purpose |
| --- | --- | --- |
| `RTSPVIEW_DATA_DIR` | `%LOCALAPPDATA%\RTSPView` (existing installations retain SpotMonitor data) | Settings, password hash, logs, thumbnails, update staging and cookie keys. Use a private absolute directory. |
| `RTSPVIEW_GITHUB_REPOSITORY` | `GalacticaActual75/RTSPView` | Public GitHub release repository. No token/key is required or supported. |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | Advanced binding override; disables the built-in LAN switch. HTTPS also requires certificate configuration. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Host allowlist for externally configured bindings. The built-in LAN switch manages its own local hostname/IP allowlist. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Keep deployments in Production. |

The same data-directory setting must reach Controller and Viewer. Camera URLs and camera credentials are runtime configuration in `settings.json`, outside source control. Restrict this directory to the application user and administrators and protect backups with disk encryption. Administrator passwords use salted PBKDF2-HMAC-SHA256 with 600,000 iterations; cookie keys use Windows DPAPI for the current user. Camera credentials remain plaintext locally because LibVLC needs them at runtime.

Composite stream compatibility enables TCP, a 3000 ms buffer, and disables low-latency tuning for rebroadcast streams. It works with any configured host. Upgrading users who relied on automatic host-specific behavior should enable this checkbox for their composite streams.

### Correct the old update-channel label without reinstalling

Versions 1.0.32 and 1.0.33 already check public GitHub Releases, but their dashboard description still says "private LAN update channel." This is stale text, not the active update source. **Version 1.0.34 includes the wording correction; its users do not need this patch.**

For an existing installation, download [Apply-GitHub-Update-Label-Fix.ps1](https://github.com/GalacticaActual75/RTSPView/releases/download/v1.0.33/Apply-GitHub-Update-Label-Fix.ps1) from the existing 1.0.33 release. On the RTSPView host, open PowerShell **as administrator** and run the downloaded file, substituting its actual path:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "C:\path\to\Apply-GitHub-Update-Label-Fix.ps1"
```

The patch finds the existing installation through its uninstall registration. If it cannot, add `-InstallDirectory "C:\path\to\your\installation"`. It backs up the original page outside the web folder and replaces only the obsolete sentence. Refresh the dashboard with **Ctrl+F5** afterward; no restart is required. The patch does not alter settings, credentials, LAN access or updater behavior, and it does not install automatically through Check for updates. Existing installer assets and hashes are unchanged; no new release/version was created for this wording fix.

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
dotnet run --project tests/SpotMonitor.LanAccessChecks -c Release
node tests/lan-firewall.checks.cjs
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

- Dashboard unavailable: open it on the same host, verify Controller is running, and check bindings and port conflicts. Enable LAN access from System for a trusted private LAN; see the LAN guide above.
- Blank cameras: fresh installations intentionally have empty URLs. Check credentials, RTSP reachability, transport, codecs, and layout assignments.
- Update unavailable: check Internet connectivity, GitHub rate limits, repository visibility, manifest and checksum. Manual installer upgrades remain available.
- Configuration recovery: preserve the data directory before inspecting `settings.json.bak` or pre-import backups. Do not share raw settings, screenshots or logs in bug reports.
- Run both processes as the same user; named pipes restrict connections to that user. Local administrators and same-user processes remain trusted.

See the [bridge release report](docs/bridge-release.md) for publication status and remaining validation limits. The [initial release audit](docs/release-readiness.md) and [history cleanup procedure](docs/history-cleanup.md) retain the detailed audit record.
