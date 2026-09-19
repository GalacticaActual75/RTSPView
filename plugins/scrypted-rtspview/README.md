# RTSPview connector

Connect [RTSPView](https://github.com/GalacticaActual75/RTSPView), the Windows RTSP
camera-wall viewer, to Scrypted without manually copying each camera URL and MQTT
topic. Select cameras in Scrypted, pair the connector with RTSPView, and sync their
rebroadcast streams and MQTT connection settings.

**Status: 0.1.0-beta.1.** Requires RTSPView **1.0.43-beta.7 or newer**.
The plugin and Controller integration are covered by automated tests; validation
on a live Scrypted installation is still pending.

## Install the beta

The beta package is attached to the [RTSPView beta release](https://github.com/GalacticaActual75/RTSPView/releases/tag/v1.0.43-beta.7).
Public npm publication is pending maintainer sign-in. After publication, open
Scrypted's **Install Plugins**, search for **scrypted-rtspview**, and install
**RTSPview connector**. The [npm package page](https://www.npmjs.com/package/scrypted-rtspview)
will show the published version when available.

Install the matching RTSPView beta on your camera-wall computer, then follow
**Pair and sync** below. Earlier RTSPView builds cannot pair with the connector.

## What it does

- Imports selected camera names and rebroadcast RTSP URLs into RTSPView.
- Matches previously imported cameras by stable Scrypted identity. An existing,
  unpaired main stream with exactly the same URL is adopted instead of duplicated.
- Reads the official Scrypted MQTT plugin's broker settings, or uses an explicit
  broker override, and configures RTSPView's MQTT connection.
- Publishes fresh `ObjectDetector` events on dedicated topics:
  `rtspview/<connector-instance>/<camera-id>/ObjectDetector`.
- Supplies each imported camera's topic to RTSPView's automation rule editor.
- Preserves layouts, automation rules, and existing camera playback tuning.

The connector subscribes to Scrypted device events directly. It does not change
the official MQTT plugin's camera extensions, per-camera broker overrides, or
existing published topics. The selected cameras use the connector's single broker
for this integration. Home Assistant is not required. The connector does not
provide object detection, a broker, or an NVR.

## Requirements

- Scrypted with the selected cameras already working and their rebroadcast/Stream
  Management extension enabled.
- A reachable MQTT TCP or TLS broker. Configure the official Scrypted MQTT plugin
  first, or enter the connector's advanced broker override.
- The RTSPView Controller build containing this connector integration, with initial
  administrator password setup complete and LAN access enabled for remote pairing.
- A Scrypted hostname/IP reachable from the RTSPView computer. Container-only
  addresses and `localhost` are not suitable for the exported streams.

## Pair and sync

1. In RTSPView, open **Settings → Network & security → RTSPview connector** and select
   **Create pairing code**. The code expires in five minutes and can be used once.
2. In the Scrypted plugin, set **RTSPView address** (for example,
   `http://viewer-host:5080`) and **Scrypted LAN hostname or IP**.
3. Paste the code into **Pairing code** and save that setting.
4. Choose **Cameras to sync**. Under **Camera streams**, select a rebroadcast stream
   for each camera when needed; otherwise the first available stream is used.
5. Select **Sync to RTSPView**. The connector checks its broker connection before
   submitting the configuration. Reload the RTSPView dashboard after a successful sync.
6. Arrange the imported streams in RTSPView. In **Automation**, select an imported
   camera as a rule source; its MQTT topic is filled in automatically. Choose the
   desired action and enable automation when ready.

For RTSPView's MQTT discovery/raw feed, use the `rtspview` topic prefix to inspect
connector events. The default `scrypted` prefix shows the official plugin's topics.

Sync is explicit, not periodic. It updates camera names and URLs but does not
remove streams when cameras are deselected, delete existing rules, or turn on
automation automatically. Changing camera selection takes effect after the next
successful sync. Existing forwarding resumes after plugin restart and reconnects
after broker outages; offline events are discarded rather than replayed.

RTSPView supports 16 main streams. Imports reject insufficient capacity and
ambiguous existing URL matches. Cameras without `ObjectDetector` can be imported
for viewing but receive no detection topic. This preview does not discover zones
or translate motion/doorbell boolean events into person detections.

RTSPView currently has one MQTT connection. If existing rules use a different
broker, sync stops with an explanation instead of moving those rules to a new
broker. Align the Automation connection with the intended broker before retrying.

## Connection and credential handling

Pairing authorizes camera and MQTT configuration changes, not general RTSPView
administration. RTSPView stores a hash of the connector token, and uses its
existing Windows data protection for the MQTT password. The Scrypted plugin
stores its token and active broker credentials in Scrypted plugin storage; protect
the Scrypted server and its backups accordingly. Rebroadcast stream paths may
themselves grant camera access and should be treated as credentials.

HTTP transfers pairing codes and configuration without encryption. Use it only on
a trusted private network, or configure HTTPS with a certificate trusted by the
Scrypted host. Certificate verification is enabled for HTTPS and MQTT TLS.

**Disconnect connector** revokes further configuration sync. Imported streams,
automation rules, and MQTT forwarding remain; disable/uninstall the plugin to stop
forwarding. Pairing a replacement connector or changing RTSPView's administrator
password also invalidates the previous token. One Scrypted connector can be paired
with each RTSPView Controller in this preview.

## Build and test

Use Node.js 22 or newer and npm in this directory:

```sh
npm ci
npm test
npm run build
npm run check:package
npm pack --dry-run
```

The build produces `dist/plugin.zip`. The SDK wrapper supports Windows command
launching and runs Scrypted's Rollup build with the pinned SDK.

To deploy a development build to your Scrypted server after authenticating with
the Scrypted CLI, run:

```sh
npm run scrypted-deploy -- <scrypted-host>
```

From the RTSPView repository root on Windows, run the Controller checks:

```sh
dotnet run --project tests/RTSPView.ConnectorChecks -c Release
node tests/connector-http.checks.cjs
```

These checks cover pairing/revocation, setup and CSRF protection, import stability,
credential protection, failure rollback, stream/broker discovery, and event
forwarding with a simulated Scrypted runtime. A live server test must still verify
camera settings exposure, RTSP playback, detection delivery, and container networking.

## Publishing

The npm package name is `scrypted-rtspview`. This beta retains the repository's
reserved licensing status (`UNLICENSED`); no open-source license is granted.
No publishing workflow runs automatically.

Publish using the maintainer's npm account with `npm publish --access public --tag beta`.
For this first beta-only release, also set
`npm dist-tag add scrypted-rtspview@0.1.0-beta.1 latest`: Scrypted's normal install
button resolves the default tag. The version remains explicitly a beta.
The package includes the `scrypted` keyword used by Scrypted's npm-backed plugin
search. Verify actual search visibility and installability after npm indexing;
indexing and ranking are external. There is no separate catalog submission.

## Links

- [RTSPView application, releases, and documentation](https://github.com/GalacticaActual75/RTSPView)
- [Scrypted plugin development](https://developer.scrypted.app/plugins.html)
- [Official Scrypted MQTT plugin](https://github.com/koush/scrypted/tree/main/plugins/mqtt)
