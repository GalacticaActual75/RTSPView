# Production architecture and roadmap

## Process model

The final appliance uses two processes. `SpotMonitor.Controller` is an ASP.NET Core Windows service responsible for authenticated LAN administration, health data, configuration, logs, and viewer supervision. `SpotMonitor.Viewer` runs only in the signed-in interactive session and owns nine native LibVLC video surfaces. IPC uses a local named pipe restricted to the service identity and interactive user.

Running the renderer as a service is intentionally avoided: Windows services run in Session 0 and cannot reliably present an interactive full-screen wall. Task Scheduler (“at log on”, delayed, restart on failure) is the appropriate launch mechanism for the viewer; the service provides the stronger watchdog.

## Video lifecycle

Phase 2 gives every slot its own `MediaPlayer`, media object, status, cancellation scope, and bounded event queue. Phase 3 adds a monotonic last-frame/progress timestamp, startup timeout, stalled-frame detection, exponential backoff with jitter, and escalation from media replay to player recreation. Recovery never runs on the WPF UI thread. The shared LibVLC instance may be recreated only after systemic failure is established.

For nine feeds, camera substreams should be preferred where available. No stream is transcoded. TCP is the stable LAN default; UDP and Auto remain per-camera choices. D3D11VA is requested first because it is the modern Windows Direct3D 11 decode path. DXVA2 is a compatibility fallback, followed by software decoding. Actual support still depends on codec profile, level, bit depth, resolution, driver, and simultaneous decoder capacity, so field validation with the exact nine feeds is mandatory.

The hardware path is deliberately GPU-vendor neutral. LibVLCSharp's cross-platform hardware-decode switch delegates decoder selection to LibVLC and the installed Windows WDDM driver. The same x64 package therefore supports AMD Radeon and NVIDIA Quadro/GeForce adapters without a separate build. Runtime logs, not a configured vendor name, determine the decoder and adapter shown in diagnostics.

## Validated source profile

The deployment rebroadcast source was observed as H.264/AAC, 1280×720, approximately 456 Kb/s, 15 FPS, with a four-second keyframe interval. Audio decoding is disabled by default because the wall does not play audio. Nine such video feeds represent only about 4.1 Mb/s of compressed payload before protocol overhead. Phase 3 stall detection must allow for the four-second GOP; startup and frozen-video thresholds should not be shorter than two keyframe intervals unless frame-progress evidence is available.

## Delivery phases

1. Single stream, native rendering, hardware-decode request and diagnostic logs (complete).
2. Fixed 3×3 grid with independent player ownership (complete; validated with nine concurrent 720p feeds).
3. Frozen-stream watchdog and escalating recovery (complete; validated with eight healthy feeds and one deliberately unreachable feed).
4. Versioned configuration, validation, backup recovery, and import/export (complete; plain local URLs retained by explicit LAN-only deployment choice).
5. ASP.NET Core LAN admin and per-camera live apply (complete; separate authenticated controller process on TCP 5080).
6. Windows/stream telemetry with optional GPU providers.
7. Authenticated, confirmed remote controls and named-pipe viewer IPC.
8. Full-screen/multi-monitor appliance behavior, cursor hiding, confirmed five-click upper-right escape gesture, duplicate-instance protection, and Task Scheduler startup/recovery (complete; awaiting host validation).
9. Authentication, password management, CSRF defenses, audit/rotating logs, login rate limits, native black-background enforcement, simplified live overlays, and viewer health supervision (complete; awaiting host validation).
10. Signed self-contained release and WiX installer with opt-in firewall and startup tasks.

Each phase must preserve a runnable build and add failure-path tests before expanding the next subsystem.
