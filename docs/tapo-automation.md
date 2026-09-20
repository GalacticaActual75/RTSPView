# Tapo sensor automations (1.0.44-beta.3)

Configure direct Tapo access in **Automation → Tapo**. Controller reads T110 contact sensors through H100/H200 hubs on your network using the bundled python-kasa reader. No separate Python installation, Home Assistant connection, MQTT broker or Scrypted plugin is needed. This beta has automated reader, Controller, HTTP and native-viewer tests; physical H100/H200/T110 validation is still pending. Firmware and Third-Party Compatibility settings may affect connectivity.

## Connect and discover

1. Expand **Tapo hubs and account**. Enter your Tapo account email and password in RTSPView's administrator interface.
2. Select **Discover hubs on network**, then add the discovered hubs you want. Discovery runs on the RTSPView computer without account credentials and does not save or activate anything. For other subnets, blocked broadcasts or undiscovered hubs, use **Add hub manually** with a name and IPv4 address or hostname (no URL scheme or port). Add both H100 and H200 if you do not know where the T110 is paired. Up to eight hubs are supported.
3. Select **Test connection & discover sensors**. This reads the draft connection without saving or firing actions. Each T110 is labeled with its hub; hub failures are reported separately.
4. Add sensor rules, enable **Tapo sensor automations**, and save. MQTT has a separate enable switch.

If authentication fails, check the account, hub address and Tapo's Third-Party Compatibility setting. RTSPView does not pair or move sensors between hubs. Keep their existing pairing in Tapo.

## Display an overlay only while a door is open

First configure the stream, host tile, shape and placement under **Overlays**. Set its display mode to **Automation only** if you want it hidden when no rule applies. In the Tapo rule choose:

| Control | Value |
| --- | --- |
| Sensor | Your discovered T110 |
| While the door is | Open |
| Then | Show overlay |
| When the state clears | Hide overlay |
| When unavailable | Hide overlay, or Restore normal behavior |
| Overlay | The configured overlay |

No layout target is required. The overlay still needs its host tile to be visible in the current layout. Show and Hide override its saved visibility while the rule applies, subject to higher-priority rules for that overlay. Reverse Open/Closed to show only while closed.

**Restore normal behavior** releases this rule's override so the saved visibility and other rules take effect. It does not always mean hidden. **Hide overlay** actively hides the selected overlay; it can suppress lower-priority show rules even after the matching state clears. Disabling a rule releases its override.

## Change the layout

Use **Layouts → New layout** to create a named blank layout or start from a preset, in either Standard View or Automation layouts. New tiles default to Fit; existing explicit sizing remains unchanged.

Choose **Activate standard layout** and its target in the sensor rule, or **Activate automation layout** and a template. Automation templates expose **Focus 1 camera**, plus **Focus 2 camera** when the template has two focus slots. The second focus can be left empty; selected cameras must be different. The same focus selections apply to matching and clearing actions. On clear, restore normal behavior or select another saved layout. Sensor layout changes are temporary and do not overwrite the saved active layout. To control a layout and overlay simultaneously, create two rules for the same sensor.

## Priorities

**Automation → Priority** lists saved MQTT and Tapo rules together. Drag to reorder or use Move up/down, then **Save order** to assign priority 1 to the top rule, 2 to the next, and so on. Save or discard edits in MQTT/Tapo first. Disabled rules retain their place. Switching subtabs preserves drafts; a stale priority save is rejected if another session changed the rules.

- Every MQTT and Tapo rule has priority **1–100; 1 is highest**. Existing rules default to 50.
- Priority applies to competing wall views and, separately, to actions targeting the same overlay. Independent overlays can remain visible together.
- Higher priority interrupts lower priority immediately after a trigger is received, regardless of the lower-priority rule's hold/takeover setting. A priority-1 doorbell rule therefore replaces a priority-2 garage view.
- When the winner clears, a lower-priority rule resumes only if its sensor action is still active or its MQTT lease has not expired.
- Equal-priority MQTT rules retain their existing fullscreen and newer-detection takeover behavior. An equal-priority sensor action wins over MQTT for the same target; equal-priority sensor rules use a stable rule-ID ordering. Use distinct priorities when ordering matters.
- Manual camera focus takes priority over automated wall changes.

## Status, testing and timing

The default polling interval is five seconds (configurable from 5 to 60). Network requests and presentation can add latency. Brief open/close changes between polls can be missed. A priority change is immediate once RTSPView receives the event; Tapo is not a push-event source.

Open/Closed reflects the hub's reported state. Missing sensors, failed hubs and explicitly offline readings are **Unavailable**, never assumed Closed. A battery sensor's hub may retain its last state until it reports the sensor offline. Choose the unavailable action deliberately. Sensor commands expire in the viewer after five seconds without renewal, restoring normal behavior if Controller stops.

**Test open**, **Test closed** and **Test unavailable** apply a saved, enabled rule's selected state to the running viewer for ten seconds. They do not operate the sensor or hub. Normal polling resumes afterward; changing or disabling settings clears tests. Status indicates active rule conditions, which may be superseded by a higher-priority rule.

## Storage and packaging

The Tapo password is encrypted using Windows account-scoped application protection, never returned to the browser, and passed privately to the reader through standard input. It is not placed in process arguments. Leave the password blank to retain it; disable Tapo before removing it.

Tapo connection settings and rules are stored separately in `tapo.json`. The standard camera-configuration export does not include this file or the Tapo password; reconfigure Tapo when moving to another Windows account or host.

The installer includes the independent GPL reader, its source, exact dependency source archives and license notices under Controller's `Tapo` directory. See [reader source and build instructions](../integrations/tapo-reader/README.md).
