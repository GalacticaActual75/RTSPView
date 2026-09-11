# RTSPView bridge release

RTSPView replaces the SpotMonitor product name. This bridge includes the release-readiness security fixes and moves both update channels to the public RTSPView GitHub repository. No GitHub key/token is configured, stored or sent by the updater.

- Stable bridge: 1.0.32.
- Beta bridge: 1.0.32-beta.1.
- Existing SpotMonitor AppId, executable names and scheduled-task/IPC identifiers are preserved for in-place upgrade compatibility. New installer/shortcuts/window titles use RTSPView.
- Existing camera settings, overlays, layouts, password state and channel choices remain in the existing data directory. Fresh installs have no camera URLs.
- Complete the required administrator password change locally. The dashboard now defaults to localhost; remote administration requires deliberate HTTPS/binding/AllowedHosts configuration.
- Subsequent updates use GitHub Releases over HTTPS with size/checksum verification, restrictive redirects and no credentials. Stable uses the latest stable release; Beta selects the highest beta version, excluding draft and stable releases. The local helper re-verifies before elevation/installation.

## Privacy publication plan

The old SpotMonitor repository and historical release assets stay private. A fresh publication clone receives the approved history filtering: private path/network literal replacement, removal of handoff/generated artifact history, and public GitHub noreply contributor attribution. Only reviewed source and rebuilt bridge releases will be published to GalacticaActual75/RTSPView. Existing private worktrees are preserved.

## Verification

Release build, fresh/legacy password-state checks, API setup/authentication/CSRF/host filtering checks, empty camera defaults, migration directory checks, GitHub channel/download tests, geometry checks and mocked update-helper scenarios pass locally. Rendering logic is unchanged; the owner confirms opacity works in 1.0.31. Automated opacity verification remains an environment/harness limitation. Final publication and SMB hashes will be recorded after deployment.
