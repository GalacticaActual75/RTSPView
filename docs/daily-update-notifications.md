# Daily update checks and wall notifications

The Controller checks the selected Stable/Beta release channel once every 24 hours, independently of the browser or viewer. The first check runs when the Controller starts with no current cache. `update-monitor.json` stores results and deadlines across restarts. Opening the admin page or polling its status reads the cache rather than GitHub. A change in installed version invalidates the old result.

System → Updates displays the last check, next check, and a Show update notifications on the stream wall toggle. Notifications default to enabled. Toggling them saves immediately, and the viewer picks up the change within five seconds. Checks continue when notifications are disabled. This host-specific preference and cache are separate from portable stream configuration backups.

The wall uses a small owned window above native video in the lower-right corner. Its smoked charcoal background, blue text and subtle border keep it readable without flashing, sounds or changing tile geometry. It is shown only for a newer version on the selected channel, not a requested downgrade. A final stable release counts as newer than a beta with the same base version.

Clicking the badge shows the target version, Install and Cancel, a wall-restart explanation and an export reminder. Installation uses a current-user-only named pipe to the Controller. The Controller rejects missing confirmation, stale versions and mismatched channels, then runs the existing release validation, checksum verification and Windows elevation workflow. No unauthenticated web installation route is added. Tests replace installer execution with a fake.

Check for updates remains available. Manual checks have a two-minute minimum interval per channel; channel changes do not install anything. Failed checks retry after 1, 2, 4, 8, 16 and then 24 hours. GitHub HTTP 403/429 responses impose a persisted cooldown, respecting Retry-After and rate-limit reset headers. Manual checks and installer metadata requests cannot bypass that cooldown. No GitHub token is distributed or required. The viewer never contacts GitHub.

New HTTP operations: POST `/api/update/check` and PUT `/api/update/notifications` with `{ "enabled": true }`. Both require the existing administrator session and CSRF protection. GET `/api/update` now reads cached status. The selected channel, manual installation endpoint and update safeguards are retained.

The windowed viewer's bottom bar includes Open web config, which opens `http://127.0.0.1:5080` through the Windows default browser. The bar wraps at smaller widths and remains hidden in full-screen mode. It does not enable LAN access or bypass administrator sign-in.

Opening Configure or the wall update prompt temporarily suppresses floating video/status overlays and their periodic window-raising behavior. They are restored when the dialog closes, including after cancellation; streams keep playing underneath.

Validation covers daily cadence, cache persistence, manual throttling, notification preferences, exponential backoff, persisted GitHub cooldowns, release ordering, stale-install rejection, authenticated/CSRF HTTP access, and the native badge's confirmation/cancel flow. Native QA artifacts are generated in the ignored `artifacts/ui-qa` directory; browser QA uses the synthetic local fixture.
