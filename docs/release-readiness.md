> Historical audit snapshot. The approved RTSPView bridge release and publication record in [bridge-release.md](bridge-release.md) supersedes the status and update-channel descriptions below.

# Public-release security and privacy audit

Audit date: 2026-09-10. Baseline: `0e79475`. **Release status: ACTION REQUIRED.** Current-source fixes are implemented; historical privacy cleanup and final visual/deployment verification remain outstanding. The owner confirms opacity works in released version 1.0.31; the audit did not change rendering logic. No history was rewritten and nothing was published.

## Changes made

- Fresh administrator initialization now stores only a salted hash of the initial `admin` password, with persistent `PasswordChangeRequired` and session-version state. Setup sessions can access only session/password/logout APIs. Changing the password revokes other sessions and requires login again. Legacy hashes remain readable but require a password change. Readable initial-password files are removed.
- Retained the existing .NET PBKDF2 implementation and increased new hashes to 600,000 SHA-256 iterations. Password changes are serialized, validate the current password, and require a different 12–1024-character password. Account-wide throttling reserves attempts before hashing, preventing simultaneous requests or IP rotation from bypassing the five-attempt/five-minute limit. This work factor follows the [OWASP password storage guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).
- Verified and tested that **all fresh-install camera URLs are empty**, including legacy fields, main cameras and overlays. Existing installations keep their configured streams.
- Controller now defaults to loopback TCP 5080, validates request hosts, suppresses raw framework request logging, returns generic unexpected-error responses and adds frame/content-type/referrer protections. Existing authentication and CSRF requirements remain on administrative routes.
- Restricted command and telemetry pipes to the current Windows user. Shell actions are fixed allowlisted operations; update installer names are constrained to the configured directory and checksums verified. Authentication must be completed before camera configuration, logs, thumbnails, imports, updates or system commands are available.
- Web export now removes URL credentials, queries and fragments. Runtime camera configuration remains available to the authenticated administrator because the editor needs it. URLs, sensitive field text, personal paths, email addresses and line breaks are sanitized in application logs; log downloads also sanitize legacy log content. Update helper no longer records raw transcripts/exception dumps.
- Replaced the private update shares with configurable directories; unified Controller/Viewer storage through `SPOTMONITOR_DATA_DIR`. Replaced hard-coded private camera-host detection with an explicit Composite stream compatibility option. Sanitized private references in documentation.
- Expanded `.gitignore`, added placeholder-only `.env.example`, rewrote the README, clarified the actual process model, removed unsafe workflow input interpolation and added HTTP security checks to CI.
- Added compiler source-path mapping and disabled Release debug symbols. Self-contained Controller/Viewer publication succeeded; eight first-party output assemblies/executables were checked for private build paths and PDB inclusion, with none found. Old generated output is not suitable for publication.

## Sensitive data found in the current tree

Before edits, `HANDOFF.md` contained private development directories, update-share locations, a camera-server address and a personal repository reference. `README.md` and `UpdateService.cs` contained the private update shares. `CameraSettings.cs` and configuration tests contained the private camera-server address. These references have been removed/replaced in the working tree.

No confirmed live password, API token, signing/encryption key, database credential or customer dataset was found in tracked source. Reserved-domain test URLs intentionally contain fake credentials; the browser fixture intentionally returns a fake CSRF token. Scanner matches in test code and cleanup regular expressions are not evidence of live credentials.

Ignored local build/package directories contain older assemblies, PDBs and images. They have been inventoried, not deleted. The local icon-verification images inspected are small branding icons. Do not publish the working directory as a ZIP: publish reviewed tracked source and fresh release artifacts only. Runtime settings/backups, logs, thumbnails and DPAPI material remain private operational data outside source control. This audit did not open or alter the installed application's private runtime settings.

## Sensitive Git history

The checkout is not shallow. Inspected all 71 available commits, 48 tag refs, local and remote-tracking branches, stash/reflog references, and all 2,612 stored Git objects, including 1,744 objects outside the 868-object reachable/reflog inventory. This includes 39 annotated-tag objects, 1,918 blobs and 584 trees. Metadata scans flagged author/committer/tagger identifying information; only one distinct commit author/committer identity was present.

Historical candidate file families:

| Location | Finding | Required disposition |
| --- | --- | --- |
| `HANDOFF.md` (including detached revisions) | Private paths, network details, repository identity | Remove historical file; optionally re-add sanitized notes. |
| `README.md` | Private update share paths | Replace historical literals. |
| `src/SpotMonitor.Controller/UpdateService.cs` | Private update server/share defaults | Replace historical literals. |
| `src/SpotMonitor.Core/CameraSettings.cs` | Private camera-server address | Replace historical literal. |
| `tests/SpotMonitor.ConfigurationChecks/Program.cs` | Private server address plus fake example-domain credentials | Remove private address; retain useful synthetic fixtures. |
| `tests/layout-designer-fixture.cjs` | Fake fixture token | Retain; not a live authentication secret. |
| Detached `publish/Controller/SpotMonitor.*.dll`, `publish/Viewer/SpotMonitor.Viewer.dll` and unmapped first-party binaries | Embedded developer build paths | Exclude detached objects and all historical build output from publication. |
| Detached `publish/**` dependency files | Dependency build paths, author/license strings, version-like IP matches and SSH parser key-header strings | Exclude generated tree. No complete private key was confirmed in the SSH parser libraries. |
| Commit/tag metadata | Personal identity | Review intended public attribution and prepared mailmap. |

The complete redacted location/object inventory is generated under ignored `artifacts/security/`: `repository-scan.json`, `object-paths.json`, `image-inventory.json` and `ignored-private-candidates.json`. These identify candidates without printing matched secret values. The object-path map includes aliases from detached subtrees; not every alias is a historical repository-root path.

There are 1,365 binary blobs. All were inventoried and scanned for printable sensitive strings, including null-interleaved strings. Thirty named PNG blobs, four ICO blobs and one otherwise-unmapped PNG were identified; the unmapped PNG is a branding source with a chroma background. Reachable images are branding assets. No camera screenshot/private photo was found in the Git image inventory. Compressed/embedded payloads and all third-party binary resources cannot be conclusively certified by string scanning.

## Credentials to rotate

**No specific live committed credential was confirmed.** No blanket rotation list is asserted. Fake credentials on reserved example domains and SSH library key-format markers do not require rotation. If any fixture value was reused as a real camera/admin credential, rotate that real credential before publication. Any additional credential discovered during remote/release-asset review must be treated as compromised even if deleted from current source.

## Git history cleanup required

See [the proposed cleanup procedure](history-cleanup.md). The preparation script generated six private literal replacements, one identity mapping and a build-output/handoff removal list in ignored artifacts. It did not run `git-filter-repo`. Review the scope before a rewrite. Deleting current files does not remove prior commits, and rewriting commits does not erase existing downloaded installers, forks or issue attachments.

## API, deployment and dependency review

All mapped administrative API routes were exercised without authentication and during forced setup. They rejected requests with 401 and 403 respectively; missing CSRF tokens are rejected. Slot IDs are bounded and never used as arbitrary paths. Imports are limited to 2 MB and validate schema/RTSP schemes. Custom SVGs are reduced to validated inert path geometry. No SQL database, arbitrary shell-command endpoint, server-side SVG markup upload, websocket endpoint, permissive CORS registration or directory listing was found. UI camera/layout labels use text/DOM APIs; interpolated geometry/options are constrained. This is a code review and targeted test suite, not a formal penetration-test certification.

RTSP destinations are intentionally administrator-configured; admins can connect cameras on private networks. That capability is part of the application's trust model. Camera URLs can include secrets in path segments that generic export redaction cannot recognize, so even sanitized exports must remain private. Same-user processes and local administrators remain trusted.

No Docker configuration exists; the WPF application requires Windows desktop graphics. Installer elevation is required for Program Files, firewall and scheduled-task setup. The existing firewall rule allows TCP 5080 from LocalSubnet on all profiles; loopback binding prevents remote access by default. HTTPS and explicit AllowedHosts configuration are required before choosing LAN administration. Installer/update scripts retain fixed application and scheduled-task names; test on a clean Windows account before release.

NuGet advisory query against `https://api.nuget.org/v3/index.json`, including transitive packages, reported no vulnerable packages for all four solution projects. Direct packages reviewed: LibVLCSharp.WPF 3.10.0, VideoLAN.LibVLC.Windows 3.0.23, and System.Diagnostics.PerformanceCounter 8.0.1. They are used in production code; no unused direct dependency or test framework shipped as a production dependency was identified. No blanket upgrades were made. [VideoLAN's 3.0.23 release notes](https://www.videolan.org/vlc/releases/3.0.23.html) describe its security fixes; native component coverage may exceed NuGet's advisory coverage. The local build runtime is .NET 8.0.29. Rebuild self-contained releases when runtime/native advisories change.

## Validation and remaining risks

Passed: Release solution build with zero warnings/errors; configuration checks; real-HTTP admin security checks; shape editor and layout preset JavaScript checks; five update-helper scenarios; four Windows layout-visibility scenarios; self-contained publication of both applications; first-party published path/PDB scan.

**Automated-check limitation:** the existing opacity renderer test did not pass in this execution environment. Initial sandbox runs could not sample screen pixels; a retry outside the sandbox still failed rendering/color assertions (snapshot capture passed). The owner confirms that opacity works in released version 1.0.31. Rendering code and the opacity test were not changed by this audit, so these results do not establish a product regression or justify changing working opacity behavior. Reconcile the harness/desktop conditions separately; the full automated suite cannot be labeled green here. Browser UI verification was blocked by `ERR_BLOCKED_BY_CLIENT` for the local test page; backend tests passed but the visual first-login flow remains to be checked in a normal browser.

Other remaining actions: approve/rewrite/rescan history and identities; inspect remote-only refs, GitHub issue attachments, release descriptions/assets and cached/forked copies; replace prior installers that may embed developer paths; verify clean-user installation and HTTPS deployment; configure publisher signing/trusted update distribution; choose a project license and review bundled dependency redistribution notices. Checksums alone do not authenticate an attacker-writable update share. Persistent camera credentials are plaintext and require Windows ACLs/encrypted backups. A deliberate attacker can trigger the account-wide login lockout; wait five minutes or use local recovery.

## Release checklist

| Check | Status |
| --- | --- |
| Current source contains no secrets | PASS — no confirmed live secrets; synthetic fixtures retained; heuristic audit limitations apply. |
| Git history contains no secrets | ACTION REQUIRED — private metadata/paths remain; no rewrite performed. |
| Screenshots/private images removed | PASS for inspected Git inventory — no private screenshots identified; hosted attachments remain unverified. |
| Personal paths removed | PASS in current source/new first-party release output; ACTION REQUIRED for old history/assets. |
| `.gitignore` appropriate | PASS. |
| `.env.example` safe | PASS — optional defaults/placeholders only. |
| Admin authentication secure | PASS for tested application controls; configure HTTPS for LAN exposure. |
| Default password forces change | PASS — persistent API gate, session revocation and tests. |
| Fresh camera URLs empty | PASS — every main/overlay/legacy default checked. |
| Logs sanitized | PASS for reviewed application paths and tested URL/password cases; operational logs still private. |
| Production errors sanitized | PASS for unexpected HTTP failures and reviewed update/viewer paths. |
| Dependencies reviewed | PASS for NuGet query and direct-package review; monitor native/runtime advisories. |
| Tests passing | ACTION REQUIRED for full automated validation — security/build checks pass; opacity works in 1.0.31 per owner, but this environment's opacity harness fails; browser visual check blocked. |
| Docker/deployment portable | PASS for configurable paths; Docker not applicable; clean-host installer validation pending. |
| README updated | PASS. |
