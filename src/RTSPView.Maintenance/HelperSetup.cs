using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using RTSPView.Core;

namespace RTSPView.Maintenance;

public static class HelperSetup
{
    public static readonly string[] TrustedOwners = ["S-1-5-18", "S-1-5-32-544", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"];
    public static void ValidateProtectedPath(string path)
    {
        var root = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)).TrimEnd('\\');
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Install RTSPView under Program Files before enabling maintenance.");
        for (var current = full; current.Length >= root.Length; current = Path.GetDirectoryName(current)!)
        {
            FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Maintenance paths must not use junctions or symbolic links.");
            var acl = item is DirectoryInfo directory ? (FileSystemSecurity)directory.GetAccessControl() : ((FileInfo)item).GetAccessControl();
            if (!TrustedOwners.Contains(acl.GetOwner(typeof(SecurityIdentifier))!.Value)) throw new InvalidOperationException("Maintenance files must be owned by administrators or SYSTEM.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                const FileSystemRights write = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes |
                    FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
                if ((rule.FileSystemRights & write) != 0 && !TrustedOwners.Contains(rule.IdentityReference.Value))
                    throw new InvalidOperationException("Maintenance files must not be writable by ordinary users.");
            }
            if (current.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    public static async Task InstallAsync(string allowedSid)
    {
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException("Administrator approval is required.");
        var sid = new SecurityIdentifier(allowedSid);
        if (!sid.IsAccountSid()) throw new ArgumentException("A Windows user SID is required.");
        ValidateProtectedPath(Environment.ProcessPath!);
        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories)) ValidateProtectedPath(file);
        using (var existing = new System.ServiceProcess.ServiceController(MaintenanceProtocol.ServiceName))
        {
            try
            {
                if (existing.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                { existing.Stop(); existing.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
            }
            catch (InvalidOperationException error) when (error.InnerException is System.ComponentModel.Win32Exception { NativeErrorCode: 1060 }) { }
        }
        using var key = Registry.LocalMachine.CreateSubKey(MaintenanceProtocol.RegistryPath);
        key.SetValue("AllowedSid", sid.Value);
        var exe = Environment.ProcessPath!;
        // Configuration contains no user-supplied executable path or command text.
        await Sc("create", MaintenanceProtocol.ServiceName, "binPath=", "\"" + exe + "\"", "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "RTSPView Maintenance");
        await Sc("config", MaintenanceProtocol.ServiceName, "binPath=", "\"" + exe + "\"", "start=", "auto");
        await Sc("description", MaintenanceProtocol.ServiceName, "Installs the verified PawnIO dependency and reads CPU/GPU sensors for RTSPView.");
        await Sc("start", MaintenanceProtocol.ServiceName);
    }
    private static async Task Sc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        if (process.ExitCode is not (0 or 1073 or 1056)) throw new InvalidOperationException("Windows could not configure the maintenance service: " + process.ExitCode);
    }
}
