> Historical audit snapshot. The approved RTSPView bridge release and publication record in [bridge-release.md](bridge-release.md) supersedes the status and update-channel descriptions below.

# Proposed Git history cleanup — approval required

No rewrite, ref deletion, garbage collection, commit, or push was performed during this audit. Deleting or sanitizing a current file does not remove its old versions.

## Proposed scope

1. Remove `HANDOFF.md` from all history. Its current useful content has been sanitized; save that version separately and optionally re-add it after filtering.
2. Remove `artifacts/`, `publish/`, `stage/`, `dist/` and `.toolchain/` if present in any imported ref. Detached trees currently include published executables/libraries and debug artifacts. Some first-party assemblies embed private build paths. These directories are build output, not source fixtures.
3. Replace the six private network/directory literals prepared from the pre-audit source. They occur in historical `README.md`, `HANDOFF.md`, `UpdateService.cs`, `CameraSettings.cs` and configuration tests. Replacements are privacy placeholders; old revisions may no longer run unchanged. The updated tip uses portable configuration.
4. Review and optionally replace the single author/committer identity, including tagger identities. The prepared mailmap uses a generic contributor identity. Choose an intentional public identity before proceeding; do not misattribute authorship.
5. Preserve legitimate configuration test credentials on reserved example domains and the UI fixture's fake CSRF token. No live credential has been confirmed in them. Preserve branding assets: no private screenshot was found among Git images inspected/inventoried.

Run `node tools/prepare-history-cleanup.cjs` **before committing the audit edits**, or retain the inputs already prepared. It reads the current pre-audit HEAD and writes `replacements.txt`, `mailmap` and `remove-paths.txt` into ignored `artifacts/security/history-cleanup/`. The files contain the old private literals/identity; do not commit or share them. The script does not change history. Review each input against `artifacts/security/repository-scan.json` and `object-paths.json`; expand it if remote-only findings appear.

## Procedure after approval

Keep the existing checkout and a private offline backup. Commit the reviewed fixes so they are included in the candidate repository. Reconcile remote refs before publication; this audit inspected local refs and did not fetch or review server-only pull-request refs.

Install a current `git-filter-repo` (2.47 or newer for sensitive-data-removal mode) from its official distribution. Set `$sourceRepository` and `$candidateRepository` to distinct absolute directories; the candidate must not already exist. The following is a proposed command sequence, **not executed**:

```powershell
$inputs = Join-Path $sourceRepository 'artifacts\security\history-cleanup'
git clone --no-local --mirror $sourceRepository $candidateRepository
Set-Location -LiteralPath $candidateRepository
git filter-repo --sensitive-data-removal --invert-paths `
  --paths-from-file (Join-Path $inputs 'remove-paths.txt') `
  --replace-text (Join-Path $inputs 'replacements.txt') `
  --replace-message (Join-Path $inputs 'replacements.txt') `
  --mailmap (Join-Path $inputs 'mailmap')
```

Use no `--force` bypass on the original repository. `--no-local` transfers referenced history instead of copying the original object directory; unreferenced local objects should not enter this candidate. Verify this independently with `git cat-file --batch-all-objects`, a fresh object audit, and `git fsck --full`. Do not publish the old `.git` folder or original working-directory ZIP.

Make a normal checkout of the candidate and rerun the audit, build, HTTP security tests and rendering checks. Verify every intended branch/tag, including older releases, is sanitized; scan commit/tag messages and identities as well as blobs. Remove unneeded remote-tracking/stash refs from the publication candidate only after reviewing their contents. Filtered commit hashes change, and signed tags/commits require new signatures. Re-add sanitized handoff notes only if useful.

Prefer publishing selected reviewed branches/tags to a **new empty repository**. Do not blindly mirror-push internal refs. Replacing existing hosted history requires a coordinated force-push, collaborator re-clones, review of forks/cached commit views, and possibly hosting-provider support. Existing release installers and issue attachments are separate objects and must be audited/replaced separately. The current checkout's detached objects and old histories remain private backups until separately approved for disposal.

See the [official filter-repo manual](https://github.com/newren/git-filter-repo/blob/main/Documentation/git-filter-repo.txt) for replacement/mailmap behavior and [GitHub's sensitive-data removal procedure](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository) for hosted copies, forks and cached views.
