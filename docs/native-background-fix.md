# Native tile background correction (1.0.38)

Versions 1.0.36 and 1.0.37 intercepted WM_PAINT, WM_ERASEBKGND and WM_PRINTCLIENT on the native video host to prevent a white background. After those releases, a host reported black main video while snapshots remained live and composited overlays still displayed. The empty-host regression used for the earlier fix did not validate live native rendering.

Version 1.0.38 removes that paint interception. Instead, the owning WPF window answers WM_CTLCOLORSTATIC with a black brush only for the matching video host. The native STATIC control and video renderer keep their normal paint behavior. This retains black empty-host painting without drawing over the decoder target. Stream decoding, opacity and composited overlays are unchanged.

The Windows regression reproduces white with a white parent brush, verifies black with the scoped color response, checks that paint/erase messages are not consumed and that unrelated controls are unaffected, and repeats after layout hide/show transitions. Remote GPU playback still requires host confirmation; the tests do not establish that every possible display failure is reproduced.

Sources: [LibVLCSharp's native host implementation](https://github.com/videolan/libvlcsharp/blob/3.10.0/src/LibVLCSharp.WPF/VideoHwndHost.cs), [Microsoft's WM_CTLCOLORSTATIC documentation](https://learn.microsoft.com/en-us/windows/win32/controls/wm-ctlcolorstatic).
