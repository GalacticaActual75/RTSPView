# Keeping installation information private

RTSPView issues and attachments are public. Use `node tests/ui-preview.cjs`
or `node tests/layout-designer-fixture.cjs` for synthetic UI examples. Crop
screenshots to the affected control before uploading. Check visible camera
pictures, names, locations, addresses, hostnames, file paths, schedules and
notifications as well as passwords. Do not attach configuration exports.

Keep real runtime settings, exports, certificates, logs and captures outside
the checkout, or in its ignored private directories. An ignored filename does
not protect an already tracked file, Git history, or a GitHub attachment.
Review the complete staged diff and any images before committing. Use reserved
documentation addresses and example.test/example domains for test fixtures.

Run these checks from the repository root:

```text
node tests/privacy.checks.cjs
node tools/check-privacy.cjs
node tools/check-privacy.cjs --history
gitleaks git . --log-opts="--all --full-history" --redact=100
```

The Privacy checks workflow runs on pushes and pull requests, fetching public
branches/tags and scanning reachable history with a checksum-pinned Gitleaks
release. The additional project check catches private network/path literals and
private runtime filenames without printing matching values. Use the existing
`tools/audit-repository.cjs` for a broader local inventory and contextual review.
These checks do not read image pixels, GitHub issues/attachments, release
installers, or encrypted/custom payloads. Passing CI is not a complete audit.

Choose an intentional public author name and a verified GitHub noreply email
for each development clone. Check both author and committer before publishing,
and check tagger identity when making annotated tags. Git configuration changes
affect future work only; `.mailmap` does not erase historical identity records.

If private information is uploaded, remove it from the current text and inspect
the edit history. Removing an embed or closing an issue does not necessarily
remove the original attachment. Check the original URL without authentication
and contact GitHub Support if it still serves sensitive content. Keep any
support request and original URLs private; do not open another public issue
containing the information you are trying to remove.

For a confirmed credential exposure, revoke or rotate the credential first.
History rewrites and hosted-content removal require separate verification;
they cannot recall copies already downloaded by others.
