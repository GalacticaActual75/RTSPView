# Camera wall layouts

Open **Layouts** in the web admin. The current layout is marked **On wall**.

- Choose **Screen format**: Landscape (16:9) or Portrait (9:16). Changing format transposes the grid and tile positions/spans without rotating camera images. Each saved layout remembers its format; existing layouts default to landscape.
- Open **Layout presets** for Single, Split, Quad, Six, Nine, Sixteen, Featured, Sidebar, Cinema, Dual focus, Center stage, or Strip. The visual previews adapt to the chosen format. Presets replace the draft arrangement, use registered cameras only, and leave any remaining cells empty. Use Discard changes to undo before applying.
- Rows and Columns are always visible beside Screen format and can be adjusted independently from 1 to 4.
- Drag tiles to move them or swap equal-sized tiles; drag the lower-right handle to resize by whole cells. Tiles cannot overlap. Select a tile to assign a camera or edit its cell coordinates and spans. Enter or Space selects a focused tile for keyboard editing.
- Remove tiles to leave black space. Drag an available camera onto an empty cell, or click its name to place it in the first empty cell. Each camera appears at most once in a layout.
- Use **Manage** to name, duplicate, or delete layouts. Up to 32 layouts can be saved. **Save layout** stores inactive layouts without changing the live wall. Changes to the live layout require Apply; duplicate it first to save a separate version.
- **Apply to wall** saves the drafts and activates the selected layout. **Discard changes** restores the saved baseline. **Manage > Revert last save** restores the previous successful save from the current admin session, including its active layout.

The Cameras page initially shows the existing nine entries. **Add camera** adds one entry at a time, up to 16, and opens its settings without discarding edits in other cards. Configure the new camera, then assign it in Layouts. Existing configured or assigned extra cameras remain available after upgrading.

All overlays follow their assigned main camera. The designer labels tiles hosting enabled overlays and reports when a layout omits their host camera. Omitted hosts hide their overlay windows. The existing Overlays page controls precise overlay framing.

The preview uses snapshots and the saved screen format. The viewer centers the wall at the same aspect ratio inside its available window/monitor area, with black margins when the screen has different proportions. A matching portrait monitor fills with a portrait layout; the format setting does not rotate Windows or the source camera images. All enabled camera players remain supervised when omitted from a layout, allowing layout switches without reconnecting unchanged streams; switching to a smaller layout does not reduce background decoding load. Newly added camera entries are disabled until configured.

## Compatibility

Schema 15 adds `Layouts` and `ActiveLayoutId`. Existing settings migrate to a Default 3×3 layout with the same nine camera references. `CameraCount` tracks registered entries independently of the sixteen-player capacity and tile positions. Main stream IDs 1–9 remain unchanged, overlay IDs 10–25 remain reserved, and the seven additional main cameras use IDs 26–32. Display numbers 1–16 are separate from these stable stream IDs. Camera credentials, thumbnails, telemetry, and restart routing stay associated with the stream ID.

The authenticated, CSRF-protected `PUT /api/layouts` endpoint validates all layouts before using the existing serialized configuration save path. Invalid references, duplicate camera assignments, overlapping/out-of-bounds tiles, duplicate layout IDs, and missing active layouts are rejected. The former camera-credential swap control is replaced by layout editing.

Export a configuration before installing this version on a host if downgrade testing is planned: older applications reject schema 15 files. Before the first schema-15 save, the application preserves the old file as `%LOCALAPPDATA%\SpotMonitor\settings.json.before-layouts.json`. To roll back, stop RTSPView, retain a separate copy of the current settings, restore that pre-layout file as settings.json, and install the older release. The old release cannot import a schema-15 configuration.

## Verification

- Release solution build and ConfigurationChecks, including legacy migration, camera identity preservation, disabled added cameras, 4×4 persistence, and invalid layout rejection.
- JavaScript syntax checks.
- `node tests/wall-layout-presets.checks.cjs`: all twelve templates, both formats, multiple camera inventories, coverage, identity, collision/bounds checks, and reversible transposition. ConfigurationChecks also validates format persistence, rejection, and viewer fit calculations.
- Isolated browser fixture: `node tests/layout-designer-fixture.cjs`, then open `http://127.0.0.1:5097`. It serves synthetic snapshots and in-memory API responses; it never starts the Controller or reads installed settings.
- Browser checks: preset/duplicate/save/apply/revert/reload, numeric collision rejection, pointer movement, equal-sized tile swapping, and resizing, and mobile width without horizontal overflow.

- `dotnet run --project tests/RTSPView.LayoutVisibilityChecks -c Release` exercises real WPF/LibVLC status-window visibility across 9, 16, 1, and 9 tile layouts without starting streams or reading installed settings.

Native stream playback and 16-stream GPU/CPU load still require validation on the camera-wall host. Hidden main tiles now explicitly hide their detached status windows to prevent cascading Disabled labels.
