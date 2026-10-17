# Application patches

Small dashboard and Windows application fixes can share a release version, with an independent patch revision. For example, `1.0.48-patch.1` and `1.0.48-patch.2` both belong to release `1.0.48`. Beta patches use an exact beta base, such as `1.0.48-beta.2-patch.1`. Revisions must increase; never replace an existing artifact with different bytes.

## User controls

Settings → Updates has **Automatically install patches during scheduled restarts**, off by default. This is independent of the wall notification preference. Turning it off preserves daily checks and **Install patch now**, and prevents a prepared automatic download from being handed to the installer. An installer that has already started cannot be canceled by changing this preference.

Automatic installation uses the enabled **Live View** schedule in Settings → Maintenance. When a patch is available, that occurrence installs the patch and restarts the whole application, including the dashboard. No enabled Live View schedule means no automatic installation. Windows host schedules retain their existing reboot behavior and take priority when both schedules coincide. Skipped or missed runs do not trigger catch-up installation. A deliberately stopped viewer stays stopped.

Checks continue with the browser closed. Routine automatic installation has no release badge, progress window, or UAC fallback. It requires the enabled maintenance helper with patch support. Missing helper, channel changes, verification failures and failed preparation leave the patch uninstalled; the result is visible in Settings. Full release upgrades and channel switches still require explicit confirmation. Streaming resolver updates retain their separate existing policy.

The preference is host-local in `patch-updates.json`, outside portable stream configuration. Missing or damaged preferences disable automatic installation. Installed identity and availability are shown in Updates; these patches are quiet, not untraceable.

## Publication

One normal bootstrap release is needed to distribute this updater and its updated maintenance helper. Older installations do not recognize patch identities.

Use the existing Windows release workflow with a patch identity (or push its `v` tag). The same build and regression gates run. Patch artifacts are always GitHub prereleases with `--latest=false`; they do not replace the advertised Stable release or normal Beta selection. The application separates patch discovery from full release discovery. A patch only installs on its exact base version and channel and must have a higher patch number.

The first implementation ships the full verified installer as the patch package, including dashboard assets. It avoids a new advertised base version; it does not reduce download size or hot-swap the running dashboard. Existing installer verification, protected service staging, trusted repository selection, hash and size checks, and component version checks still apply. The privileged helper independently retrieves and verifies the patch; it does not trust a user-supplied installer path. This uses the existing GitHub release trust model, not a new package signing system.

Application changes briefly disconnect streams and the dashboard during the scheduled window. Installer recovery is unchanged; this feature does not add transactional binary rollback. Changes requiring incompatible configuration migrations or a new compatibility baseline should be a normal release.

Local checks use synthetic release metadata and installer bytes, plus injected scheduler callbacks. They never install an application patch or restart the host. Before publication, test a bootstrap install and successive patches on a disposable Windows host, including unavailable helper, failed download, opt-out during preparation, restart recovery and full-version upgrade afterward.
