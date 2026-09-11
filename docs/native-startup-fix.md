# Native playback startup correction (1.0.39)

After 1.0.38, a host still showed black main feeds with live snapshots and working composited overlays. Investigation reproduced a distinct startup failure: CameraTile started media while collapsed, and LibVLCSharp left MediaPlayer.Hwnd at zero even after the tile acquired a valid native window. The earlier background-only checks could not detect this.

Native startup now waits until the tile is visible, loaded and has a nonzero native handle. The playback operation explicitly assigns that handle before Play, including manual restarts and replacement players. Initially hidden tiles begin playback when revealed. Pending starts are canceled when the stream is stopped or disabled. Composited overlays retain their existing startup path, and the 1.0.38 black-background correction remains.

The Windows regression covers collapsed startup, reveal, manual restart and replacement-player attachment using an unavailable loopback source; it checks the real player and host handles without relying on external cameras. Existing background and layout tests remain in release CI. This regression proves the startup attachment failure is corrected, not that every remote GPU display issue has been reproduced.
