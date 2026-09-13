using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace RTSPView.Maintenance;

public static class MaintenanceClientIdentity
{
    public static void Verify(NamedPipeServerStream pipe, string allowedSid)
    {
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId)) throw Failure("identify the connected Controller process");
        using var process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid) throw Failure("query the connected Controller process");
        if (!OpenProcessToken(process, 0x0008, out var token)) throw Failure("read the Controller account"); // TOKEN_QUERY
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        {
            if (identity.User?.Value != allowedSid)
                throw new UnauthorizedAccessException("The Controller process account does not match the Windows account authorized during helper setup.");
        }
        // The pipe ACL also restricts connections to this account and denies network clients.
        // No client impersonation or client-supplied PID is used for privileged actions.
    }

    private static IOException Failure(string action) => new("Cannot " + action + " (Windows error " + Marshal.GetLastWin32Error() + ").");
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
}
