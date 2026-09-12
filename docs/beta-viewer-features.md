# Beta viewer tools

Base: stable 1.0.39 and beta 1.0.39-beta.1 both point to public commit `6300d001141ae63efade4cd77e54b5a9e6bb454b`. The beta branch was fast-forwarded to this exact base before applying these changes. Stable is unchanged.

## Focus

Double-click a main camera or picture-in-picture overlay to fill the viewer area. Double-click it again to restore the active saved layout, including custom tile spans and overlay placement, shape and opacity. Focusing does not save configuration or restart streams. Other overlays are hidden during focus. The fullscreen five-click escape corner and restart buttons retain their existing actions. If the saved layout changes while focused, leaving focus shows that current saved layout.

## Stream details

The viewer and web camera cards show source resolution, codec, measured decoded frames per second, demux bitrate and per-camera decoder status. Decoded FPS is sampled roughly once per second and can fluctuate; it is not the advertised source frame rate. Bitrate is LibVLC's demux statistic, not a promise of fixed encoder bitrate.

Native cameras use individual LibVLC engines so hardware-decoder log evidence can be attributed to the correct camera. Hardware-active status requires a positive report from that camera's engine. If the engine does not report its hardware choice, the display explicitly says "Hardware requested (unconfirmed)". Composited overlays and explicitly disabled hardware decoding show software decoding. This avoids treating hardware requested as proof that it is active. Separate engines add some per-camera overhead; full-wall GPU and memory behavior needs testing on the target camera host.

## Stale video

After five seconds without decoded-frame progress, a persistent red banner shows "Last frame received N seconds ago", or "No video frames received for N seconds" if no frame has arrived. It stays visible with camera labels/stats disabled and continues aging through reconnect attempts. Only actual decoded-frame progress clears it. Existing automatic recovery remains in place. Disabled/unconfigured cameras do not get a stale warning.

This detects missing decoded-frame progress. A camera or NVR repeatedly sending the same image as newly encoded frames still counts as a progressing stream; visual scene-freeze detection is not implemented.

No MQTT or automation integration is included in this beta.
