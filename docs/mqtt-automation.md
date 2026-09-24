# MQTT person-detection automation

This guide describes RTSPView 1.0.46. For installation, LAN access and Viewer setup, see the [README](../README.md).

## Connect and create a rule

The Controller maintains MQTT while the browser is closed. The Viewer must be running to display actions. Automation is disabled by default.

1. Configure a reachable MQTT broker and a source publishing person detections. With Scrypted, use its official MQTT plugin and the camera's MQTT extension, or the optional [RTSPview connector](../plugins/scrypted-rtspview/README.md). RTSPView does not provide a broker or object detector; Home Assistant is not required.
2. In **Automation → MQTT**, enter **Broker host**, port, transport and broker credentials. RTSPView defaults to port 1883; use your broker's actual TCP/TLS port. TLS validates the certificate against Windows trust. Each Controller needs a distinct Client ID.
3. For **Show overlay**, configure the target under **Picture in picture**, including its URL and host camera. Choose **Automation only** to hide it until triggered, then **Save & apply**. **Always visible** restores visible presentation when automation releases its request.
4. Add a rule, choose **Show overlay**, **Fullscreen camera** or **Automation layout**, and select its targets and source cameras.
5. Use **Discover topics / Start listening**, trigger activity and select the observed source topic. Check the source snapshot/name rather than assuming a discovered topic is the intended camera. Manual exact-topic entry remains available. Wildcards are not accepted in rule source topics.
6. Optionally enter a **Required zone**, set **No new person detections for (minutes)** (0.1–120 minutes), enable the rule and enable MQTT automations, then **Save & apply**.
7. Use the saved rule's **Test** control, followed by a real detection. A synthetic test does not verify camera publishing or zone metadata.

For the official Scrypted MQTT path, a topic commonly has the form `scrypted/<device-id>/ObjectDetector`; device IDs are not RTSPView slot numbers. Connector-generated topics use a separate `rtspview` prefix. Select observed topics or the saved connector mapping instead of guessing IDs.

With unsaved edits, **Test connection** checks the draft broker connection. Without draft edits it also checks saved enabled-rule subscriptions. It neither saves settings nor activates actions. MQTT can accept a subscription with no publisher; Connected alone does not confirm detections or video decoding.

## Discovery and event filtering

Discovery uses a separate connection and the current draft connection fields. It does not publish or feed the rule engine, and stops after five minutes or when stopped manually. Normal automation continues independently.

**Raw MQTT details** shows camera/topic, received time, payload, retained flag and delivered QoS. Pause/resume and camera/topic filters help inspect activity. Clear hides the current feed in this browser without resetting automation. Discovery holds at most 200 messages and 256 object topics in memory, with raw payloads truncated above 8 KB. Treat payloads and camera metadata as private.

Discovery defaults to `scrypted/#`; change the advanced topic prefix for connector events or custom publishers. Camera names may be read from retained Home Assistant-format metadata, without contacting or requiring Home Assistant. An **Entered topic (not observed)** entry means only that the topic was typed or saved.

A matching `person` detection renews the clear timer. Empty, motion-only, face-only and duplicate messages do not renew it. Required zones match the exact, case-sensitive name in that person's `zones` array; a different object in the zone does not qualify. Source-specific filters are independent within a rule.

Keep clocks synchronized. Retained events, events older than ten seconds, events more than two seconds in the future and events predating the current connection beyond that tolerance are ignored. Reconnection uses a clean session and backoff without replaying queued actions. Loss of events lets active timers expire. Clear means no fresh matching event during the delay, not proof that a scene is empty.

## Actions, templates and takeover

| Action | Presentation |
| --- | --- |
| Show overlay | Reveals the configured picture-in-picture feed while its host tile is visible. Multiple requesting rules keep it visible until all requests clear, subject to priority. |
| Fullscreen camera | Temporarily fills the Viewer with the target feed without changing its window/fullscreen preference. |
| Automation layout | Uses the default Automatic arrangement or a saved template from **Layouts → Automation layouts**. |

Automatic places the focus camera in a large upper-left tile and includes the other enabled, configured main streams. Custom templates use their saved tiles instead. Templates have one or two unassigned Focus tiles; the rule selects fixed cameras or **Camera that detected the person**. With two focus positions, choose a second fixed camera or **Next camera with an active detection**. A fixed second camera shares the triggering timer; a dynamic second position waits for another active camera. A camera appearing in a focus tile is not duplicated in its ordinary tile.

Use **Edit automation layouts** to open the editor. Templates support up to 16 tiles on a 12×12 grid. Saving a template does not select it as the standard wall; a referenced template cannot be deleted until rules are updated.

For following sources, the takeover control allows a newer detection to replace the current focus. Otherwise the existing focus holds until its episode clears. **Any detection-enabled stream** uses source/topic/zone mappings already configured across MQTT rules; it is not automatic discovery of every camera. Conflicting mappings must be corrected before saving.

**Automation → Automation Priority** orders saved MQTT and Tapo rules together, with 1 highest. Higher-priority actions can interrupt lower-priority holds. Wall presentations compete separately from actions for each overlay. Lower-priority actions can resume only while their requests remain active. Use distinct priorities when order matters.

Manual focus takes priority over automated presentation. Double-clicking a temporary automation-only overlay dismisses that episode; fresh detections in the same episode do not immediately reveal it again. A later episode can trigger after the clear interval. Automation does not bring a hidden/minimized Viewer forward. Disabling/deleting a rule releases its request, and stream/layout edits cancel existing requests. Viewer-local expiry restores presentation if Controller or MQTT stops.

Configured picture-in-picture feeds remain connected and decode while hidden, including automation-only feeds. This avoids reconnection at trigger time but consumes resources. Clear the source URL or remove the overlay to stop its feed.

## Status, credentials and recovery

Rule status distinguishes Viewer acknowledgement from **Wins viewer priority**, with explanations for manual focus, dismissal and competing rules. Missing/stale telemetry is unavailable, not proof of successful display. **Recent activity** retains up to 200 decisions per integration across restarts and excludes raw MQTT payloads and passwords. Queue-drop counts apply since Controller startup. These records describe event handling and arbitration, not successful decoding.

MQTT connection settings and rules live in `automation.json`. Passwords use the Controller's Windows data protection and are never returned to the browser. A blank password field keeps the saved password; **Clear saved password** removes it. Re-enter credentials when moving Windows identities/hosts. Web configuration exports include MQTT settings/rules but omit its password.

MQTT and Tapo retain three local backup generations; recovery can restore a structurally valid backup. Combined imports and priority saves use a recovery journal. Keep these files private, and preserve the `data-protection` directory during administrator-password recovery.
