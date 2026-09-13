# Scheduled restarts (beta)

System → Maintenance → Scheduled restarts has independent Viewer application and Windows host schedules, both disabled by default. Enable either one or both. Each has its own interval of 1–8760 hours or selected weekdays and a time in the host's time zone, plus its own save and skip controls. Saving re-arms only that action's schedule; changing a dropdown alone does nothing. Saving, disabling or skipping one action leaves the other action's settings and next occurrence intact.

Existing single schedules are carried into their original action slot, including their next occurrence and history; the other action remains disabled. If a viewer restart becomes due during a host countdown or on the same scheduler tick as a host restart request, that viewer occurrence is skipped. Cancelling the host countdown does not replay the skipped viewer occurrence. Its next scheduled occurrence remains active.

The Controller owns scheduling and must remain running; no browser or Home Assistant connection is needed. Windows must still be configured to launch RTSPView after sign-in for unattended recovery following a host reboot. This feature does not change sign-in or startup configuration.

Host schedules require an acknowledgement when enabled or saved. Each host occurrence first starts a 60-second countdown shown across the admin pages. Cancel this restart skips that occurrence. Skip next restart also works before the countdown. Disabling or saving the host schedule cancels any pending countdown. There is no operating-system shutdown timer to accidentally cancel another application's reboot. After the countdown expires, Windows is asked to restart without forcing applications closed; Windows can reject or block the request. Status reports acceptance, not proof that the reboot completed.

The atomic `restart-schedule.json` file in the application's data directory contains the settings, next occurrence, pending countdown, last attempt and last result. Occurrences are consumed before issuing a restart command to prevent replay after a crash. Failed requests are recorded and are not retried until the next occurrence. Logs preserve schedule actions. This host-local file is deliberately excluded from portable stream configuration exports/imports.

Controller startup skips overdue occurrences and interrupted countdowns. During operation, occurrences overdue by more than two minutes are skipped (for example, after sleep). Interval schedules restart their interval from saving or executing; skipping advances one interval. Weekly schedules use host local time, skip nonexistent spring-forward times, and execute only once for repeated fall-back times.

Scheduled commands share the update staging gate. An active update defers a due restart by one minute; a deferred host restart gets a fresh countdown. Recent unfinished update progress files also protect a newly started Controller for two hours. The current Controller's own active progress file remains protected until it reports completion or failure.

Validation includes isolated HTTP authentication/CSRF tests and deterministic scheduler tests for due times, skip/cancel, persistence, startup/resume, DST, update exclusion and failures. Production restart commands are replaced by fakes in scheduler tests. Browser QA uses `tests/ui-preview.cjs`, never the installed host.

This beta also fixes stale stream errors: new decoded-frame progress in a Live stream with no pending reconnect clears its active error. A connection event alone cannot clear it. Reconnect counts and log history are retained; ongoing stalls and connection failures remain visible.
