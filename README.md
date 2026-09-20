# RTSPView

[Buy me a coffee](https://buymeacoffee.com/galacticaactual75)—or help refill my Codex credits. I keep saying “one last little fix”. ☕

RTSPView is a Windows viewer for RTSP streams: security cameras, encoder feeds, rebroadcasts, composite feeds, and other compatible RTSP sources. Arrange up to 16 main streams into configurable layouts, add picture-in-picture overlays, and manage playback through a web dashboard. It supports hardware decoding through LibVLC and automatic stream recovery. Controller supervises the WPF Viewer; both run as the signed-in Windows user.

RTSPView is intended to be used alongside Scrypted, displaying its rebroadcast RTSP streams and optionally responding to its MQTT detection events. **Scrypted is not required:** you can use compatible RTSP streams directly from cameras, NVRs, or other sources without setting up MQTT automation.

## Current releases and feature availability

Release channels as of September 20, 2026:

| Channel | Published version | Availability |
| --- | --- | --- |
| Stable | [1.0.43](https://github.com/GalacticaActual75/RTSPView/releases/tag/v1.0.43) | Promotes the beta dashboard, native stream editor, expanded layout/framing controls, per-layout appearance and Scrypted connector support. Adds intentional viewer exit, manual resume, optional fullscreen hover exit and viewer taskbar identity fixes. |
| Beta | [1.0.44-beta.6](https://github.com/GalacticaActual75/RTSPView/releases/tag/v1.0.44-beta.6) | Adds direct Tapo H100/H200 discovery for T110 door sensors, sensor-controlled overlays and saved layouts, shared automation priorities, and custom overlay alignment across layouts. Live hub validation is pending. |

Use [latest stable](https://github.com/GalacticaActual75/RTSPView/releases/latest) for the stable installer or [all releases](https://github.com/GalacticaActual75/RTSPView/releases) for betas. Unless marked beta, this guide describes stable 1.0.43. Tapo integration and numeric rule priorities require 1.0.44-beta.1 or newer; they are not in stable 1.0.43.

### Tapo sensors and rule priority (beta)

Open **Automation → Tapo** to add both hubs, discover your T110, and view Open, Closed or Unavailable status. A rule can show an overlay while a door is open and hide it when closed, without changing the layout. Rules can also activate a standard wall layout or an automation template with selected focus cameras. Everything runs in Controller after the browser closes; Home Assistant and MQTT are not required for Tapo.

Manage MQTT and Tapo priority together in **Automation → Automation Priority**; 1 is highest. For example, a priority-1 doorbell detection interrupts a priority-2 garage view immediately, even when the garage rule holds its view. A lower-priority view resumes only while its condition or detection timer remains active. Manual camera focus stays above automation.

In **1.0.44-beta.3**, Automation has aligned **MQTT**, **Tapo** and **Priority** tabs with enable toggles. Use Priority to drag rules into one order, or use Move up/down, then Save order. The top rule receives priority 1. **Discover hubs on network** finds local H100/H200 candidates before sensor discovery. Both layout editors have **New layout** (blank or preset) and **Add stream**; new tiles default to Fit, preserving explicit sizing on existing layouts.

See [Tapo setup, overlay recipes and priority behavior](docs/tapo-automation.md), including polling latency, unavailable behavior, and beta hardware-validation limits.

In **1.0.44-beta.2**, custom overlay masks also follow their host camera's Fit/Fill/Stretch/Original framing, zoom and pan across layouts. The mask and overlay video transform together and stay clipped to the host tile. Existing 16:9 editor calibration is retained; no additional setting is required.

In **1.0.44-beta.4**, priority editing and guidance live in the Priority tab, and automation help uses the full panel width. **Quick actions** beside Sign out mirrors Maintenance. Restart viewer is disabled while stopped or starting; one full-screen button follows the viewer’s reported mode.

In **1.0.44-beta.5**, each MQTT and Tapo rule has an On/Off switch. Apply or save to persist it. Tapo discovery buttons and hub rows have clearer spacing. **Remove Tapo account** clears RTSPView’s saved email/password and disables Tapo automation while keeping saved hubs and rules.

In **1.0.44-beta.6**, **Restart Live View** restarts the camera wall, while **Restart Application** restarts the wall, dashboard, watchdog and sensor readers. Quick actions matches the header buttons. Stream, sensor and layout choices refresh while preserving rule drafts. **Automation Priority** shows priorities by row position; Save order applies the displayed order.

### Finding controls after upgrading

| Task | Previous stable 1.0.42 | Stable 1.0.43 |
| --- | --- | --- |
| Snapshot overview | Overview | Monitor, with active-layout and All streams views |
| Edit a stream | Streams | Streams → Edit opens a side drawer; Save & apply commits changes |
| Display and global border preferences | System → Viewer | Settings → Display |
| LAN access and password | System → Network & security | Settings → Network & security |
| Temperature reporting | System → Viewer | Settings → Diagnostics |
| Updates, backups and maintenance | Corresponding tabs under System | Corresponding tabs under Settings |
| Logs | System → Logs | Settings → Diagnostics |
| Saved standard and automation layouts | Layouts | Layouts; Canvas settings contains output and appearance controls |

## Install and first setup

Use Windows 10/11 x64 with a current graphics driver. Download the installer and checksum from this repository's Releases page, verify the checksum, and run the installer. Installation requires elevation; normal operation should use a standard Windows account.

### First time on a new host

1. Install and launch RTSPView on the Windows computer that will display the streams. Complete setup **on that computer first**, not through its LAN IP address.
2. Open **http://127.0.0.1:5080** in a browser on that computer, or use the **Web configuration** shortcut. If the viewer covers the browser, minimize it or exit full screen using five clicks in the upper-right corner within three seconds; exiting full screen alone does not disable always-on-top.
3. Sign in with the initial password **`admin`**. The account name is also `admin`, but the login form asks only for the password.
4. On the required password-change screen, enter **`admin` in Current password**, and enter your chosen replacement in **New password**. In version **1.0.33 and later**, there is no minimum length: the replacement must be nonblank, at most 1024 characters, different from the current password and not `admin`. Choose a password that is difficult to guess, especially before enabling LAN access.
5. Submit the change, then **sign in again with your new password**. The initial password stops working and earlier sessions are revoked. No readable administrator password file is created. All administration remains blocked until the password change succeeds.
6. Add your RTSP sources under **Streams**, configure a layout and apply it. For administration from another computer or phone, follow [Enable LAN access](#access-the-admin-panel-from-the-lan) below; installation alone leaves administration local-only.

**Upgrading an existing host:** use your existing administrator password, not `admin`. If a password change is required, enter that existing password in Current password. If version 1.0.32 rejects your desired password or leaves you stuck in setup, manually install [the latest stable release](https://github.com/GalacticaActual75/RTSPView/releases/latest) over it before retrying. The password-policy fix is in 1.0.33; your existing streams/settings are retained.

**Fresh installations have no camera URLs configured**, including every main camera, legacy camera field and overlay. Enter your own URLs after setup. Existing installations retain their streams and password, but legacy security state requires a password change after login. Back up before upgrading.

The dashboard starts local-only on TCP 5080. After initial setup, use **Settings → Network & security → LAN access** to enable HTTP access on your trusted private LAN. HTTPS is optional; see the LAN instructions below.

## Shared camera diagnostics

The Viewer reserves a diagnostics strip beside the video wall. Main-feed and
overlay errors stack in this panel, labeled by camera name, feed type and slot;
custom overlay shapes do not clip their text. Hover over a camera, select it in
the panel, or click its small warning badge to inspect statistics. **Restart
selected stream** restarts only that feed. Alerts clear individually on recovery.

In full screen, the panel appears only while at least one camera has a connection
or stale-video issue, then disappears completely when all issues clear. In windowed
mode it starts hidden; use the **Diagnostics** button to open or close it. The button
shows the current alert count, but errors do not open the windowed panel automatically.
The wall reclaims all panel space when it hides without changing the saved layout.
Detailed statistics
and errors now appear here instead of over the video; main-camera name labels
still follow the name-display preference. Hidden, configured overlay feeds can
also report connection problems while being kept ready for automation.

## MQTT person-detection automation

The [RTSPview connector beta](plugins/scrypted-rtspview/README.md)
adds Scrypted camera-stream and MQTT setup through pairing and sync. It requires
RTSPView 1.0.43-beta.7 or newer. See the connector README for installation and npm
publication status. Validation on a live Scrypted server is still pending.

Open the top-level **Automation** tab to configure the broker and person-detection rules.
Saved rules collapse to a summary; click a rule to expand and edit it. New rules
stay open, and validation expands any rule that needs attention.
Rules are shown first; connection settings, event discovery and troubleshooting
are expandable sections. In Stable, opening a category closes its peers at the
same level. The dashboard uses independent inspectors and disclosure controls.
An **Entered topic (not observed)** option is a saved or typed topic that discovery
has not received during the listening session; it does not confirm event support.
The Controller maintains the MQTT connection even with the browser closed; the
Viewer must be running to display overlays. Automation is disabled by default.

1. In Scrypted, install its official **MQTT** plugin and enable the MQTT extension
   on the source cameras. Use its built-in broker or an existing independent broker;
   Home Assistant is not required. The built-in broker defaults to TCP 1883.
2. In RTSPView, enter **Broker host**, **Port**, **Transport**, and authentication
   settings. These are broker credentials, separate from the Scrypted web login.
   TLS validates the host certificate using Windows trust. Each Controller needs
   a unique **Client ID** under Advanced connection settings.
3. For **Show overlay**, configure the target stream and appearance in **Overlays**. Choose
   **Display mode → Automation only**, then **Save overlay**. This hides an already
   visible overlay while waiting for a trigger. **Always visible** keeps it visible
   when automation clears. Linked rule names and an **Open Automation** button are
   shown beneath the display mode. Existing enabled overlays stay Always visible
   after upgrading; previously disabled overlays are shown as Automation only.
4. Select **Add rule**. Choose **Show overlay**, **Fullscreen camera**, or
   **Focused layout**, then choose the target and add one or more source
   cameras. Click **Discover topics / Start listening** and trigger camera activity.
   Choose the observed camera/topic from the dropdown; a unique matching camera
   name or an existing saved mapping can fill it automatically. Verify the selected
   source using its small snapshot preview and **Refresh snapshot** button.
   If discovery cannot identify a name, inspect the raw feed while triggering one
   camera at a time. Manual topic entry remains under **Advanced**, normally
   `scrypted/<scrypted-device-id>/ObjectDetector`. Scrypted IDs are not RTSPView
   stream slots. An example event is
   `{"timestamp":1789318449255,"detections":[{"className":"person","score":0.827}]}`.
5. Set **Clear delay (minutes)**, enable the rule, and use **Test connection**.
   Testing checks the draft broker connection without saving
   or activating actions. Turn on **Enable automation**, then **Save automation**.

**Raw MQTT details** expands a feed grouped by camera and topic, showing received
time, payload, retained flag, and delivered QoS. Use Pause/Resume, Clear, and the
camera/topic filter to inspect it. Discovery is a separate read-only connection
using the fields currently in the GUI; it never sends messages to the broker or
feeds the rule engine. It stops after five minutes (or **Stop listening**), even if
the browser closes. Normal automation continues independently. At most 200 messages
and 256 observed object topics are kept in memory, with raw payloads limited to
8 KB each; larger payloads are labeled truncated. Clear hides the current feed in
this browser without changing rule state. Camera names are read from Scrypted's
own MQTT metadata (including its retained `homeassistant/+/+/+/config` topic format);
this does not contact or require Home Assistant. Discovery defaults to `scrypted/#`;
set **Advanced discovery settings → Topic prefix** for a custom publish path. Broker
permissions must allow these read-only subscriptions. Only observed ObjectDetector
topics are offered; seeing a topic does not guarantee person detection is configured.

Person detection on **any source camera** shows the selected overlay and renews
one shared clear timer. Fresh frames of the same person keep it visible. Empty,
motion-only, face-only, and duplicate messages do not renew the timer. Clear means
no new matching detections for the configured interval, not proof of an empty
scene. Multiple rules can share an overlay; it stays until all requests expire.
Different overlays can be active simultaneously. Saved layouts are not changed.

Configured overlay feeds stay connected and decode in the background, including
**Automation only** feeds while hidden. Detections reveal the already-playing
video; expiry and dismissal hide it without reconnecting. This uses continuous
stream bandwidth and decoding resources. Clearing an overlay's RTSP URL or
removing the overlay stops its background feed. Real connection/recovery warnings
still appear if the stream is unavailable when triggered.

Manual focus takes priority. Double-click a temporary, automation-only overlay
to dismiss the current detection episode; fresh detections in that same episode
do not undo the dismissal. New episodes can activate it after the clear interval.
Hidden/minimized viewers are not brought forward. An overlay's configured host
tile must be present in the active layout. Editing stream/layout configuration
cancels existing automation requests. Disabling or deleting a rule releases its
request. Viewer-local timers restore visibility even if Controller or MQTT stops.

**Status and troubleshooting:** Connected confirms the broker/subscriptions, not
person detection or successful playback. The tab reports the last message result,
last person time, and per-rule clear countdown. If nothing appears, check the
source topic, target stream URL, host tile visibility, manual focus, and Viewer
connection. MQTT can accept a topic subscription with no publisher. Keep the
Scrypted and Windows clocks synchronized: retained messages, events older than
10 seconds, events over 2 seconds in the future, and pre-connection events beyond
that tolerance are ignored. Reconnection uses a clean session, backoff, and no
queued action replay. Loss of MQTT updates lets the existing timer expire.

Connection settings and rules live in `automation.json`, separate from general
configuration exports. Passwords are protected for the Controller's Windows
identity and never returned to the GUI. Leave the password blank to keep it;
use **Clear saved password** to remove it. Re-enter credentials when moving to a
different Windows account/host. RTSPView does not modify the Scrypted broker.

**Fullscreen camera** fills the Viewer with a selected main stream or overlay;
the Viewer’s window/fullscreen setting is preserved. **Focused layout** gives the
selected main camera a larger tile and includes every enabled, configured main
stream (up to 16), using the saved layout’s landscape or portrait orientation.
Both restore the saved layout when their timers expire, without editing it.
The default **Automatic** arrangement gives the focused camera a 2×2 tile at the
upper left; remaining cameras use single tiles. Existing rules keep this behavior.
For a custom arrangement, open **Layouts → Automation layouts**, or use **Edit
automation layouts** in Automation. Separate **Standard View layouts** and
**Automation layouts** tabs keep the normal wall and automation templates independent.
Start with **One large camera** or **Two large cameras**, then move, resize, add or
remove tiles and save. Select that saved layout in a **Focused layout** rule.
Saving a template does not select it as the standard wall layout.

Each automation template has one or two **Focus tiles**, with no camera assigned
in the layout editor. The rule chooses the camera, either a fixed target or
**Camera that detected the person**. Active cameras fill the focus tiles in
activation order. Two-focus layouts expose **Focus 1 camera** and **Focus 2 camera**
in the rule editor. Assign a second camera to show both on one trigger, sharing
the clear timer, or select **Next camera with an active detection** to fill the
second position dynamically (empty until another camera is active).
If a focused camera also has a regular tile, that regular position stays empty
while the camera is focused, so it is not displayed twice. Existing saved focus
positions migrate to unassigned Focus tiles without moving their geometry.
Custom templates support up to 16 tiles on a 12×12 grid and cannot be deleted while
a rule references them. Clear timing and focus priority remain unchanged.

Both standard and automation layouts balance row heights and column widths for
16:9 camera pictures while retaining tile placement. Full video remains visible:
no cropping or stretching is introduced. Some arrangements still require bars;
other source aspect ratios remain fitted inside their tiles. The regular 3×3
layout is unchanged. **Focus + seven** is a 4×4 preset with a 3×3 large tile,
three cameras down the right and four across the bottom. Its tiles all have 16:9
proportions on a landscape wall. In automation layouts the large tile is a Focus
tile; in standard layouts it is an assigned camera.

Under **Settings → Display → Diagnostics automatic opening**, select cameras to
exclude from automatically opening the fullscreen diagnostics panel. Excluded
camera warnings, counts and statistics remain available in manual diagnostics;
issues on other cameras still open the panel normally. Settings persist across
restarts and include main cameras and overlays.

Overlays scale uniformly when their host camera tile changes size or proportions.
Their proportions follow the 16:9 overlay-editor canvas, so custom shapes do not
stretch when a host becomes tall, wide, or focused. Width/height percentages act
as maximum bounds; horizontal/vertical positioning retains the selected anchor.
Zoom, image positioning, shape and opacity remain configured as saved. Fullscreen
actions targeting the overlay itself still display the camera across the Viewer.

Each saved rule has a **Test** button, also available while the rule is collapsed.
Each source also has an optional **Required zone** field. Enter the exact,
case-sensitive Scrypted object-detection zone name (for example `MQTT`), or leave
it blank to accept people anywhere. A person must carry that name in its own
`zones` array; missing zones, other objects in the zone, and people outside it do
not trigger or renew the rule. Scrypted can continue recording the whole frame.
Zone filtering happens in RTSPView; the camera's MQTT topic still carries all
published detections. Other sources in the same rule can still trigger it using
their own filters. Test simulates a matching detection and does not verify actual
zone metadata from Scrypted; verify that with a real walk-by and Raw MQTT details.

For multiple sources, choose the source camera beside Test. Apply edits first and
enable automation and the rule. Test simulates one person detection locally in the
Controller, without publishing to MQTT or requiring a broker connection. It uses
the saved action, normal priority, and configured clear delay. The Viewer must be
running. Existing detections can keep an action visible longer, and higher-priority
actions or manual dismissal can prevent it appearing. This tests the viewer action,
not Scrypted detection or MQTT delivery.

Choose **Camera that detected the person** to follow sources automatically. Each
source then has its own clear timer; one camera’s detections do not keep another
camera active. For a fixed target, all sources renew one shared timer. Fullscreen
rules take priority over focused-layout rules. Within the same action, the earliest
active episode keeps focus until it clears; pending cameras only take over if their
detection timers are still active. Renewals do not reorder them. Overlay actions can
coexist with focused layouts when their host tiles are visible; fullscreen hides
other overlays. Manual double-clicks dismiss active and pending automation episodes
and retain the usual focus/restore control. Expiry still restores the wall if the
Controller or broker goes offline.

Focused-layout and fullscreen targets can include enabled main streams and
configured original overlay sources. Old rules without an action field remain **Show overlay** rules.

### Following detections between streams

In a rule, choose **Allow a newer detection to take over** to let a fresh
person-detection episode on another stream replace its fullscreen or focused view
before the clear timer expires. Repeated detections renew the existing episode
without stealing focus back. Existing rules default to holding their view.
When the winning episode expires, another still-active rule may resume; the
standard layout returns after all relevant episodes expire. Manual dismissal
still applies to the current episodes.

Choose **Any detection-enabled stream** to use the stream/topic/zone mappings
configured across rules, including disabled rules. Configure mappings using
Selected streams first; unmapped RTSP feeds cannot trigger automation. Each
stream must have a consistent topic and zone mapping across rules in this mode.
Select **Focused layout**, your saved layout, and **Camera that detected the
person** to follow detections. With takeover enabled and dynamic Focus 2, the
newest detecting stream fills Focus 1 and the previous still-active stream fills
Focus 2. A fixed Focus 2 selection continues to take precedence.

## Temperature monitoring and optional maintenance helper

Under **Settings → Diagnostics → Temperature reporting**, enable CPU/GPU warnings and choose a maximum for each sensor. The master warning switch turns all temperature warnings off. Above the selected maximum, the live wall shows a large red warning on a smoked background. Unavailable or stale readings do not trigger an alarm.

CPU temperatures may require [PawnIO](https://pawnio.eu/). LibreHardwareMonitor is bundled with RTSPView; sensor support still depends on the hardware and drivers. Error 1060 for the PawnIO service means that dependency is missing.

1. Install RTSPView under the default, administrator-protected **Program Files** folder. The maintenance helper is included in Stable 1.0.42.
2. In Temperature reporting, select **Enable maintenance helper** and approve the Windows administrator prompt on the host once. Keep UAC enabled; no UAC bypass is needed.
3. Select **Install PawnIO on host** and confirm. The helper downloads the pinned official PawnIO 2.2.0 installer and verifies its SHA-256 before running it. The viewer stays open; temperature readings pause during installation and resume afterward.
4. Follow the progress message. If Windows requires a restart, restart the host when convenient; RTSPView does not reboot it automatically. A successful driver installation does not guarantee that every CPU/GPU exposes a temperature sensor.

The optional **RTSPViewMaintenance** Windows service runs as LocalSystem and uses the RTSPView product icon. It accepts only dependency/update status, preparation and installation commands, and reads temperature sensors. It has no network listener: a local named pipe allows the Windows user who enabled it, with authenticated web administration providing remote access through the Controller. It cannot execute arbitrary commands, paths or download URLs. The Controller and Viewer continue running as the signed-in user. Enabling it for a different Windows account requires administrator approval again.

**RTSPView updates without repeated UAC prompts:** after installing RTSPView and enabling the helper once, confirmed updates from the web panel or wall badge use the helper when the target installer supports service updates. The helper independently looks up the exact confirmed Stable/Beta release in the official repository, downloads to an administrator-protected folder, and verifies the checksum. A separate worker survives replacement of the helper, installs the update, preserves the existing startup-task settings, and restarts the wall in the authorized user's session. Keep that user signed in during installation. Normal update confirmation and channel selection still apply; changing the channel never installs anything automatically.

Installing this capability initially still uses the current update/UAC path. Hosts without an enabled compatible helper, and older target installers (including older stable releases), continue to request Windows approval. UAC remains enabled. An interrupted service update reports its failure; check the installed version before retrying. No actual host restart is performed automatically.

The helper is bundled but is not enabled automatically. To disable it, stop **RTSPView Maintenance** in Windows Services and set its startup type to Disabled after any installation finishes. Uninstalling RTSPView removes the helper service; PawnIO is retained because other applications may use it. The manual dependency download remains available if you prefer to manage it yourself.

## Access the admin panel from the LAN

**Version 1.0.34 adds a built-in LAN switch. No certificates, environment variables or manual firewall commands are needed for normal trusted-LAN use.**

1. On the RTSPView host, open **http://127.0.0.1:5080**, sign in, and finish the required first-time password change.
2. Open **Settings → Network & security → LAN access**, turn on the **Enable LAN access** toggle, and click **Save LAN access**.
3. Accept the Windows administrator approval prompt **on the RTSPView host**. If asked, set that host's trusted connection to **Private** under Windows Settings → Network & Internet → your connection's properties, then try again. The app does not automatically mark an unfamiliar network as trusted.
4. Wait a few seconds for the listener to update. The LAN access panel lists clickable **`http://HOST-IP:5080`** addresses. Open one from another computer or phone on the same LAN and sign in with your admin password. Use **HTTP and port 5080** for this mode, not HTTPS or port 5081.

The switch saves its setting in `lan-access.json` alongside `settings.json`, allows the host's current LAN IP addresses/hostname, and configures a Windows firewall rule for **Private networks and LocalSubnet only**. The application also rejects off-subnet connections in this mode. Local access stays available, and the Viewer/streams do not restart. A brief browser disconnect while the web listener changes is normal. Approval cancelled or firewall setup failed? LAN access stays off and the panel explains the error.

**HTTP is unencrypted. Use this option only on a trusted private LAN; do not port-forward the admin panel to the Internet.** HTTPS remains available as an [optional advanced setup](docs/https-administration.md). If custom `ASPNETCORE_URLS` or Kestrel endpoints are already configured, the switch is disabled with an explanation; remove those custom bindings and restart the Controller to return to the built-in switch.

To turn LAN access off, clear the switch and save. No administrator prompt is needed for disabling: the listener returns to loopback and remote requests are blocked. A remote browser will lose access; re-enable locally on the host if needed. The scoped firewall rule remains dormant for the next enable and is removed on uninstall.

If another device cannot connect, check that both devices are on the same LAN, the host's network profile is Private, and Wi-Fi client/guest isolation is not blocking them. Use an address shown in the panel; `localhost` and `127.0.0.1` on your phone/laptop refer to that device, not the RTSPView host. VPNs or multiple adapters may produce several addresses—choose the one reachable from your device. Update installation requires Windows approval on the host unless the optional maintenance helper is enabled and the target installer supports service updates.

## What RTSPView does

RTSPView turns a Windows display into a wall of live RTSP video. The desktop Viewer plays the video; the browser dashboard configures the wall and shows status and snapshot previews. A source does not need to be a physical camera: any compatible RTSP video stream can fill a main slot or an overlay. Playback depends on the source codec and LibVLC support. RTSPView is intended for live viewing, not recording or NVR playback.

The dashboard calls source entries **Streams**; some controls still use camera terminology. These are interface labels, not restrictions on the source: substitute your encoder, rebroadcast or other RTSP feed wherever the guide says camera.

| Feature | What you can do |
| --- | --- |
| Main streams | Configure up to 16 RTSP streams with individual names, enable switches, transport, buffering and recovery settings. |
| Layout designer | Build landscape or portrait walls, choose presets, resize/rearrange tiles and keep up to 32 saved layouts. |
| Picture-in-picture | Show Doorbell, Garage and up to 14 additional overlay streams over selected camera tiles. These labels are defaults; use your own names and streams. |
| Overlay styling | Adjust shape, size, placement, opacity, zoom and pan; draw a custom mask or import supported SVG paths. |
| Dedicated display | Choose a monitor, launch full screen, keep the viewer on top and hide the idle mouse cursor. |
| Stream resilience | Request hardware decoding, reconnect failed or stalled streams, and restart individual streams or the whole viewer. Actual capacity depends on resolution, frame rate, codecs and hardware. |
| Administration | Check stream health, view logs, manage Stable/Beta updates, change the admin password and export/import configuration. |
| Diagnostics | Inspect simultaneous main-stream and overlay errors in a shared panel; restart one feed and choose which warnings automatically open it. |
| MQTT automation | Show overlays, focus one stream or activate a saved one/two-focus layout; filter by zone, test rules and optionally follow newer detections. |
| Temperature monitoring | Read supported CPU/GPU sensors and show configurable warnings. The optional maintenance helper can install the PawnIO dependency. |
| Maintenance | Schedule Viewer or host restarts; use the optional helper for compatible updates after one-time administrator approval. |
| Source management | Delete/reuse stream slots without renumbering others; use original overlay feeds in standard or automation layouts. |

## Set up streams and layouts

1. Open **Streams** after completing the initial password change. Enter a descriptive stream name and its RTSP URL, enable the slot, and choose **Save & apply**. Use the URL supplied by your camera, encoder or RTSP server; no network discovery or preconfigured camera addresses are provided.
2. Use **Add stream** when you need more than the initial nine entries, up to 16. An enabled camera also needs an assignment in the active layout to appear on the wall.
3. Open **Layouts**, choose **Landscape · 16:9** or **Portrait · 9:16**, and pick a starting preset. Presets include Single, Split, Quad, Six, Nine, Sixteen, Featured, Sidebar, Cinema, Dual focus, Center stage and Strip.
4. Assign cameras, drag tiles to move or swap them, and use a tile's corner handle to resize it. Standard layouts and automation templates support up to twelve rows and columns. Layout previews use snapshots; watch the desktop Viewer for live video.
5. Choose **Apply to wall** to save and display the draft. Edits stay in draft until applied. Under **Manage layout**, duplicate and name a layout to keep an alternative; **Save layout** saves an inactive layout without switching the wall. **Discard changes** abandons the draft. The active layout cannot be deleted.

Camera settings save per slot and apply live. Changing layout geometry or overlay placement does not require reinstalling the application.

### Stream lifecycle and snapshot previews

Streams can be removed with **Delete stream**. Deletion clears its connection and
layout placements without renumbering other streams; Add stream reuses removed
slots. Remove automation references or change a configured overlay's host first
if deletion reports a dependency.

Use **Delete overlay** on the Overlays tab to remove an overlay, including Doorbell
or Garage. This clears its connection, shape and framing and removes its original
stream entry. Remove references in automation rules and layouts first; the delete
action reports these dependencies. Other overlay IDs stay unchanged, and **Add
overlay** reuses deleted slots.

Configured overlay feeds also appear in Streams as **(overlay source)** entries
and in both layout editors. These share their source connection settings with
Overlays, but layout playback uses the complete original frame, with no overlay
crop, shape, zoom or opacity. Edit the shared connection through **Edit source in
Overlays**. Overlay presentation and layout playback can coexist; the additional
original-stream player stays connected and decodes in the background, ready for
focus and layout changes. Stable displays the full frame with aspect-fit sizing;
The editor also offers the selected layout tile's framing controls. This player uses
software decoding; configured overlay sources therefore add background CPU
and network load even when their original streams are not currently visible.

Enabled, configured main streams connect and keep decoding even when they are not
assigned to the current layout. Layouts control visibility; disable a stream to
stop its background playback. Unassigned streams retain native decoding and the
configured hardware-decoding preference, and still consume network and decoder
resources. They can provide snapshots and be shown by automation without waiting
for their first connection.

Browser previews automatically request fresh snapshots about every 15 seconds
while visible in the active tab. Captures run one at a time, so large grids or
slow streams can take longer. Preview age is separate from stream health; failed
refreshes retain the previous image. These previews are not live video.

## Expanded layout sizing and appearance

These controls are included in Stable 1.0.43. Both **Standard View layouts** and **Automation layouts** support up to 16 tiles on a grid of up to 12 rows and 12 columns. Each saved layout can use a preset or custom output resolution; custom dimensions range from 240 to 16384 pixels per axis.

### Redesigned workspaces

Version 1.0.43 replaces Overview with a dedicated **Monitor** snapshot board and saved-layout switcher. **Streams** has search and a focused editing drawer; closing the drawer keeps its draft until saved or discarded. **Overlays** has Position, Appearance, Shape, Image and Connection inspectors, with one shape editor for presets, drawing and SVG import. **Settings** groups Display, Network & security, Updates, Backups, Maintenance, Diagnostics and About. The Windows viewer has a simpler toolbar and a separate stream editor.

### Canvas settings

Open **Layouts**, select the standard or automation layout, then **Canvas settings**:

- **Borderless view** hides camera-tile borders for that layout.
- **Border color** sets the tile-border color when borders are enabled.
- **Background color** fills empty canvas space and letterboxed areas in the Viewer and browser previews. Black bars encoded into the camera footage remain part of the video.
- **Use display border setting** restores inheritance from the global Display preference. Existing layouts keep that preference until overridden.

Standard and automation layouts save these settings independently. Use **Apply to wall** for the active standard layout, **Save layout** for an inactive standard layout, or **Save automation layouts** for an automation template. When automation clears, the standard layout's appearance returns. Floating picture-in-picture overlay borders remain separately controlled.

### Tile framing and sizing

Select a tile to choose **Original**, **Fit**, **Fill**, or **Stretch**, plus zoom and horizontal/vertical picture position. Fit preserves the full frame; Fill can crop; Stretch can distort the source aspect ratio. Original scales relative to the saved output resolution. These controls affect the layout view without changing the camera's source stream.

In either layout editor, open **Sizing**. **Fit tiles to streams** uses loaded
snapshot proportions (choose preview streams for automation focus tiles first)
to propose less letterboxing while keeping small tiles equal. It leaves the
arrangement unchanged if no improvement is found. **Row heights** and **Column
widths** allow fine shared-track adjustments in 0.5% steps; linked tracks move
together, so tiles cannot overlap. Selected tiles show a picture-fit percentage.

Changes remain in the draft until saved or applied. Use **Undo** or **Reset
sizing** to return to the previous or automatic proportions. Existing layouts,
including the standard 3x3, retain their original sizing. The viewer uses the
same saved proportions as the editor; the selected tile framing mode determines
whether video is fitted, cropped or stretched. Focus sizing stays fixed when automation changes
streams, avoiding unexpected rearrangement of the surrounding feeds.

## Always-on-top and full-screen behavior

**Keep viewer always on top is enabled by default.** In **Settings → Display**, clear **Keep viewer always on top** and choose **Apply changes** when you want to use other applications normally on the same monitor. While enabled, RTSPView periodically reasserts its topmost position, so a browser or another ordinary window can appear behind the camera wall even after you switch to it. This is intentional for a dedicated camera display.

Always-on-top and full-screen mode are separate settings. **Exit full screen** restores the window border and local controls, but does **not** turn off always-on-top. To work comfortably on the same computer, disable always-on-top and exit full screen. **Launch full screen** controls the saved behavior; later configuration reloads can restore that saved mode, so clear it as well if you want the viewer to stay windowed.

**Settings → Maintenance** provides immediate **Enter full screen** and **Exit full screen** commands. For a local escape from full screen, click the upper-right corner of the selected display **five times within three seconds**, then confirm. The click area is the upper-right 64 × 64 pixels. This gesture exits full screen; it does not stop the viewer or disable always-on-top. This is a display convenience, not a secure Windows kiosk lock.

**Viewer lifecycle controls:** **Full exit** in the bottom-right of the
windowed viewer closes the viewer and pauses automatic recovery. Web administration,
monitoring, and background controller services remain online. The pause survives a
controller restart; scheduled viewer restarts also respect it. Open the desktop
RTSPView shortcut or use **Settings → Maintenance → Start viewer** to resume viewing
and re-enable automatic recovery. Start viewer is disabled while the viewer is
running or starting. Failed launches leave an intentional pause intact.

Under **Settings → Display → Wall behavior**, enable **Show Full exit when hovering
in the top-right corner** to reveal an exit button in full-screen mode only while
the pointer is in that corner. It is off by default. The existing five-click
full-screen escape gesture remains available with either setting.

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

Under **Settings → Maintenance**, **Scheduled restarts** can restart the Viewer and Windows host independently, on selected weekdays at a host-local time or every X hours. The Controller must remain running. Host restarts have a 60-second countdown with cancellation in the admin panel; updates defer restarts and missed runs are skipped after startup or sleep. These host-specific schedules are excluded from configuration exports.

Start with the default streaming settings. For an unreliable connection, try TCP and increase **Cache (ms)** to trade latency for smoother playback. **Startup timeout**, **Stall timeout** and **Maximum backoff** control connection/recovery timing; **Low latency** changes playback tuning. **Composite stream compatibility** forces TCP with a 3000 ms buffer and disables low-latency tuning for rebroadcast/composite streams. More streams and larger resolutions increase network, decoder and graphics load.

Use **Restart stream** on a camera for a single-feed problem, **Restart all streams** for all feeds, or **Restart Live View** for the display process. **Restart Application** restarts the live view and Controller, including its watchdog, MQTT/Tapo readers, and dashboard. It resumes automatic viewer recovery, briefly disconnects the dashboard, and reconnects without discarding browser edits. This requires an installed application and waits for updates/maintenance to finish. The optional privileged temperature/installer service stays running. **Reboot host** restarts the entire host. The dashboard also shows health information and recent logs. Browser thumbnails and layout/overlay previews are snapshots, not full-motion browser video.

**Show stream names** controls labels on the wall. Detailed statistics and connection/stale-video errors are shown in the shared diagnostics panel. These diagnostics are separate from picture-in-picture video overlays.

If startup supervision was enabled during installation, RTSPView starts at sign-in and the Controller relaunches a missing Viewer. Closing only the viewer can therefore cause it to return. To stop the installed wall deliberately, run the **Stop RTSPView** shortcut with administrator rights; it stops supervision and the camera processes until a manual start or the next sign-in.


## Configuration and persistent data

The main configuration file is `settings.json`:

- **Existing installations using the legacy data directory:** `%LOCALAPPDATA%\SpotMonitor\settings.json`
- **Fresh RTSPView installations:** `%LOCALAPPDATA%\RTSPView\settings.json`

Paste the applicable path into File Explorer's address bar on the computer running RTSPView, signed in as the Windows user that runs the application. `%LOCALAPPDATA%` belongs to that user, so another Windows account has a different folder.

If set, `RTSPVIEW_DATA_DIR` overrides the default folder; the older `SPOTMONITOR_DATA_DIR` variable is also supported as a fallback. The same folder contains LAN access state (`lan-access.json`), password state (`web-security.json`), logs, backups and cookie-protection keys. **Keep the folder private: stream credentials are stored in the settings.** Stop both processes before manually editing or backing up these files.

No environment variables are required. `.env.example` is a reference; the application does **not** automatically load `.env` files. Set variables in the Windows user environment and restart both processes (sign out/in for scheduled startup).

| Variable | Default | Purpose |
| --- | --- | --- |
| `RTSPVIEW_DATA_DIR` | `%LOCALAPPDATA%\RTSPView` (existing installations retain their legacy data directory) | Settings, password hash, logs, thumbnails, update staging and cookie keys. Use a private absolute directory. |
| `RTSPVIEW_GITHUB_REPOSITORY` | `GalacticaActual75/RTSPView` | Public GitHub release repository. No token/key is required or supported. |
| `ASPNETCORE_URLS` | `http://127.0.0.1:5080` | Advanced binding override; disables the built-in LAN switch. HTTPS also requires certificate configuration. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Host allowlist for externally configured bindings. The built-in LAN switch manages its own local hostname/IP allowlist. |
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
dotnet restore RTSPView.sln -r win-x64
dotnet build RTSPView.sln -c Release --no-restore
dotnet run --project tests/RTSPView.ConfigurationChecks -c Release
$env:DOTNET_HOST_PATH = (Get-Command dotnet).Source
node tests/admin-security.checks.cjs
dotnet run --project tests/RTSPView.LanAccessChecks -c Release
node tests/lan-firewall.checks.cjs
node tests/branding.checks.cjs
node tests/shape-editor.checks.cjs
node tests/wall-layout-presets.checks.cjs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/UpdateHelper.Checks.ps1
dotnet run --project tests/RTSPView.LayoutVisibilityChecks -c Release
dotnet run --project tests/RTSPView.OpacityChecks -c Release -- --auto
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

See the [branding audit](docs/branding-audit.md) for the cleanup and the compatibility identifiers retained for existing installations.
