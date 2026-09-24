using Microsoft.AspNetCore.DataProtection;
using RTSPView.Infrastructure;
internal static class PasswordRecoveryChecks
{
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"RTSPView-key-reset-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var keys=new DirectoryInfo(Path.Combine(root,"data-protection"));
        var before=DataProtectionProvider.Create(keys,b=>b.SetApplicationName("SpotMonitor.Controller"));
        var purposes=new[]{"RTSPView.Mqtt.Password.v1","RTSPView.Tapo.Password.v1"};
        var encrypted=purposes.Select(p=>before.CreateProtector(p).Protect("synthetic-secret")).ToArray();
        var securityPath=Path.Combine(root,"web-security.json");
        var security=await WebSecurity.LoadOrCreateAsync(securityPath,Path.Combine(root,"initial.txt"));
        var session=security.SessionVersion;
        File.Move(securityPath,securityPath+".private-backup");
        var reset=await WebSecurity.LoadOrCreateAsync(securityPath,Path.Combine(root,"initial.txt"));
        if(!reset.Verify("admin")||!reset.PasswordChangeRequired||reset.SessionVersion==session)throw new Exception("Password reset failed to revoke old sessions");
        var after=DataProtectionProvider.Create(keys,b=>b.SetApplicationName("SpotMonitor.Controller"));
        for(var i=0;i<purposes.Length;i++)if(after.CreateProtector(purposes[i]).Unprotect(encrypted[i])!="synthetic-secret")throw new Exception("Password reset destroyed integration credentials");
        Console.WriteLine("PASS key-preserving administrator recovery and session revocation");
    }
}
