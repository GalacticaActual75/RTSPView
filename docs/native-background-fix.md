# Native tile background correction (1.0.36)

The previous periodic background guard set a black window-class brush. That did not control every repaint: LibVLCSharp 3.10.0 creates its WPF video host as a Win32 STATIC control, whose normal painting obtains a brush from its parent through WM_CTLCOLORSTATIC. A white parent brush therefore still produced a white tile background, even after the guard ran.

The tile now attaches a paint hook when the native host loads. It fills the host with black during WM_PAINT, WM_ERASEBKGND and WM_PRINTCLIENT. The existing WS_CLIPCHILDREN style excludes the decoder's child surface from ordinary painting. The hook follows replacement host objects and is detached on tile disposal. Stream decoding, opacity, composited overlays and video transforms are unchanged.

The Windows regression check uses the actual LibVLCSharp host with a parent explicitly returning white. It removes the new hook to reproduce the old white result, reinstates it and reads black pixels from native paint/erase output. It repeats after layout hide/show transitions. This checks a reproduced native painting failure; it does not establish that every possible GPU or remote-host display issue has the same cause.

Sources: [LibVLCSharp's native host implementation](https://github.com/videolan/libvlcsharp/blob/3.10.0/src/LibVLCSharp.WPF/VideoHwndHost.cs), [Microsoft's WM_CTLCOLORSTATIC documentation](https://learn.microsoft.com/en-us/windows/win32/controls/wm-ctlcolorstatic).
