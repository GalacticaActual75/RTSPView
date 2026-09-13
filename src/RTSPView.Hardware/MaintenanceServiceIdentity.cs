using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RTSPView.Core;

namespace RTSPView.Hardware;

public static class MaintenanceServiceIdentity
{
    public static void Verify(uint pipeProcessId)
    {
        // Query-only SCM access works without opening the SYSTEM-owned process.
        // Service creation/configuration remains protected by Windows administrator ACLs.
        using var manager = OpenSCManager(null, null, 0x0001); // SC_MANAGER_CONNECT
        if (manager.IsInvalid) throw Failure("open the Windows service manager");
        using var service = OpenService(manager, MaintenanceProtocol.ServiceName, 0x0004); // SERVICE_QUERY_STATUS
        if (service.IsInvalid) throw Failure("find the RTSPView maintenance service");
        if (!QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<ServiceStatus>(), out _))
            throw Failure("query the RTSPView maintenance service");
        if (status.CurrentState != 4 || status.ProcessId == 0 || status.ProcessId != pipeProcessId || status.ServiceType != 0x10)
            throw new UnauthorizedAccessException("The pipe does not belong to the running RTSPView maintenance service.");
    }

    private static IOException Failure(string action) => new("Cannot " + action + " (Windows error " + Marshal.GetLastWin32Error() + ").");
    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode,
            CheckPoint, WaitHint, ProcessId, ServiceFlags;
    }
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ServiceHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseServiceHandle(handle);
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out ServiceStatus status, int size, out int needed);
    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
