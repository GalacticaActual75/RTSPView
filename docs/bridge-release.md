# RTSPView bridge release

RTSPView replaces the SpotMonitor product name. This bridge includes the release-readiness security fixes and moves both update channels to the public [GalacticaActual75/RTSPView repository](https://github.com/GalacticaActual75/RTSPView). No GitHub key/token is configured, stored or sent by the updater.

- Stable bridge: 1.0.32.
- Beta bridge: 1.0.32-beta.1.
- Existing SpotMonitor AppId, executable names and scheduled-task/IPC identifiers are preserved for in-place upgrade compatibility. New installers, shortcuts and window titles use RTSPView.
- Existing camera settings, overlays, layouts, password state and channel choices remain in the existing data directory. Fresh installs have no camera URLs.
- Complete the required administrator password change locally. The dashboard defaults to localhost; remote administration requires deliberate HTTPS/binding/AllowedHosts configuration.
- Subsequent updates use GitHub Releases over HTTPS with size/checksum verification, restrictive redirects and no credentials. Stable uses the latest stable release; Beta selects the highest beta version, excluding draft and stable releases. The local helper verifies the checksum again before installation.

## History cleanup and publication

The original SpotMonitor repository and its historical release assets remain private. A separate publication clone was filtered with git-filter-repo: six private path/network literal replacements, removal of handoff/generated artifact history, and public GitHub noreply contributor attribution. The account rename to GalacticaActual75 was applied to repository references and attribution before publication. Original private worktrees were preserved.

The sanitized clone contains 51 retained commits and 799 Git objects across its reviewed local refs. All objects were scanned again; none of the six private literals remained. Its ten binary objects are branding images/icons, with no flagged sensitive strings. Remaining text scanner candidates are synthetic test fixtures and scanner expressions. No confirmed live committed credential was identified, so no specific credential revocation is claimed. The updater does not support a read-only GitHub key.

Only sanitized main and the two new release tags were pushed. Old remote releases, issue attachments, cached content and internal branch/tag refs were not copied to the public repository. Historical source commits reachable from main are preserved in sanitized form. The original repository must remain private unless its separate hosted content is reviewed and cleaned.

## Verification and remaining limits

Release build, fresh/legacy password-state checks, real HTTP setup/authentication/CSRF/host filtering checks, empty camera defaults, migration directory checks, GitHub channel/download tests, geometry checks and mocked update-helper scenarios pass. Rendering logic is unchanged; the owner confirms opacity works in 1.0.31. This environment's automated opacity test still fails, and browser visual verification was blocked. A real clean-user installation and interactive in-place upgrade were not performed on the user's camera host.

The source scan is heuristic, not a guarantee about every encoded payload. Camera credentials in private runtime settings still require Windows ACLs and protected backups. Publisher signing, a project license, and bundled dependency redistribution notices remain follow-up work. The earlier audit and cleanup proposal are retained as historical records; this document supersedes their pending history/publication status.

## Final release checklist

| Check | Status |
| --- | --- |
| Current source contains no secrets | PASS for reviewed source; no confirmed live secrets. |
| Public Git history cleaned | PASS for reviewed published history; original private archive stays private. |
| Screenshots/private images | PASS for reviewed Git inventory; only branding retained in publication clone. |
| Personal paths removed | PASS for sanitized source/history and previously checked first-party release outputs. |
| Ignore rules and example configuration | PASS. |
| Admin authentication and forced password change | PASS in automated HTTP/state tests. |
| Fresh camera URLs empty | PASS. |
| Logs and production errors sanitized | PASS for reviewed and tested paths. |
| Dependencies reviewed | PASS for direct packages and NuGet advisory query; continue native/runtime monitoring. |
| Tests passing | PASS for release/security suites; ACTION REQUIRED for opacity harness and browser visual validation. |
| Portable deployment | PASS for new/legacy directory checks; ACTION REQUIRED for clean-user installer validation. Docker is not applicable. |
| README updated | PASS. |
| Public GitHub stable/beta updates and final SMB bridge | PASS — both anonymous downloads and SMB copies checksum-verified; manifests published last. |

## Published bridge artifacts

Both GitHub release workflows completed successfully. The application's actual GitHubUpdateSource selected and downloaded each release anonymously, validating manifest, size and checksum. Those verified installers and checksum files were copied to their respective legacy SMB channels; both remote hashes passed before the manifests were atomically replaced and read back. Previous manifests were backed up privately. No installer was run on the user's host.

| Channel | Version | Installer bytes | SHA-256 |
| --- | --- | ---: | --- |
| [Stable](https://github.com/GalacticaActual75/RTSPView/releases/tag/v1.0.32) | 1.0.32 | 139301761 | `E998E6B1D6F11F73087BEB16BC5B0C7D01055C1D5906759C9515F35F6CDD6D62` |
| [Beta](https://github.com/GalacticaActual75/RTSPView/releases/tag/v1.0.32-beta.1) | 1.0.32-beta.1 | 139303350 | `644F817FD6B21EF357069FAD68F20D4439D1FD659A7ED1975406CAA3089C5F1D` |

Install the bridge once through the existing SMB update path. That installation switches subsequent checks/downloads to public GitHub Releases. Selecting a channel does not itself install anything. Existing internal SpotMonitor names are retained for upgrade compatibility, and some legacy interface wording may still refer to a camera wall or LAN update channel; the updater itself uses GitHub.

The README now documents general RTSP sources, always-on-top versus full-screen behavior, the hidden five-click escape corner, layouts, overlays, opacity, custom masks and routine operation.
