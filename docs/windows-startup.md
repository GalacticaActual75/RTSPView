# Live View at Windows sign-in

Open **Settings → Display → Windows startup** on the host's web administration page. The checkbox reads the installed Windows scheduled task, rather than a separate application preference. Select **Open Live View when I sign in to Windows**, then **Apply startup setting**. If browsing from another device, approve the Windows administrator prompt on the host. Use **Refresh status** to verify the result.

Applying the enabled setting also repairs the existing task: it targets the Windows account running the Controller, uses an interactive session, waits 20 seconds after sign-in, and permits operation on battery power. Disabling it prevents future automatic launches; it does not stop the current Live View.

Startup occurs after Windows sign-in, not at the sign-in screen or when unlocking. It does not configure Windows automatic sign-in. A source/development build reports the option unavailable unless the installed startup helper is present.

Previously, the sign-in launcher used the same pause check as watchdog recovery. Closing Live View intentionally left a durable pause marker, so a subsequent sign-in could start the Controller while leaving Live View closed. The repaired task marks a sign-in launch explicitly; that launch resumes Live View and clears the old pause. Watchdog recovery within a running session still respects an intentional stop.

The installer checkbox registers the task through the same helper. If setup was launched as a different administrator account, use the web setting while RTSPView is running under the intended viewing account to repair the task. A task that cannot be read is reported as unavailable rather than disabled. Cancelled approval and unverified changes are not reported as saved.

The task retains the historical name `SpotMonitor Camera Wall` for upgrade compatibility. Tests use mocked task operations; validation on an affected remote host requires installing the beta, applying the startup setting, and signing out and back in.
