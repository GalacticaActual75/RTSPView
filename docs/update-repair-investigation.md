# Update and playback investigation

## Installer and helper

The supplied installer log records a locked `Controller\System.Private.CoreLib.dll`, an Abort, and rollback. Exit code 5 must remain a failure. It is not evidence that the update completed, even if the Controller reports the requested version.

Service updates launch `Run-Appliance.ps1` directly in the interactive session. Stopping only the scheduled task does not stop that supervisor. It can restart the Controller after its grace period while the installer is replacing files.

The repair disables the startup task, stops the installed supervisor by its complete script path, waits for the installed Controller and Viewer to exit, and checks application executables and DLLs for locks before replacement. An unresolved lock aborts before replacement. Task state is preserved across retries and restored afterward. The preparation runs in both updater workers as well as the installer, including when the target is an older stable installer. The installer carries its own preparation script so an existing beta installation can receive the repair.

If preparation aborts after the installer stops a running maintenance service, the installer restores that service. Both update paths retain installer logs and verify Controller, Viewer, and Maintenance versions before reporting success. This is component-version verification, not a full installed-file integrity audit. Persisted failed service updates show their attempt timestamp. The Controller records why it falls back to Windows approval instead of silently choosing UAC.

The affected host's current helper/service status was not supplied. Its particular reason for falling back to UAC remains unconfirmed. The original shared update log predates the later reported rollback attempt.

## Application log

* `/api/telemetry` logged `InvalidOperationException`. Code inspection found shared mutable GPU counter lists being refreshed, enumerated, and disposed by concurrent requests without synchronization. Sampling and disposal now share a lock; concurrent sampling across rediscovery is tested. The original log lacks a stack trace, so attribution to this race is probable rather than proven.
* Video converter errors occur across cameras in hourly groups and shortly after startup. Those timings match scheduled and initial thumbnail captures. Native thumbnail capture asks LibVLC to convert frames; normal displayed-frame counters continue afterward. Investigate thumbnail conversion on the affected GPU before attributing these errors to failed camera playback. No speculative GPU/backend changes or error suppression were made.
* Direct3D texture errors and late frames require reproduction on the affected host. The log alone cannot establish GPU exhaustion, a driver bug, or insufficient CPU performance.
* Five watchdog relaunches are recorded. One follows an application shutdown; these are not five proven crashes. Intentional pause uses the viewer's explicit exit action. The fullscreen escape gesture only leaves fullscreen.

## YouTube

The supplied public live stream was retested with the packaged beta 4 helper, Streamlink, and yt-dlp's own HTTP client. Extraction succeeds, but requests for media segments receive HTTP 403. Both default and Android VR yt-dlp clients fail; the iOS client offers no usable format in this test. The bundled yt-dlp version matches the latest official stable release checked during investigation. No cookies or account credentials were used.

The helper now reads initial media bytes before announcing readiness, preserves those bytes for HLS playback, and returns a sanitized HTTP 401/403 error when an upstream media request is refused. This repairs misleading startup diagnostics; it does **not** claim to make the refused YouTube live stream playable.

Validation uses real synthetic RTSP/HLS/MP4 playback, including ONVIF profile-to-RTSP decoding and website relays. Public-provider compatibility is separate from these deterministic tests.

### Follow-up after beta 5 installation

The subsequent host log contains repeated explicit HTTP 403 refusals and scheduled retries from the new helper. This confirms that the new error-handling code is active; it does not independently verify every installed file. Historical audit entries also confirm that both the beta update and stable rollback attempts requested elevation rather than using the service path. A native converter error immediately followed by a successful snapshot refresh further supports treating converter messages individually rather than as proof that playback failed.

The official yt-dlp nightly `2026.09.16.232951` was tested separately with its published executable SHA-256 verified and a local test FFmpeg binary. Its native HLS download also receives HTTP 403 on the video segments. No dependency update is therefore claimed to repair the reported stream.

Follow-up changes distinguish website resolution from stale playback, prioritize actual source errors in stream status, and include semaphore queue time in the resolver's deadline. A native viewer fixture with deliberately refused HLS segments verifies that resolving telemetry has no stale warning, that HTTP 403 reaches telemetry, and that retries occur. These follow-up changes are not in the published beta 5 installer.
