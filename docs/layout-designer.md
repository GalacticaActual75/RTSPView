# Camera wall layouts

Open **Layouts** in the web admin. The current layout is marked **Live**.

- Choose a preset (1×1, 2×2, 3×3, 4×4, or Featured camera), or adjust rows and columns independently from 1 to 4.
- Drag tiles to move them or swap equal-sized tiles; drag the lower-right handle to resize by whole cells. Tiles cannot overlap. Select a tile to assign a camera or edit its cell coordinates and spans. Enter or Space selects a focused tile for keyboard editing.
- Remove tiles to leave black space. Drag an available camera onto an empty cell, or click its name to place it in the first empty cell. Each camera appears at most once in a layout.
- Duplicate and name layouts to keep different views. Up to 32 layouts can be saved. Save layouts stores inactive layouts without changing the live wall. Changes to the live layout require Apply; duplicate it first to save a separate version.
- Apply to wall saves the drafts and activates the selected layout. Discard draft restores the saved baseline. Revert last save restores the previous successful save from the current admin session, including its active layout.

All overlays follow their assigned main camera. The designer labels tiles hosting enabled overlays and reports when a layout omits their host camera. Omitted hosts hide their overlay windows. The existing Overlays page controls precise overlay framing.

The preview uses snapshots and a 16:9 wall. The viewer fills the actual window/monitor. All enabled camera players remain supervised when omitted from a layout, allowing layout switches without reconnecting unchanged streams; switching to a smaller layout does not reduce background decoding load. Newly added camera entries are disabled until configured.

## Compatibility

Schema 15 adds `Layouts` and `ActiveLayoutId`. Existing settings migrate to a Default 3×3 layout with the same nine camera references. Sixteen main camera entries are available independently of tile positions. Main stream IDs 1–9 remain unchanged, overlay IDs 10–25 remain reserved, and the seven additional main cameras use IDs 26–32. Display numbers 1–16 are separate from these stable stream IDs. Camera credentials, thumbnails, telemetry, and restart routing stay associated with the stream ID.

The authenticated, CSRF-protected `PUT /api/layouts` endpoint validates all layouts before using the existing serialized configuration save path. Invalid references, duplicate camera assignments, overlapping/out-of-bounds tiles, duplicate layout IDs, and missing active layouts are rejected. The former camera-credential swap control is replaced by layout editing.

Export a configuration before installing this version on a host if downgrade testing is planned: older applications reject schema 15 files. Before the first schema-15 save, the application preserves the old file as `%LOCALAPPDATA%\SpotMonitor\settings.json.before-layouts.json`. To roll back, stop SpotMonitor, retain a separate copy of the current settings, restore that pre-layout file as settings.json, and install the older release. The old release cannot import a schema-15 configuration.

## Verification

- Release solution build and ConfigurationChecks, including legacy migration, camera identity preservation, disabled added cameras, 4×4 persistence, and invalid layout rejection.
- JavaScript syntax checks.
- Isolated browser fixture: `node tests/layout-designer-fixture.cjs`, then open `http://127.0.0.1:5097`. It serves synthetic snapshots and in-memory API responses; it never starts the Controller or reads installed settings.
- Browser checks: preset/duplicate/save/apply/revert/reload, numeric collision rejection, pointer movement, equal-sized tile swapping, and resizing, and mobile width without horizontal overflow.

Native LibVLC playback, HWND visibility through layout switches, and 16-stream GPU/CPU load still require validation on the camera-wall host before release.
