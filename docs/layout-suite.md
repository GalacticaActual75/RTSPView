# Layout suite

Standard and Automation layouts support a saved output resolution, up to 12 rows and columns, and independent image framing on every tile, including automation focus positions.

- Select a tile and choose **Fit**, **Original size**, **Fill**, or **Stretch**. Original maps source pixels to the selected output resolution, centered and clipped to the tile. Fit keeps the whole image; Fill crops to cover; Stretch changes proportions.
- Use **Zoom** (25–400%), horizontal/vertical position, and **Reset image framing**. **Drag action → Reposition images** pans within a tile. **Move tiles** rearranges tiles. Edge and corner handles resize; arrow keys move and Shift + arrows resize.
- Choose an available camera, then click an empty cell or drag across an empty rectangular area. Camera drag-and-drop remains available. **Fill available space** expands from the tile's top-left corner into the largest free rectangle.
- Output presets and custom dimensions (240–16384 pixels per side) scale the preview and display the selected tile's output dimensions. This defines the wall's aspect and reference pixels; it does not change the Windows display mode.
- Shared grid proportions, optional stream fitting, automation focus selection, and preview-camera choices remain available. Undo/redo covers draft edits. Save/Apply commits changes.

In 1.0.44-beta.3 and newer, new tiles default to Fit in both layout editors; existing explicit sizing is preserved. Earlier releases defaulted to Original size. Use **New layout** for a named blank canvas or preset, and **Add stream** to populate it.

Original-size previews use live source dimensions. When unavailable, they fall back to Fit with an explanatory note. Snapshots show framing, not live-video detail. The actual camera host still needs visual verification across its display/GPU configuration.

Build and configuration checks cover persistence, all four sizing transforms, zoom, position and custom resolution. Preset/proportion regression checks cover shared grid sizing.
