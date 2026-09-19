# UI redesign — 1.0.43-beta.4

Implemented in `codex/beta-ui-redesign`, based on the current release checkout at `00c0e3a` (1.0.43-beta.3). This includes the newer automation, temperature monitoring, scheduled restarts, maintenance helper, raw overlay sources, paired focus layouts, and fine sizing features. The older `beta` worktree was not used as a baseline because it predates those capabilities.

## Workspaces

- **Monitor** has a dedicated snapshot board following the saved active layout, a saved-layout switcher, and an All streams view. Stream connectivity and snapshot age remain separate. Host failure is summarized once. The browser is a snapshot monitor, not a live video player; native playback and temporary automation focus remain in the Windows viewer.
- **Streams** is a searchable compact inventory. Open one stream in an accessible modal side drawer. Closing retains its draft; reopening restores it. Save & apply persists through the existing authenticated endpoint. Discard restores the saved values. Deletion, restart, advanced recovery and original overlay-source editing remain available.
- **Layouts** retains standard and automation layouts, draft/save/apply, undo/redo, presets, exact geometry, sizing, and framing. Canvas configuration is in a dedicated inspector. Draft changes do not change Monitor until applied.
- **Overlays** uses a preview plus one inspector with Position, Appearance, Shape, Image and Connection tabs. Automation details and telemetry are disclosed on demand. One shape editor has Presets, Draw and Import SVG modes; custom-mask rotation remains in the Shape inspector. Shapes remain drafts until Apply to wall.
- **Automation** preserves rule configuration, event discovery, broker setup and diagnostics; rows and dividers replace nested decorative panels.
- **Settings** separates Display, Network & security, Updates, Backups, Maintenance, Diagnostics and About. Host metrics live in Diagnostics. Version, host address, source links and feedback live in About.
- **Windows viewer** has a slim viewing toolbar; connection editing lives in Streams. The native editor has a stream list, Connection fields, Streaming/Recovery expanders and Backup/restore. Drafts survive stream selection. Existing appliance mode, diagnostics, alerts and update behavior remain unchanged.

## Design system

`product.css` owns the web shell, typography, spacing, status colors, forms, buttons, tables, drawers, tabs and responsive layout. Geometry-specific editor styles remain with their editor. The previous app/compact/layout/admin-ui/dashboard stylesheets are no longer loaded. `ProductTheme.xaml` supplies matching native brushes and input/button styles.

Dialogs support keyboard focus and Escape; tab controls provide arrow-key navigation. Details reveal invalid fields. Stream edits survive navigation; existing unload/sign-out guards remain. Snapshot failure retains the last image and its capture timestamp. Stale images are labeled separately from live stream health.

## Validation

- Release build: zero warnings/errors.
- All browser JavaScript syntax checks.
- ConfigurationChecks: schema, persistence, sanitized transfer, framing, layouts, stream identities and automation.
- UiChecks: native resource loading, minimum-size rendering, stream-list selection, draft retention, composite-stream option and no premature persistence.
- LayoutVisibilityChecks: native surface visibility, playback/recovery, overlays, focus restoration, diagnostic panel, warning windows, toolbar bounds and update confirmation with fake installer.
- Administrator-security checks: protected routes, first-run gate, password/session/CSRF, sanitized exports/logs, throttling.
- Snapshot age/refresh, shape geometry, layout presets and proportions checks.
- Isolated browser fixture: stream save, search, close/reopen draft, discard, layout duplicate/save, canvas settings, shape select/apply, settings destinations, 390px responsive checks. Fixture traffic never reaches installed camera configuration.

The local test restores used cached packages with NuGet audit disabled; repository audit configuration is unchanged. Real camera-host visual and usability acceptance follows beta installation. No production deployment or live host installation was performed.

## Local build

Version defaults to the beta identity in Directory.Build.props. CI's explicit version override still controls published releases. The local installer uses separate staging under `artifacts/beta-redesign/stage`; set `RTSPVIEW_STAGE_DIR` to that absolute directory when compiling Inno Setup. No stable channel manifest is written.
