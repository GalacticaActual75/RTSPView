param([switch]$FunctionsOnly)
$ErrorActionPreference = 'Stop'

function Invoke-ControllerSupervisionStep {
    param([hashtable]$State, [datetime]$Now, [scriptblock]$IsRunning, [scriptblock]$Launch)
    if (& $IsRunning) {
        if (!$State.HealthySince) { $State.HealthySince = $Now }
        if (($Now - $State.HealthySince).TotalSeconds -ge 60) { $State.Failures = 0 }
        $State.MissingSince = $null
        return
    }
    $State.HealthySince = $null
    if (!$State.MissingSince) { $State.MissingSince = $Now; return }
    # Allow Restart Application and installers to complete their own process handoff.
    if (($Now - $State.MissingSince).TotalSeconds -lt 15 -or ($State.NextAttempt -and $Now -lt $State.NextAttempt)) { return }
    $State.Failures = [int]$State.Failures + 1
    $State.NextAttempt = $Now.AddSeconds([Math]::Min(60, 5 * [Math]::Pow(2, [Math]::Min(4, $State.Failures - 1))))
    try { & $Launch } catch { Write-Warning 'Controller could not be restarted; supervision will retry with backoff.' }
}
if ($FunctionsOnly) { return }

$controller = Join-Path $PSScriptRoot 'Controller\SpotMonitor.Controller.exe'
$viewer = Join-Path $PSScriptRoot 'Viewer\SpotMonitor.Viewer.exe'
$session = [Diagnostics.Process]::GetCurrentProcess().SessionId
$hash = [Security.Cryptography.SHA256]::Create()
$identity = [BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($PSScriptRoot.ToLowerInvariant()))).Replace('-', '')
$hash.Dispose()
$mutex = New-Object Threading.Mutex($false, ('Local\RTSPView.Supervisor.' + $identity))
$owned = $false
function Test-InstalledProcess([string]$Path) {
    foreach ($candidate in [Diagnostics.Process]::GetProcessesByName([IO.Path]::GetFileNameWithoutExtension($Path))) {
        try { if ($candidate.SessionId -eq $session -and $candidate.MainModule.FileName -eq $Path) { return $true } }
        catch [InvalidOperationException] { }
        catch [ComponentModel.Win32Exception] { }
        finally { $candidate.Dispose() }
    }
    return $false
}
try {
    try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
    if (!$owned) { exit 0 }
    if (!(Test-InstalledProcess $controller)) { Start-Process -FilePath $controller -WindowStyle Hidden }
    if (!(Test-InstalledProcess $viewer)) { Start-Process -FilePath $viewer -ArgumentList '--respect-viewer-pause' }
    $state = @{ Failures = 0 }
    while ($true) {
        Start-Sleep -Seconds 5
        Invoke-ControllerSupervisionStep $state ([DateTime]::UtcNow) { Test-InstalledProcess $controller } {
            if (!(Test-InstalledProcess $controller)) { Start-Process -FilePath $controller -WindowStyle Hidden }
        }
    }
}
catch { Write-Warning 'Appliance supervision stopped unexpectedly.'; exit 1 }
finally { if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
