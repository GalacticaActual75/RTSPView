using System.Runtime.InteropServices;

namespace RTSPView.Viewer;

public static class ViewerTaskbarIdentity
{
    // Keep in sync with the launch shortcuts in installer/RTSPView.iss.
    public const string AppId = "RTSPView.Viewer";

    public static void Initialize() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
