# This worker is copied with its job into an administrator-protected staging folder.
# It survives stopping/replacing RTSPViewMaintenance. It accepts no caller arguments.
$ErrorActionPreference = 'Stop'
$progressPath = Join-Path $PSScriptRoot 'progress.json'
$taskWasEnabled = $false
$wallStopped = $false
$job = $null
function Report([string]$State, [string]$Message) {
    try {
    $temporary = $progressPath + '.tmp'
    @{ state=$State; message=$Message; windowSession='service'; updatedAt=[DateTimeOffset]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $progressPath -Force
    } catch { Write-Warning 'Update progress could not be written.' }
}
function Start-UserWall {
    $runner = Join-Path $job.InstallRoot 'Run-Appliance.ps1'
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    [RTSPViewUserLaunch]::Launch([uint32]$job.SessionId, [string]$job.AllowedSid, $powershell,
        ('"' + $powershell + '" -NoProfile -WindowStyle Hidden -File "' + $runner + '"'), [string]$job.InstallRoot)
}
try {
    if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18') { throw 'Service updates must run through RTSPViewMaintenance.' }
    $job = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'job.json') | ConvertFrom-Json
    if ($job.Version -notmatch '^\d+\.\d+\.\d+(-beta\.\d+)?$' -or $job.Sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid verified update job.' }
    $root = [IO.Path]::GetFullPath([string]$job.InstallRoot).TrimEnd('\')
    if (!$root.StartsWith([Environment]::GetFolderPath('ProgramFiles').TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'The install location must be under Program Files.' }
    $installer = Join-Path $PSScriptRoot 'installer.exe'
    if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $job.Sha256) { throw 'The verified installer checksum changed.' }
    # Capture/validate the interactive account before stopping anything; never launch the wall as SYSTEM.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class RTSPViewUserLaunch {
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct STARTUPINFO {
  public int cb; public string reserved; public string desktop; public string title;
  public int x,y,xSize,ySize,xChars,yChars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error;
 }
 [StructLayout(LayoutKind.Sequential)] struct PROCESSINFO { public IntPtr process,thread; public int processId,threadId; }
 [DllImport("wtsapi32.dll",SetLastError=true)] static extern bool WTSQueryUserToken(uint session,out IntPtr token);
 [DllImport("userenv.dll",SetLastError=true)] static extern bool CreateEnvironmentBlock(out IntPtr environment,IntPtr token,bool inherit);
 [DllImport("userenv.dll")] static extern bool DestroyEnvironmentBlock(IntPtr environment);
 [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcessAsUser(IntPtr token,string app,System.Text.StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref STARTUPINFO startup,out PROCESSINFO process);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 static IntPtr GetToken(uint session,string sid) {
  IntPtr token; if(!WTSQueryUserToken(session,out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
  using(var identity=new WindowsIdentity(token)) { if(identity.User.Value!=sid) { CloseHandle(token); throw new UnauthorizedAccessException("The signed-in account changed."); } }
  return token;
 }
 public static void Validate(uint session,string sid) { CloseHandle(GetToken(session,sid)); }
 public static void Launch(uint session,string sid,string app,string command,string directory) {
  IntPtr token=GetToken(session,sid),environment=IntPtr.Zero;
  try {
   if(!CreateEnvironmentBlock(out environment,token,false)) throw new Win32Exception(Marshal.GetLastWin32Error());
   var startup=new STARTUPINFO { cb=Marshal.SizeOf(typeof(STARTUPINFO)), desktop="winsta0\\default", flags=1, show=0 };
   PROCESSINFO process;
   if(!CreateProcessAsUser(token,app,new System.Text.StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,0x08000400,environment,directory,ref startup,out process)) throw new Win32Exception(Marshal.GetLastWin32Error());
   CloseHandle(process.thread); CloseHandle(process.process);
  } finally { if(environment!=IntPtr.Zero) DestroyEnvironmentBlock(environment); CloseHandle(token); }
 }
}
'@
    [RTSPViewUserLaunch]::Validate([uint32]$job.SessionId, [string]$job.AllowedSid)
    # Leave time for the Controller to receive the accepted response and show progress.
    Start-Sleep -Seconds 3
    $task = Get-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -TaskPath '\' -ErrorAction SilentlyContinue
    if ($task -and $task.Settings.Enabled) {
        $taskWasEnabled = $true
        Disable-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -TaskPath '\' | Out-Null
    }
    Report 'working' 'Installing RTSPView. The live wall will restart when installation finishes.'
    # Stop only the helper here; the installer closes the Controller and Viewer.
    $service = Get-Service RTSPViewMaintenance
    $service.Stop(); $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    $wallStopped = $true
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /SERVICEUPDATE=1 /DIR="' + $root + '"'
    $process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru
    while (!$process.WaitForExit(1000)) { Report 'working' 'Installing RTSPView. Please keep this host on.' }
    if ($process.ExitCode -ne 0) { throw "The RTSPView installer exited with code $($process.ExitCode)." }
    $controller = Join-Path $root 'Controller\SpotMonitor.Controller.exe'
    if ((Get-Item -LiteralPath $controller).VersionInfo.ProductVersion.Split('+')[0] -ne $job.Version) { throw 'Installed version does not match the confirmed release.' }
    Start-Service RTSPViewMaintenance
    Start-UserWall
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        $viewerReady = Get-Process -Name 'SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $job.SessionId -and $_.Path -eq (Join-Path $root 'Viewer\SpotMonitor.Viewer.exe') }
        $controllerReady = Get-Process -Name 'SpotMonitor.Controller' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $job.SessionId -and $_.Path -eq $controller }
    } until (($viewerReady -and $controllerReady) -or [DateTimeOffset]::UtcNow -ge $deadline)
    if (!$viewerReady -or !$controllerReady) { throw 'Update installed, but the viewer and Controller did not both return. Launch RTSPView on the host.' }
    Report 'complete' "RTSPView $($job.Version) is installed and the viewer has restarted."
}
catch {
    Report 'failed' ('RTSPView update needs attention: ' + $_.Exception.Message)
    if ($wallStopped) {
        Start-Service RTSPViewMaintenance -ErrorAction SilentlyContinue
        try {
            $running = Get-Process -Name 'SpotMonitor.Controller' -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq $job.SessionId }
            if (!$running) { Start-UserWall }
        } catch { }
    }
}
finally {
    if ($taskWasEnabled) { Enable-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -TaskPath '\' -ErrorAction SilentlyContinue | Out-Null }
}
