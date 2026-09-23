# Modern Dark admin — 1.0.46-beta.7

The management web application now follows the supplied Modern Dark concept: a persistent collapsible sidebar, compact page header, camera-first Monitor, and a shared dark palette across all six destinations. This release changes no Windows Viewer source, backend endpoints, configuration schema, automation execution, or playback behavior.

## Functionality inventory and preservation

| Area | Retained workflows |
| --- | --- |
| Shell | Authentication/setup, sign-out, beta badge, Quick actions and host commands, original Buy Me a Coffee URL, original feedback dialog and GitHub destinations |
| Monitor | Active wall and all-stream views, layout selection with explicit switching, refresh snapshots, stream details, telemetry health, snapshot age/failure reporting, configured layout proportions and weather previews |
| Streams | Search, inventory thumbnails, assignment/status, add/edit/delete, enable, source URL/type/quality, ONVIF discovery, connector guidance, test/restart, transport and recovery, dirty/save/discard |
| Layouts | Standard and automation layouts, new/duplicate/name/delete/revert, all presets, weather and streams, move/swap/resize, image framing, orientation/output resolution, row/column sizing, colors/borders, undo/redo, draft save versus explicit wall application |
| Picture in picture | Overlay selection/add/delete, direct canvas manipulation, host and source selection, display mode, position/size/opacity, shape/SVG editor, image framing, connection/recovery, automation linkage, test/refresh/save/discard |
| Automation | MQTT and Tapo rules and connections, trigger/source/zone/topic mappings, all action/target/timing/clear/unavailable choices, testing/discovery, activity/troubleshooting, shared priorities, distinct integration save scopes |
| Settings | Display, snapshots, network/security/connector, updates/helper components, backup/import, maintenance and restart schedules, diagnostics/temperatures/logs, About and existing links |

## Implementation boundaries

- `modern-shell.js` moves existing navigation/support controls and preserves their event handlers. Sidebar collapse preference uses a local presentation-only key with a storage-unavailable fallback. Narrow windows use an icon rail with accessible names and tooltips.
- `modern-dark.css` provides the management theme and responsive styling. Existing editor geometry and camera rendering math are retained.
- `ui-dialogs.js` queues application confirmations, restores focus, safely renders user names as text, and supplies nonblocking dismissible notifications. Cancel is the initial focus for destructive actions. All former browser confirmation calls now await the result before issuing a request.
- Deleting layouts, MQTT/Tapo rules, and hubs now asks for a named confirmation. These still edit the draft first and require the existing save action; their persistence behavior is unchanged.
- The user explicitly chose removal of the native close/reload warning. Unsaved forms survive in-app navigation; sign-out asks for confirmation. Closing or reloading the browser discards unsaved work without a native warning. Credentials/drafts are not copied into browser storage.
- The layout toolbar separates add-content, editing, and wall actions. Existing active-layout save behavior is unchanged; the existing scope explanation remains visible.
- PiP shows the selected host/source and stream connection status in the inspector. Connection status is not represented as proof that an automation is currently displaying the overlay.
- Missing, failed, and stale snapshot captions remain visible on desktop; fresh-image detail appears on hover or keyboard focus. The wall remains explicitly labeled as snapshot previews.

## Verification

- Controller Release build: passed, zero warnings/errors.
- Existing JavaScript checks: presets, proportions, shape editor, snapshot age and refresh, viewer controls, stream status, application restart, automation health, related choices, branding, privacy.
- New `modern-admin.checks.cjs`: no native dialogs/unload warning, script load order, safe untrusted names, cancel/accept, focus restoration, queued requests, and input dialogs. Included in the release check runner.
- Isolated real-HTTP Controller security checks: protected routes, setup gate, password rotation/session invalidation, CSRF, sanitized export/logs, throttling.
- Configuration regression executable: passed, including persistence, stale/concurrent writes, layout/overlay isolation, source validation, and stable/beta update selection.
- Browser fixture: all six destinations at 1920×1080, 2560×1440, 1280×800, 820×1180, and 390×844; no horizontal document overflow.
- Browser workflows: stream search/edit retention across navigation, discard and save feedback, named deletion cancel, unsaved sign-out cancel; new layout from preset, save without wall switch, explicit wall apply; PiP inspector tabs and source framing; MQTT/Tapo/priority access; settings categories; support dialog and original destinations; sidebar collapse/persistence.

Browser tests use synthetic images and in-memory APIs. They do not contact cameras or modify the installed host. Live installation/playback acceptance remains with the camera-wall host. The release is beta/prerelease only; stable is not promoted.
