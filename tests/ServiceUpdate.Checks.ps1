# Run the shipped worker with all host actions replaced by fakes in an isolated directory.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Get-Content -Raw -LiteralPath (Join-Path $root 'deployment\Apply-ServiceUpdate.ps1')
$nativePattern = "(?s)Add-Type -TypeDefinition @'\r?\n(.*?)\r?\n'@"
$native = [regex]::Match($source, $nativePattern).Groups[1].Value
if (!$native) { throw 'Session launcher code missing.' }
# Compile the real native declarations, but never call them.
Add-Type -TypeDefinition ($native.Replace('RTSPViewUserLaunch','RTSPViewNativeCompileCheck'))
Add-Type -TypeDefinition @'
public static class RTSPViewUserLaunch {
 public static bool Deny; public static int Launches;
 public static void Validate(uint session,string sid) { if(Deny) throw new System.Exception("Fake account unavailable"); }
 public static void Launch(uint session,string sid,string app,string command,string directory) { Launches++; }
}
'@
$source = [regex]::Replace($source, $nativePattern, '# Native actions replaced by fake launcher for this check only.')
$guard = "if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18')"
if (!$source.Contains($guard)) { throw 'Service identity guard changed; review this test.' }
$source = $source.Replace($guard, 'if ($false)')
function Start-Sleep { param($Seconds) }
function Get-ScheduledTask { [pscustomobject]@{ Settings=[pscustomobject]@{ Enabled=$true } } }
function Disable-ScheduledTask { $global:rtspTestdisabled++ }
function Enable-ScheduledTask { $global:rtspTestenabled++ }
function Get-Service {
    $s = New-Object PSObject
    $s | Add-Member ScriptMethod Stop { $global:rtspTeststopped++ }
    $s | Add-Member ScriptMethod WaitForStatus { }
    return $s
}
function Start-Service { $global:rtspTeststarted++ }
function Start-Process {
    param($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru)
    if ($FilePath -notlike '*\installer.exe' -or $ArgumentList -notlike '*/SERVICEUPDATE=1*') { throw 'Unexpected process request' }
    $global:rtspTestinstalls++
    $p = [pscustomobject]@{ ExitCode=$global:rtspTestinstallerExit }
    $p | Add-Member ScriptMethod WaitForExit { return $true }
    return $p
}
function Get-Item { [pscustomobject]@{ VersionInfo=[pscustomobject]@{ ProductVersion='1.0.40-beta.12' } } }
function Get-Process {
    param($Name)
    [pscustomobject]@{ SessionId=42; Path=(Join-Path $global:rtspTestinstallRoot $(if($Name -like '*.Viewer'){'Viewer\SpotMonitor.Viewer.exe'}else{'Controller\SpotMonitor.Controller.exe'})) }
}
foreach ($scenario in @('success','hash','installer','session')) {
    $directory = Join-Path $root ('artifacts\service-update-checks\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $global:rtspTestinstallRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'RTSPView'
    [IO.File]::WriteAllText((Join-Path $directory 'installer.exe'), 'Fake installer bytes; never executed')
    $hash = (Get-FileHash -LiteralPath (Join-Path $directory 'installer.exe') -Algorithm SHA256).Hash
    if ($scenario -eq 'hash') { $hash = '0' * 64 }
    @{ InstallRoot=$global:rtspTestinstallRoot; Version='1.0.40-beta.12'; Sha256=$hash; AllowedSid='S-1-5-21-1-2-3-1001'; SessionId=42 } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'job.json')
    $worker = Join-Path $directory 'worker.ps1'; Set-Content -LiteralPath $worker -Value $source
    $global:rtspTestdisabled=0; $global:rtspTestenabled=0; $global:rtspTeststopped=0; $global:rtspTeststarted=0; $global:rtspTestinstalls=0
    $global:rtspTestinstallerExit = if($scenario -eq 'installer'){1}else{0}
    [RTSPViewUserLaunch]::Deny = $scenario -eq 'session'; [RTSPViewUserLaunch]::Launches=0
    & $worker
    $result = Get-Content -Raw -LiteralPath (Join-Path $directory 'progress.json') | ConvertFrom-Json
    if ($scenario -eq 'success') {
        if ($result.state -ne 'complete' -or $global:rtspTestinstalls -ne 1 -or [RTSPViewUserLaunch]::Launches -ne 1 -or $global:rtspTestenabled -ne 1) { throw "Successful update failed: $($result.message)" }
    } else {
        if ($result.state -ne 'failed') { throw 'Failure was not reported.' }
        if ($scenario -in @('hash','session') -and ($global:rtspTeststopped -ne 0 -or $global:rtspTestinstalls -ne 0)) { throw 'Unverified update stopped the wall.' }
        if ($scenario -eq 'installer' -and ($global:rtspTestenabled -ne 1 -or $global:rtspTeststarted -ne 1)) { throw 'Failed installer did not restore service/task state.' }
    }
    Write-Host "PASS service update $scenario (fake host actions only)"
}
