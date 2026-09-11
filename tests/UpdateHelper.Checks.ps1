# Exercise the deployed helper with fake process/task commands. Never install software.
$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('RTSPView-UpdateChecks-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null
$helper = Join-Path $scratch 'Apply-Update.ps1'
$source = Get-Content (Join-Path $PSScriptRoot '..\deployment\Apply-Update.ps1') -Raw
$source.Replace("Join-Path `$env:LOCALAPPDATA 'SpotMonitor\logs'", "Join-Path `$PSScriptRoot 'test-logs'") | Set-Content -LiteralPath $helper
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\deployment\Show-UpdateProgress.ps1') -Destination $scratch
function Start-Transcript { param($Path, [switch]$Append) }
function Stop-Transcript { param($ErrorAction) }
function Start-Sleep { param($Seconds) }
function Stop-ScheduledTask { param($TaskName, $ErrorAction) $global:RTSPViewTeststopped++ }
function Start-ScheduledTask { param($TaskName, $ErrorAction) if ($global:RTSPViewTestrestartFails -and $ErrorAction -eq 'Stop') { throw 'Simulated restart failure' } }
function Stop-Process { param([Parameter(ValueFromPipeline)]$InputObject, [switch]$Force) process {} }
function Get-Process { param($Name, $ErrorAction) if ($Name -eq 'SpotMonitor.Controller') { [pscustomobject]@{Path=$controllerPath} } elseif ($Name -eq 'SpotMonitor.Viewer') { [pscustomobject]@{Path=$viewerPath} } }
function Get-Item { param($LiteralPath) [pscustomobject]@{VersionInfo=[pscustomobject]@{ProductVersion=$global:RTSPViewTestproductVersion}} }
function Start-Process {
    param($FilePath,$ArgumentList,$WindowStyle,[switch]$PassThru)
    if ($FilePath -ne 'powershell.exe') { $global:RTSPViewTeststarted++ }
    $fake = [pscustomobject]@{Handle=1;ExitCode=$global:RTSPViewTestinstallerExit}
    $fake | Add-Member ScriptMethod WaitForExit { param($Milliseconds) return $true }
    return $fake
}
foreach ($case in @('success','bad-hash','installer-failure','version-mismatch','restart-failure')) {
    $global:RTSPViewTeststopped=0; $global:RTSPViewTeststarted=0; $global:RTSPViewTestinstallerExit=0; $global:RTSPViewTestrestartFails=$false; $global:RTSPViewTestproductVersion='1.0.29-beta.6+test'
    $installer=Join-Path $scratch ($case + '.exe'); 'Synthetic installer - never executed' | Set-Content -LiteralPath $installer
    $hash=(Get-FileHash -LiteralPath $installer).Hash
    switch ($case) {
        'bad-hash' {$hash='0'*64}
        'installer-failure' {$global:RTSPViewTestinstallerExit=5}
        'version-mismatch' {$global:RTSPViewTestproductVersion='1.0.29-beta.5'}
        'restart-failure' {$global:RTSPViewTestrestartFails=$true}
    }
    $status=Join-Path $scratch ($case + '.json')
    & $helper -InstallerPath $installer -ExpectedSha256 $hash -StatusPath $status -ExpectedVersion '1.0.29-beta.6'
    $result=Get-Content -LiteralPath $status -Raw | ConvertFrom-Json
    $expected=if($case -eq 'success'){'complete'}else{'failed'}
    if ($result.state -ne $expected) { throw "$case returned $($result.state), expected $expected" }
    if ($case -eq 'bad-hash' -and ($global:RTSPViewTeststopped -ne 0 -or $global:RTSPViewTeststarted -ne 0)) { throw 'Invalid installer caused a process/task action' }
    if ($case -eq 'success' -and $global:RTSPViewTeststarted -ne 1) { throw 'Expected exactly one installer launch' }
    Write-Output "PASS $case : $($result.message)"
}
Write-Output "Test evidence: $scratch"
