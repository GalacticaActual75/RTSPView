param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSha256
)

$ErrorActionPreference = 'Stop'
$logDirectory = Join-Path $env:LOCALAPPDATA 'SpotMonitor\logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory 'update.log'

try {
    Start-Transcript -Path $logPath -Append | Out-Null
    $resolvedInstaller = (Resolve-Path -LiteralPath $InstallerPath).Path
    $actualHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash
    if ($actualHash -ne $ExpectedSha256) { throw 'The staged installer checksum is invalid.' }

    Start-Sleep -Seconds 3
    Stop-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue
    Get-Process -Name 'SpotMonitor.Controller','SpotMonitor.Viewer' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS'
    $process = Start-Process -FilePath $resolvedInstaller -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "The installer exited with code $($process.ExitCode)." }

    Start-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $resolvedInstaller -Force -ErrorAction SilentlyContinue
}
catch {
    $_ | Out-String | Add-Content -LiteralPath $logPath
    Start-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction SilentlyContinue
}
finally {
    Stop-Transcript -ErrorAction SilentlyContinue
}
