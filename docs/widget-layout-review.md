# Widget layout review

## Findings

- The native weather reading used two equal columns. A large temperature could exceed its half of the row and lose leading digits even though the browser preview showed the full value.
- The widget editor used a fixed 640 × 360 coordinate system instead of the saved wall dimensions. Its margins and natural-height previews differed from the Layouts canvas.
- Placement depended on dragging or percentage controls buried in the content dialog. Very small cards were easy to create unintentionally.
- Aircraft photo reservation divided compact data/photo space equally, compressing the identity unnecessarily.

## Implemented behavior

- Native weather allocates the icon its measured width and fits the entire temperature into the remaining space. Browser weather uses the same typography limits. Portrait cards stack the reading, and attribution keeps its own footer space. Text alignment preferences remain available.
- Content dialogs use the actual output dimensions, including portrait layouts. Position controls are consolidated in the layout widget inspector.
- The inspector exposes wall-pixel X/Y/width/height, Compact/Standard/Large presets, nine alignment positions, and Fit height to content. Presets retain the existing alignment anchor. Dragging stays freeform, edge resizing stays independent, and keyboard arrows move the selected widget (Shift resizes; Alt uses one-pixel steps).
- Newly added widgets start at readable sizes. Resize handles stop at 160 × 96 wall pixels; older saved geometry is retained. The inspector warns when a card is small for its content.
- Compact aircraft cards reserve more width for identity data, with a stable photo column. Two-aircraft content scaling uses a wider reference so each flight can retain data and photo space. Unknown full model names fall back to the available type code.

## Verification

Native WeatherChecks, AircraftChecks, and UiChecks passed. Browser selection/visibility and layout geometry checks passed. Visual browser checks covered complete readings at 180×160, 320×180, 530×350, 180×400, 640×120 and 160×96, edge resizing without changing the other dimension, preset/alignment placement, data refresh, and matching canvas/dialog geometry. Native render checks cover 37°F, -12°F and 105°F, with an exported image matrix in artifacts/weather/widget-layout-matrix.png.

Local review page: run node tests/widget-layout-review-fixture.cjs and open http://127.0.0.1:5211/. This fixture uses sample feeds and cannot update MAIN-CAM-MONITOR.

These changes are not released or installed on MAIN-CAM-MONITOR. Host rendering has not been verified in this review.

## Follow-up: text hierarchy and photo fitting

- High/low weather readings now use 1.1 times the configured text size (previously 0.6), with larger condition, metric and attribution text. Short cards shrink the primary reading as needed before discarding high/low values. Browser and native size checks confirm the high/low row remains present across the six test dimensions.
- Aircraft fitting retains a photo while removing optional details, then expands the photo into the remaining area. Narrow columns can still prioritize readable identity text. Native measurements are invalidated before re-fitting to avoid making decisions from stale dimensions.
- Native presentation checks include a loaded photo in a 620 × 220 card and confirm the photo's ancestor chain remains visible. Existing compact-data, fade, and four-design photo-arrival checks pass. Browser photo-arrival checks also preserve text geometry.
- Photo lookup/download for the particular aircraft shown on MAIN-CAM-MONITOR has not been verified; this addresses the demonstrated layout suppression path.
