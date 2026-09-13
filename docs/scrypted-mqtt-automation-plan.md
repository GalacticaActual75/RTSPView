# Scrypted MQTT overlay automation

Implemented for beta 1.0.42-beta.1. See the README for setup and troubleshooting.

## Behavior

Each GUI rule selects one or more source cameras, their exact MQTT ObjectDetector
 topics, a target overlay, and a clear delay. A person detected on any selected
source shows the overlay and renews a shared deadline. No new matching detections
for the selected interval releases the overlay. Existing manual visibility is
preserved, and multiple rules may share an overlay.

The Controller owns MQTT, validation, freshness checks, and event processing.
The Viewer owns temporary overlay leases and expiry so restoration does not
require the browser or Controller to remain connected. Configuration changes
invalidate old leases, and manual focus/dismissal takes priority.

## Official Scrypted support

The official `@scrypted/mqtt` plugin supports publication and an optional Aedes
broker. Built-in topics normally use `scrypted/<device-id>/<property-or-interface>`.
For person rules, subscribe to `ObjectDetector` and inspect timestamped
`detections` entries with `className` equal to `person`. Device IDs are separate
from RTSPView camera slots. The current integration uses clean MQTT 3.1.1 sessions.

- [Official MQTT plugin](https://github.com/koush/scrypted/tree/main/plugins/mqtt)
- [ObjectsDetected schema](https://developer.scrypted.app/gen/interfaces/ObjectsDetected.html)
- [ObjectDetectionResult schema](https://developer.scrypted.app/gen/interfaces/ObjectDetectionResult.html)
- [Smart Motion Sensors](https://docs.scrypted.app/detection/smart-motion-sensor.html)

Boolean motion and doorbell states have different freshness semantics and are
not implemented as triggers in this beta. Device-specific button support must
be verified with the camera provider.

## Validation

Automation tests cover person filtering, multiple-source OR renewal, stale and
retained messages, duplicate/out-of-order events, shared overlays, manual
dismissal, expiry without Controller, protected credentials, and a real local
MQTT broker driving an isolated named-pipe test receiver. Tests include broker
outage/reconnect and disabling automation. Existing configuration, native
layout/focus, and authenticated HTTP security checks also pass. The GUI was
checked with a two-source rule, draft connection test, save, and reload.

A source-camera walk test confirmed motion and person messages in Scrypted MQTT.
Live playback validation with the beta installed remains a separate step.

## Future actions

These are recorded requests, not implemented options:

1. Make a selected camera full screen until person detection has been clear for
   a configurable time.
2. Switch to a layout that enlarges the camera with an active person while
   keeping all streams visible.

Both should eventually be action choices in Automation. Simultaneous detections
and restoration of manual layout state need explicit behavior when implemented.
