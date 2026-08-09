$ErrorActionPreference = 'Stop'
$controller = Join-Path $PSScriptRoot 'Controller\SpotMonitor.Controller.exe'
$viewer = Join-Path $PSScriptRoot 'Viewer\SpotMonitor.Viewer.exe'

try {
    Start-Process -FilePath $controller -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $viewerProcess = Start-Process -FilePath $viewer -PassThru -Wait
    exit $viewerProcess.ExitCode
}
catch {
    exit 1
}
