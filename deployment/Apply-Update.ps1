param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256,
    [string]$StatusPath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:LOCALAPPDATA 'SpotMonitor\logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory 'update.log'
$wallStopped = $false
function Report-Update([string]$State, [string]$Message) {
    if (!$StatusPath) { return }
    try {
        $temporary = $StatusPath + '.tmp'
        @{ state = $State; message = $Message; windowSession = 'install'; updatedAt = [DateTimeOffset]::UtcNow.ToString('o'); logPath = $logPath } |
            ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
        Move-Item -LiteralPath $temporary -Destination $StatusPath -Force
    } catch { Write-Warning "Unable to publish update status: $_" }
}

try {
    Start-Transcript -Path $logPath -Append | Out-Null
    Report-Update 'working' 'Administrator approval received. Verifying the installer again...'
    # Launch from the elevated helper, which survives stopping the scheduled wall task.
    # The staging window closes when it sees the install session in the status file.
    if ($StatusPath) {
        $progressScript = Join-Path $PSScriptRoot 'Show-UpdateProgress.ps1'
        if (!(Test-Path -LiteralPath $progressScript)) { throw 'The update progress helper is missing.' }
        $uiArguments = @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', ('"' + $progressScript + '"'), '-StatusPath', ('"' + $StatusPath + '"'), '-WindowSession', 'install')
        $null = Start-Process -FilePath 'powershell.exe' -ArgumentList $uiArguments -WindowStyle Hidden -PassThru
    }
    $resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
    $actualHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedSha256) { throw 'The staged installer checksum is invalid.' }

    Report-Update 'working' 'Stopping the camera wall before installation...'
    Start-Sleep -Seconds 2
    Stop-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue
    $wallStopped = $true
    Get-Process -Name 'SpotMonitor.Controller','SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS'
    Report-Update 'working' 'Installing SpotMonitor. The camera wall will restart when installation finishes.'
    $process = Start-Process -FilePath $resolvedInstaller -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    $installStarted = [DateTimeOffset]::UtcNow
    while (!$process.WaitForExit(1000)) {
        $elapsed = [int]([DateTimeOffset]::UtcNow - $installStarted).TotalSeconds
        if ($elapsed % 10 -eq 0) { Report-Update 'working' "Installing SpotMonitor ($elapsed seconds elapsed). Please keep this host on." }
    }
    if ($process.ExitCode -ne 0) { throw "The installer exited with code $($process.ExitCode)." }

    $installRoot = Split-Path -Parent $PSScriptRoot
    $controllerPath = Join-Path $installRoot 'Controller\SpotMonitor.Controller.exe'
    $viewerPath = Join-Path $installRoot 'Viewer\SpotMonitor.Viewer.exe'
    if ($ExpectedVersion) {
        $installedVersion = (Get-Item -LiteralPath $controllerPath).VersionInfo.ProductVersion.Split('+')[0]
        if ($installedVersion -ne $ExpectedVersion) { throw "Expected $ExpectedVersion but found $installedVersion after installation." }
    }
    Report-Update 'working' 'Installation finished. Starting SpotMonitor and waiting for the viewer...'
    Start-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction Stop
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        $controllerRunning = Get-Process -Name 'SpotMonitor.Controller' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $controllerPath }
        $viewerRunning = Get-Process -Name 'SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $viewerPath }
    } until (($controllerRunning -and $viewerRunning) -or [DateTimeOffset]::UtcNow -ge $deadline)
    if (!$controllerRunning -or !$viewerRunning) { throw 'Installation finished, but the controller and viewer did not both start within 60 seconds. See the update log.' }
    Report-Update 'complete' "SpotMonitor $ExpectedVersion is installed and the viewer has started. Camera streams may still be reconnecting."
    Remove-Item -LiteralPath $resolvedInstaller -Force -ErrorAction SilentlyContinue
}
catch {
    $failure = $_.Exception.Message
    $_ | Out-String | Add-Content -LiteralPath $logPath
    if ($wallStopped) { Start-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue }
    Report-Update 'failed' "Update needs attention: $failure"
}
finally {
    Stop-Transcript -ErrorAction SilentlyContinue
}
