# RTSPView branding audit

The audit covered all tracked source text and filenames: dashboard, WPF windows/resources, update staging and elevated progress UI, installer, launcher/maintenance scripts, generated assembly display metadata, build/release workflow, tests and current documentation. Existing branding images are camera symbols; their old resource filenames were renamed.

## Corrected

- The progress heading now reads **Updating RTSPView**. The periodic installer-progress message and all up-to-date messages use RTSPView.
- Launcher messages, task description, maintenance text and test-window captions use RTSPView.
- Solution, projects, namespaces, resource filenames, branding-generation paths, launcher filenames and release-build variables use RTSPView.
- Executable ProductName and FileDescription both report RTSPView.
- New shortcuts use the RTSPView group. Upgrade cleanup removes known old launcher files, old shortcuts and superseded library files; it does not delete user settings or arbitrary folders.
- Session/CSRF cookie names, IPC names, viewer mutex and new log filenames use RTSPView. Existing passwords remain valid; signing in again may be required after the cookie-name change.

## Deliberate compatibility references

A literal zero-reference replacement would break installed upgrades. These identifiers remain explicitly allowlisted in `tests/branding-compatibility.json`:

- `SpotMonitor.Controller` and `SpotMonitor.Viewer` output filenames/process identities: installed update helpers verify these exact files and wait for these exact processes. Projects/namespaces and Windows display metadata use RTSPView.
- `SpotMonitor Camera Wall` scheduled-task identifier: installed helpers stop/start this task by name. Its description uses RTSPView.
- The legacy data folder, environment-variable alias and data-protection application identifier: retained to read existing private settings and key material correctly.
- Old firewall, launcher, shortcut and library names appear as cleanup targets, and compatibility tests cover them.

The three earlier audit/publication reports retain historically accurate names and paths. Published Git history and old release assets were not rewritten or silently replaced for a branding change. This report is also an explicit documentation exception. The active UI has no old-branding allowance.

## Validation

Passed: Release build with zero warnings/errors; configuration and GitHub updater checks; real HTTP administrator-security checks; LAN listener/persistence/host-filter checks; update-helper success/failure scenarios; four Windows layout-visibility checks; executable display-metadata inspection; repository branding check. `node tests/branding.checks.cjs` is included in release CI to detect unexpected old branding and old tracked filenames.

The previously reported opacity harness limitation remains; rendering algorithms were not changed. Full interactive installation on the user's remote host was not performed.

## Upgrade-window detail

An update is launched by the version already installed on the host. While upgrading from 1.0.34 or earlier, an already-open progress window can still show the old heading. The newly installed progress script fixes subsequent updates; installing a new version cannot retroactively replace text in a window already created by the old script.
