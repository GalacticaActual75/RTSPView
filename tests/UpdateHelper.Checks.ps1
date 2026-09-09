# Exercise the deployed helper with fake process/task commands. Never install software.
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('SpotMonitor-UpdateChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$helper = Join-Path $scratch 'Apply-Update.ps1'
$source = Get-Content (Join-Path $PSScriptRoot '..\deployment\Apply-Update.ps1') -Raw
$source.Replace("Join-Path `$env:LOCALAPPDATA 'SpotMonitor\logs'", "Join-Path `$PSScriptRoot 'test-logs'") | Set-Content -LiteralPath $helper
function Start-Transcript { param($Path, [switch]$Append) }
function Stop-Transcript { param($ErrorAction) }
function Start-Sleep { param($Seconds) }
function Stop-ScheduledTask { param($TaskName, $ErrorAction) $global:SpotMonitorTeststopped++ }
function Start-ScheduledTask { param($TaskName, $ErrorAction) if ($global:SpotMonitorTestrestartFails -and $ErrorAction -eq 'Stop') { throw 'Simulated restart failure' } }
function Stop-Process { param([Parameter(ValueFromPipeline)]$InputObject, [switch]$Force) process {} }
function Get-Process { param($Name, $ErrorAction) if ($Name -eq 'SpotMonitor.Controller') { [pscustomobject]@{Path=$controllerPath} } elseif ($Name -eq 'SpotMonitor.Viewer') { [pscustomobject]@{Path=$viewerPath} } }
function Get-Item { param($LiteralPath) [pscustomobject]@{VersionInfo=[pscustomobject]@{ProductVersion=$global:SpotMonitorTestproductVersion}} }
function Start-Process {
    param($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru)
    $global:SpotMonitorTeststarted++
    $fake = [pscustomobject]@{Handle=1;ExitCode=$global:SpotMonitorTestinstallerExit}
    $fake | Add-Member ScriptMethod WaitForExit { param($Milliseconds) return $true }
    return $fake
}
foreach ($case in @('success','bad-hash','installer-failure','version-mismatch','restart-failure')) {
    $global:SpotMonitorTeststopped=0; $global:SpotMonitorTeststarted=0; $global:SpotMonitorTestinstallerExit=0; $global:SpotMonitorTestrestartFails=$false; $global:SpotMonitorTestproductVersion='1.0.29-beta.6+test'
    $installer=Join-Path $scratch ($case + '.exe'); 'Synthetic installer - never executed' | Set-Content -LiteralPath $installer
    $hash=(Get-FileHash -LiteralPath $installer).Hash
    switch ($case) {
        'bad-hash' {$hash='0'*64}
        'installer-failure' {$global:SpotMonitorTestinstallerExit=5}
        'version-mismatch' {$global:SpotMonitorTestproductVersion='1.0.29-beta.5'}
        'restart-failure' {$global:SpotMonitorTestrestartFails=$true}
    }
    $status=Join-Path $scratch ($case + '.json')
    & $helper -InstallerPath $installer -ExpectedSha256 $hash -StatusPath $status -ExpectedVersion '1.0.29-beta.6'
    $result=Get-Content -LiteralPath $status -Raw | ConvertFrom-Json
    $expected=if($case -eq 'success'){'complete'}else{'failed'}
    if ($result.state -ne $expected) { throw "$case returned $($result.state), expected $expected" }
    if ($case -eq 'bad-hash' -and ($global:SpotMonitorTeststopped -ne 0 -or $global:SpotMonitorTeststarted -ne 0)) { throw 'Invalid installer caused a process/task action' }
    if ($case -eq 'success' -and $global:SpotMonitorTeststarted -ne 1) { throw 'Expected exactly one installer launch' }
    Write-Output "PASS $case : $($result.message)"
}
Write-Output "Test evidence: $scratch"
