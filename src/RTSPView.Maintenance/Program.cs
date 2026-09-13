using System.ServiceProcess;
using RTSPView.Maintenance;

if (args.Length == 2 && args[0] == "--install")
{
    try { await HelperSetup.InstallAsync(args[1]); return 0; }
    catch { return 1; }
}
if (args.Length != 0) return 2;
ServiceBase.Run(new MaintenanceService());
return 0;
