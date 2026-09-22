param([string]$Dotnet = 'dotnet', [string]$PackageSource = '')
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
function Run-Native([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit $LASTEXITCODE" }
}
$env:DOTNET_HOST_PATH = (Get-Command $Dotnet).Source
$env:DOTNET_ROOT = Split-Path $env:DOTNET_HOST_PATH
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$restore = @('restore','RTSPView.sln','-r','win-x64')
if($PackageSource){$restore += @('--source',$PackageSource)}
Run-Native $Dotnet $restore
Run-Native $Dotnet @('build','RTSPView.sln','-c','Release','--no-restore')
foreach($project in @('Configuration','Reliability','Connector','ViewerLifecycle','Automation','AutomationHealth','Tapo','LanAccess','RestartSchedule','Maintenance','Weather','LayoutVisibility','Ui','Onvif')) {
    $restore = @('restore',"tests/RTSPView.${project}Checks")
    if($PackageSource){$restore += @('--source',$PackageSource)}
    Run-Native $Dotnet $restore
    Run-Native $Dotnet @('run','--project',"tests/RTSPView.${project}Checks",'-c','Release','--no-restore')
}
foreach($check in @('admin-security','connector-http','tapo-http','onvif-http','layout-lan-http','wall-layout-presets','wall-proportions','shape-editor','dashboard-ux','viewer-controls','application-restart','automation-health','related-choices','preview-refresh','lan-firewall','branding','privacy')) {
    Run-Native 'node' @("tests/$check.checks.cjs",$env:DOTNET_HOST_PATH)
}
foreach($check in @('UpdateHelper','ServiceUpdate','UpdateProgress','ControllerSupervision','InstallationShutdown')) {
    Run-Native 'powershell.exe' @('-NoProfile','-ExecutionPolicy','Bypass','-File',"tests/$check.Checks.ps1")
}
