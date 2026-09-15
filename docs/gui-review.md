# GUI review — pending beta follow-up

Reviewed Overview, Streams, Layouts (both editors), Overlays, Automation (including an expanded rule), and all six System tabs using the isolated browser fixture plus source inspection.

## Changes

- Layouts: collapse Focus setup and Grid settings by default so the canvas is easier to reach. Keep the selected-tile inspector visible.
- Manage layout: dismiss on outside pointer click, keyboard focus leaving the menu, and Escape; Escape restores focus to the summary.
- Shared controls: wrap action rows and layout toolbars, constrain popover width, respect hidden panels, and provide visible keyboard focus for expandable sections.
- Layout inspector: stack below the canvas at narrower widths rather than squeezing both columns.
- Keep advanced MQTT discovery, exact topics, overlay framing, and system operations in their existing named sections. Preserve settings and existing save behavior.

## Verification and limits

Browser: visited all main pages and System tabs; inspected expanded automation rule; visually inspected the cleaned layout page; verified outside-click and Escape dismissal. Fixture uses synthetic images and no live cameras. Some host-only services intentionally return unavailable responses; live service configuration was not exercised. No mobile-device or installed-host visual verification was performed.

JavaScript syntax, layout geometry/configuration checks, and solution build pass. Doorbell full-frame rendering has a decoded-video regression test. Overlay placement now anchors to the host's visible aspect-fit picture without changing overlay size, with letterbox/pillarbox geometry checks. Installed-host confirmation of the driveway overlay remains necessary.

## Canvas-first follow-up

The layout page now opens directly onto the canvas. Add camera opens a searchable drawer; Presets, Advanced, and Help share the same drawer. Select a tile to change its camera or use Exact position and size. Focus tiles offer Preview camera directly on the canvas; focus positions are moved visually, and normal tiles can take a focus role from their inspector. Grid dimensions, screen format, and focus count are under Advanced. Discard, rename, duplicate, delete, and revert are in the compact Manage layout menu. Right and bottom edge handles supplement the existing corner resize handle.

Verified in the browser fixture: focus preview selection, camera search, adding a camera, Undo, drawer dismissal, and the canvas-first default view. The fixture does not replace installed-host testing.
