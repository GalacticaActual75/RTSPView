# Real child processes and file locks; scheduled-task operations are isolated fakes.
$ErrorActionPreference = 'Stop'
$scratch = Join-Path $PSScriptRoot ('..\artifacts\installation-shutdown-' + [Guid]::NewGuid().ToString('N'))
$root = Join-Path $scratch 'App with spaces'
New-Item -ItemType Directory -Path (Join-Path $root 'Controller') -Force | Out-Null
$helper = (Resolve-Path "$PSScriptRoot/../deployment/Prepare-Installation.ps1").Path
$runner = Join-Path $root 'Run-Appliance.ps1'
$dll = Join-Path $root 'Controller\locked.dll'
[IO.File]::WriteAllText($dll, 'Synthetic application file')
@'
$stream = [IO.File]::Open((Join-Path $PSScriptRoot 'Controller\locked.dll'), 'Open', 'Read', 'Read')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'ready'), 'ready')
while ($true) { Start-Sleep -Seconds 1 }
'@ | Set-Content -LiteralPath $runner
$wrapper = Join-Path $scratch 'invoke.ps1'
@'
param($Helper, $InstallRoot, $StatePath, [switch]$Restore)
function Get-Service { $null }
function Start-Service { throw 'Must not touch real services' }
function Get-ScheduledTask { [pscustomobject]@{Settings=[pscustomobject]@{Enabled=!(Test-Path -LiteralPath ($StatePath + '.disabled'))}} }
function Disable-ScheduledTask { Set-Content -LiteralPath ($StatePath + '.disabled') -Value 'disabled' }
function Stop-ScheduledTask { if (!(Test-Path -LiteralPath ($StatePath + '.disabled'))) { throw 'Task must be disabled before stopping' } }
function Enable-ScheduledTask { Set-Content -LiteralPath ($StatePath + '.restored') -Value 'restored' }
& $Helper -InstallRoot $InstallRoot -StatePath $StatePath -Restore:$Restore
exit $LASTEXITCODE
'@ | Set-Content -LiteralPath $wrapper
$powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$children = @()
function Invoke-Preflight([switch]$Restore) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $wrapper + '" -Helper "' + $helper + '" -InstallRoot "' + $root + '" -StatePath "' + (Join-Path $scratch 'state.json') + '"'
    if ($Restore) { $arguments += ' -Restore' }
    $process = Start-Process $powershell -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { $process.Kill(); throw 'Preflight timed out' }
    return $process.ExitCode
}
try {
    $child = Start-Process $powershell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $runner + '"') -WindowStyle Hidden -PassThru
    $children += $child
    $limit = [DateTime]::UtcNow.AddSeconds(10)
    while (!(Test-Path -LiteralPath (Join-Path $root 'ready'))) {
        if ([DateTime]::UtcNow -gt $limit) { throw 'Fixture did not start' }
        Start-Sleep -Milliseconds 100
    }
    # A different script in the same installation must not be killed.
    $otherScript = Join-Path $root 'Unrelated.ps1'
    'Start-Sleep -Seconds 120' | Set-Content -LiteralPath $otherScript
    $other = Start-Process $powershell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $otherScript + '"') -WindowStyle Hidden -PassThru
    $children += $other
    if ((Invoke-Preflight) -ne 0) { throw 'Directly launched supervisor was not stopped' }
    if (!$child.HasExited -or $other.HasExited) { throw 'Incorrect process selection' }
    $stream = [IO.File]::Open($dll, 'Open', 'ReadWrite', 'None'); $stream.Dispose()
    'PASS direct supervisor terminated, DLL released, unrelated PowerShell preserved.'
    $lock = [IO.File]::Open($dll, 'Open', 'Read', 'Read')
    try { if ((Invoke-Preflight) -eq 0) { throw 'Locked application file accepted' } }
    finally { $lock.Dispose() }
    if ([IO.File]::ReadAllText($dll) -ne 'Synthetic application file') { throw 'Preflight changed application bytes' }
    'PASS unrelated file lock rejects installation before replacement.'
    # The running installer is staged here; it must never block its own preflight.
    $stage = Join-Path $root 'Maintenance\State\Updates\fixture'
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $stagedFile = Join-Path $stage 'installer.exe'; [IO.File]::WriteAllText($stagedFile, 'fixture')
    $lock = [IO.File]::Open($stagedFile, 'Open', 'Read', 'Read')
    try { if ((Invoke-Preflight) -ne 0) { throw 'Staged installer blocks itself' } }
    finally { $lock.Dispose() }
    'PASS active staged installer excluded from application lock check.'
    if ((Invoke-Preflight -Restore) -ne 0 -or !(Test-Path -LiteralPath (Join-Path $scratch 'state.json.restored'))) { throw 'Original task state lost after preflight retries' }
    'PASS original enabled task restored after repeated preflight, including failure.'
} finally { foreach ($child in $children) { if (!$child.HasExited) { $child.Kill(); $child.WaitForExit() }; $child.Dispose() } }
