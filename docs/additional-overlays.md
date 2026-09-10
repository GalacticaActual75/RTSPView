# Additional overlays

Use **+** beside the overlay tabs to add an overlay. It is saved immediately as a disabled, unconfigured entry, and the new tab opens with Connection and recovery expanded. Enter its Name and RTSP URL, enable it, and save. Adding an overlay does not discard unsaved edits in other tabs.

The Name field updates the active tab and card title as you type. Save overlay persists the name; Discard restores the saved name. This applies to the original Doorbell and Garage tabs as well as added overlays.

Up to 16 overlays are supported (the original two plus 14 added entries). Disable unwanted entries with their toggle. Each has independent source, recovery, host camera, shape/SVG, drawing, framing, opacity, snapshots, and restart controls. More enabled overlays consume additional software-decoding resources.

Added entries are stored in `AdditionalOverlays`; slots 12–25 are assigned in list order. The original properties and slots 10/11 are retained for compatibility. Existing configurations need no migration. Full configuration export/import includes added overlays, and credential-free export strips their RTSP credentials too. Invalid imports and excessive lists are rejected before replacing settings.

Stable 1.0.30 predates added overlays: it only displays the original two and will omit the additional entries if settings are saved in that version. Export the beta configuration before reverting so all added overlays can be restored later. Schema 14 is retained to let the original camera wall continue working when reverting.

Validation: Release solution build; configuration checks for maximum count, unique slot assignment, save/load/import, masks and opacity, sanitized export, legacy configurations, and rejected invalid input; shape checks; local browser checks for add, rename, preserved unsaved edits, Discard, reload, and opening the shape editor on an added overlay. Live multi-stream behavior requires camera-host beta testing.
