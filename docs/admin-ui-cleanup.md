# Admin UI cleanup (beta)

All five admin pages share a 1320px maximum width, aligned page headers, compact host/version/time metadata, and common card, control, focus and status styles. The existing charcoal/slate palette and blue/green accents remain.

- Overview and Cameras retain their desktop three-column grids and existing preview sizing. Camera health, measurements and errors use one presentation. Expanded camera settings do not stretch neighboring cards into empty boxes.
- Layouts retains its toolbar, canvas, inspector and all drag, swap, resize, preset and assignment logic. Selection, handles, destructive actions and the available-camera empty state have clearer styling.
- Overlays uses a narrow inspector with collapsible status, placement, appearance, shape/mask, framing and connection sections. Existing input nodes and event handlers are retained. Preview modes expose their selected state to assistive technology.
- System groups LAN controls and copyable URLs, five display toggle tiles, update versions/channel/status, backups, maintenance, logs and security. All existing warnings and confirmations remain. LAN copying supports secure contexts and a selection-based fallback for HTTP.

Only frontend presentation files change. No production endpoints, request payloads, configuration models, viewer code, streaming behavior or editor coordinate calculations change.

## Validation

`node tests/ui-preview.cjs` serves the shipped frontend at `http://127.0.0.1:5099` with synthetic camera images, in-memory settings and representative telemetry. It never changes host settings or contacts cameras. Stop it with Ctrl+C. This fixture is not included in the installer.

Browser QA covered Overview, Cameras, Layouts, Overlays and System at desktop, tablet and phone widths. At 1440px all five page containers measured 1320px and titles had the same horizontal position. No horizontal page overflow was found at 390px. Keyboard navigation showed visible toggle focus outlines. Checks included expanded camera settings, layout discard state, custom SVG controls and shape dialog, source framing, overlay save payloads, display save payloads and LAN copying. Selecting another update channel issued only the channel request; installation still required its separate confirmation dialog, which was canceled.

Release build, administrator security, LAN firewall mock checks, shape editor, layout presets, branding and JavaScript syntax checks passed. Visual checks used synthetic previews, not live camera feeds.

## System tabs

System groups existing controls into Viewer, Network & security, Updates, Backups, Maintenance, and Logs tabs. Cards and controls are moved intact, preserving unsaved edits and existing save handlers. Tabs use ARIA tab/panel relationships, one keyboard tab stop, Left/Right cycling, and Home/End navigation. The tab row wraps on narrow screens. This does not change routes or make tab selection save settings. Maintenance contains the independent viewer and Windows host restart schedules.
