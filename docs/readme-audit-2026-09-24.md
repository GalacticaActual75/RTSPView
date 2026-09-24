# README documentation audit — September 24, 2026

## Scope and baseline

The target is the public RTSPView repository, not the older parent workspace. GitHub reported the latest published stable release as **v1.0.46**, published `2026-09-24T00:45:11Z`. Its annotated tag resolves to commit `355d403b42119e05da222453026601334665bbc4`. This documentation change is based on that tag on branch `codex/readme-1.0.46-audit`.

At audit time, remote `main` was `31908001d526e5ea4b148bdd5ac9fee04b77577f`, with a 1.0.45 build version and older application code. The published 1.0.46 tag contains substantial functionality absent from main, including the current sidebar, ONVIF and website-stream support. The README explicitly identifies its release baseline and source-build instructions check out that tag. **Reconcile the release branch with main before applying this README alone to main**, or its relative links and feature claims will not match that older tree.

Only README/documentation files were changed. Application code, settings, installer behavior and existing work in other checkouts were left untouched. Nothing was published or installed.

## Changes made

- Replaced accumulated version-by-version material with overview, grouped features, installation, quick start, UI map, daily use, integrations, configuration, recovery, security and development.
- Reduced the README from roughly 10,000 words to roughly 4,100, preserving advanced MQTT guidance in [mqtt-automation.md](mqtt-automation.md).
- Replaced stale Stable 1.0.45/Beta 1.0.46-beta.5 recommendations with a latest-stable link and an explicitly dated 1.0.46 baseline.
- Corrected the no-discovery claim, RTSP-only framing, old overlay labels, floating support-button description, index-only monitor description and duplicate raw-overlay decoder claim.
- Brought ONVIF, website source types/quality/testing, automatic signed helper updates, named displays, shared overlay frames and current sidebar behavior into the main feature/workflow sections. These were absent or inconsistently represented in the original README; some were already mentioned in scattered later paragraphs.
- Kept Tapo, MQTT, weather, layout framing, Full exit, priorities, optional maintenance and scheduled restarts visible without presenting hardware compatibility as proven.
- Clarified exports versus private host backups, query-string removal, helper packaging requirements, runtime versus build dependencies, environment overrides and the distinct update mechanisms.
- Preserved the release-tag README's correct key-preserving administrator recovery. The older main/workspace README still recommended moving the key ring too; that instruction would endanger stored integration credentials.
- Added concise contribution and licensing guidance without inventing a project license.

## Evidence map

Paths refer to the audited release tag. Implementation was treated as authoritative; older design plans and audit prose were not used as proof that features work.

| README subject | Implementation checked |
| --- | --- |
| Installation, runtime packaging, startup/shutdown | `installer/RTSPView.iss`, `deployment/Start-RTSPView.cmd`, `Stop-RTSPView.cmd`, `Run-Appliance.ps1`, `Prepare-Installation.ps1`, `.github/workflows/release.yml`, application project files |
| First login, setup gate, session revocation, password recovery | `src/RTSPView.Infrastructure/WebSecurity.cs`, `src/RTSPView.Controller/Program.cs`, `tests/RTSPView.ConnectorChecks/PasswordRecoveryChecks.cs` |
| LAN binding, custom endpoints, subnet restrictions | `src/RTSPView.Controller/LanAccessService.cs`, `Program.cs`, `deployment/Enable-LanAccess.ps1` |
| Stream capacity, defaults, validation, source types | `src/RTSPView.Core/AppSettings.cs`, `CameraSettings.cs`, `StreamSource.cs`, `StreamCatalog.cs` |
| UI labels, save scope, sidebar, feedback | `wwwroot/index.html`, `app.js`, `layout.js`, `workspace.js`, `modern-shell.js`, `system-tabs.js`, `automation-tabs.js`, `weather.js`, `feedback.js` under Controller |
| Layout capacity, dimensions and framing | `src/RTSPView.Core/WallLayout.cs`, `AutomationLayouts.cs`, `WallVideoTransform.cs`, Controller's `wwwroot/wall-designer.js` |
| Picture-in-picture masks, placement, shared original frames | `AppSettings.cs`, Controller's `wwwroot/app.js` and `overlay-source.js`, Viewer's `RawOverlaySources.cs`, `SharedOverlayFrames.cs`, `CameraTile.xaml.cs` |
| Monitor selection, escape gesture and intentional exit | Viewer's `DisplayMonitors.cs`, `MainWindow.xaml.cs`, Controller's `wwwroot/display-settings.js`, `ViewerSupervisor.cs` |
| Snapshots, diagnostics, scheduling | `src/RTSPView.Core/SnapshotSettings.cs`, `RestartSchedule.cs`, Controller's `RestartScheduler.cs`, `wwwroot/snapshots.js`, Viewer's diagnostics and telemetry code |
| MQTT filtering, discovery, focus and priorities | `src/RTSPView.Core/Automation.cs`, `AutomationLayouts.cs`, Controller's `AutomationService.cs`, `MqttDiagnostics.cs`, `wwwroot/automation.js`, `automation-tabs.js` |
| Tapo and Scrypted | Tapo settings/service/UI, `integrations/tapo-reader`, `plugins/scrypted-rtspview`, Controller connector endpoints and tests |
| ONVIF and website resolution | `src/RTSPView.Infrastructure/OnvifClient.cs`, `StreamResolver.cs`, Controller's `OnvifEndpoints.cs`, `Program.cs`, `wwwroot/onvif.js`, `integrations/stream-resolver/resolver.py` |
| Weather and temperature | Controller's `WeatherService.cs`, weather endpoints/UI, Core weather models, `TemperatureStatus.cs`, Controller's `TemperatureMonitor.cs`, dependency UI and maintenance projects |
| Environment and private state | `src/RTSPView.Core/AppPaths.cs`, `.env.example`, Controller startup and Infrastructure's `JsonSettingsStore.cs` |
| Export/import and credentials | Controller's `ConfigurationBackup.cs` and export endpoint, `RtspUrlSanitizer.cs`, Viewer's `ConfigurationWindow.xaml.cs`, `JsonSettingsStore.cs` |
| Application versus component updates | Infrastructure's `GitHubUpdateSource.cs`, `StreamingUpdates.cs`, Controller's `UpdateMonitor.cs`, `StreamingUpdateMonitor.cs`, update deployment scripts |
| Source building and publication | `global.json`, `Directory.Build.props`, `RTSPView.sln`, `tools/checks.ps1`, `tools/publish-release.ps1`, release workflow and helper build scripts |

Tracked-file inventory found no Dockerfile/container deployment or native Android project. The desktop projects target Windows and the Viewer uses WPF/Windows Forms. The README does not suggest that containers or Android can run the desktop Viewer.

## Link and screenshot results

All 11 distinct external Markdown destinations across the original and replacement README returned HTTP 200, including the old versioned releases, latest-stable redirect, issue chooser, Buy Me a Coffee, Open-Meteo and PawnIO. The issue chooser leads to GitHub's normal account flow when necessary. GitHub API independently confirmed the current release and its installer, checksum and `update.json` assets. The old release links were valid but obsolete recommendations, not broken URLs.

All original local links/anchors existed. All replacement README and MQTT-guide local links/anchors passed a second check. The finished README retains no broken link discovered in this audit.

Neither original nor replacement README embeds a screenshot. Tracked raster assets are branding/application icons and the support icon, not current UI screenshots. There is therefore no screenshot to replace or redact. No old mockup or synthetic fixture was presented as a live product screenshot. A future preview should capture the 1.0.46 sidebar, layout editor and native Viewer with synthetic feeds, then be reviewed for private details.

## Validation performed

- Restored and built `RTSPView.sln` for Release/win-x64 using .NET SDK 8.0.423: **passed, zero warnings/errors**.
- `RTSPView.ConfigurationChecks`: **passed**, including schema/defaults, source modes, overlay geometry/linking, layout validation, snapshot schedules, sanitized exports, concurrency and update-channel behavior.
- `RTSPView.ConnectorChecks`: **passed**, including key-preserving administrator recovery, session/token invalidation, pairing, imports and capacity. Initial restore hit the network restriction for NuGet vulnerability metadata; rerunning with network access succeeded, without disabling vulnerability checks.
- `tests/admin-security.checks.cjs`: **passed** against an isolated Controller, including first setup, blank URLs, password rotation, CSRF, authorization, exports/log sanitization and throttling.
- `tests/modern-admin.checks.cjs`, `tests/privacy.checks.cjs`, `tests/branding.checks.cjs`: **passed**. A draft branding check initially rejected a new generic legacy-name mention; the README was adjusted to use the already permitted migration path instead of relaxing the test policy.
- Local links/anchors, external HTTP destinations and `git diff --check`: **passed**.

No new application tests were added for this documentation-only change. The full rendering/integration suite was not rerun. The build checks application projects; it does not recreate the frozen Python helpers or release installer.

## Remaining limitations and findings for the maintainer

1. **Release/default-branch divergence:** described above. This is the largest documentation-publication risk; a README-only merge onto current main would advertise code and link to files that main does not yet contain.
2. **Possible installer account mismatch:** the autostart task is registered by an elevated `[Run]` PowerShell command using `$env:USERNAME` for `New-ScheduledTaskTrigger -AtLogOn -User`. If a standard user supplies credentials for a different administrator, that environment may identify the administrator rather than the intended Viewer user. Verify that scenario before promising startup under every initiating standard-user account. This is a source-review concern, not a reproduced installation bug; the installer was not changed.
3. **Stale supplemental documentation/UI wording:** `docs/website-streams.md` and `integrations/stream-resolver/README.md` still describe independent helper updates as unavailable/installer-only; `docs/streaming-updates.md` and implementation supersede that. `docs/temperature-monitoring.md` and `docs/scheduled-restarts.md` use older System/Overview navigation and beta-era wording. `docs/tapo-automation.md` has older Overlays/priority labels. `docs/modern-dark-admin.md` still says stable was not promoted. The shipped source-type hint in `wwwroot/index.html` says “This beta” although 1.0.46 is stable. These were flagged rather than changing unrelated guides or application text silently.
4. **Physical/integration validation:** no real camera, ONVIF firmware, Tapo hub/sensor, live Scrypted installation, temperature hardware, LAN firewall/UAC installation, or real website stream was exercised in this audit. Existing project records also mark physical ONVIF/Tapo and live connector validation pending. The README states those limitations instead of broad compatibility guarantees. YouTube/site refusal remains a known limitation; no playback fix is claimed.
5. **Screenshots:** no existing README screenshots; a current privacy-reviewed visual preview remains an optional addition, not a verified artifact from this audit.
6. **License:** no project-wide open-source license file/grant is present; GitHub reports no repository license. Existing `docs/licensing.md` says the same. The README preserves that status; choosing a license remains an owner decision.

No additional reproducible application behavior bug was established by the checks run. Configuration examples contain only application defaults, public project URLs, generic placeholders and per-user Windows variables; no live stream credentials, private host addresses or machine-specific workspace paths were added.
