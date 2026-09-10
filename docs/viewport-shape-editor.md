# Browser viewport shape editor

In Overlays, open Doorbell or Garage and select **Choose or draw shape** under Appearance.

- Choose rectangle, square, rounded rectangle, rounded square, circle, oval, triangle, diamond, or hexagon.
- Select **Draw outline**, then drag one continuous outline over the camera snapshot. Releasing closes the shape. Mouse, pen, and touch are supported.
- Adjust **Smoothing** from 0 to 100%. Smoothing always uses the original stroke during this editing session. After saving and reloading, the resulting SVG outline is retained; the original mouse samples are not stored.
- **Undo** restores the previous shape choice or stroke. **Cancel** leaves the overlay unchanged.
- **Use shape** updates the unsaved wall preview. **Save overlay** applies it to the viewer.

The drawing preview uses the current viewport aspect ratio, video zoom, and image position. Drawn coordinates preserve their placement within that viewport, including space outside the outline. Drawing is one closed contour; uploaded SVG remains available for complex masks and holes. Existing SVG masks display in the editor with their rotation; drawing a replacement starts unrotated.

All existing viewport sizing, position, opacity, rotation for custom masks, framing, and SVG upload controls remain. Rounded square creates a custom rounded mask and adjusts the initial dimensions to a square; independent width/height controls can subsequently stretch it. Other custom presets use the current width and height.

This is an admin UI change using the existing path-only mask configuration and renderer. No settings schema or playback pipeline changes are required. Paths are bounded to roughly 600 samples to stay below the existing mask length and complexity limits.

Validation: Release solution build and configuration checks; `node tests/shape-editor.checks.cjs` verifies closed/bounded paths, smoothing wobble reduction, degenerate strokes, and extreme-stroke storage limits. Local browser fixture verified mouse drawing, smoothing, save/reopen, Cancel, Undo, presets, both overlays, and narrow phone layout. Real camera-host testing remains the beta acceptance check.
