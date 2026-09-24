param(
    [Parameter(Mandatory=$true)][string]$InstallRoot,
    [Parameter(Mandatory=$true)][string]$StatePath,
    [switch]$Restore
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$taskName = 'SpotMonitor Camera Wall'
try {
    if ($Restore) {
        if (Test-Path -LiteralPath $StatePath) {
            $state = Get-Content -Raw -LiteralPath $StatePath | ConvertFrom-Json
            if ($state.TaskEnabled) { Enable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null }
            if ($state.MaintenanceRunning) { Start-Service RTSPViewMaintenance }
        }
        exit 0
    }
    $task = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
    $service = Get-Service RTSPViewMaintenance -ErrorAction SilentlyContinue
    # Preserve the original state across a retry of Inno's PrepareToInstall hook.
    if (!(Test-Path -LiteralPath $StatePath)) {
        @{ TaskEnabled=[bool]($task -and $task.Settings.Enabled); MaintenanceRunning=[bool]($service -and $service.Status -ne 'Stopped') } | ConvertTo-Json | Set-Content -LiteralPath $StatePath
    }
    if ($service -and $service.Status -ne 'Stopped') { $service.Stop(); $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30)) }
    if ($task) {
        Disable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null
        Stop-ScheduledTask -TaskName $taskName -TaskPath '\'
    }
    # Service updates launch this script directly in the user session, outside Task Scheduler.
    # Match the complete -File argument, never all PowerShell processes or a substring.
    $runner = Join-Path $root 'Run-Appliance.ps1'
    $supervisors = Get-CimInstance Win32_Process -Filter "Name = 'powershell.exe' OR Name = 'pwsh.exe'" |
        Where-Object {
            if ($_.ProcessId -eq $PID -or $_.CommandLine -notmatch '(?i)(?:^|\s)-File\s+(?:"([^"]+)"|(\S+))') { return $false }
            $scriptPath = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
            try { [IO.Path]::GetFullPath($scriptPath) -eq $runner } catch { $false }
        }
    foreach ($supervisor in $supervisors) {
        $process = Get-Process -Id $supervisor.ProcessId -ErrorAction SilentlyContinue
        if ($process) { $process.Kill(); if (!$process.WaitForExit(10000)) { throw 'The appliance supervisor did not stop.' }; $process.Dispose() }
    }
    $paths = @('Controller\SpotMonitor.Controller.exe','Viewer\SpotMonitor.Viewer.exe') | ForEach-Object { Join-Path $root $_ }
    foreach ($process in Get-Process -Name 'SpotMonitor.Controller','SpotMonitor.Viewer' -ErrorAction SilentlyContinue) {
        try {
            if ($process.Path -in $paths) {
                $process.Kill()
                if (!$process.WaitForExit(10000)) { throw 'An RTSPView process did not stop.' }
            }
        } finally { $process.Dispose() }
    }
    # Loaded runtime/native DLLs may outlive the UI. Refuse partial replacement if still locked.
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $locked = $null
        foreach ($component in @('Controller','Viewer','Maintenance')) {
            $directory = Join-Path $root $component
            if (!(Test-Path -LiteralPath $directory)) { continue }
            $stateDirectory = (Join-Path $root 'Maintenance\State') + '\'
            foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object {
                $_.Extension -in @('.dll','.exe') -and !$_.FullName.StartsWith($stateDirectory, [StringComparison]::OrdinalIgnoreCase)
            }) {
                try { $stream = [IO.File]::Open($file.FullName, 'Open', 'ReadWrite', 'None'); $stream.Dispose() }
                catch { $locked = $file.FullName; break }
            }
            if ($locked) { break }
        }
        if (!$locked) { exit 0 }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "A file is still locked or not writable: $locked. Close RTSPView and retry. No application files have been replaced."
} catch {
    $_.Exception.Message | Set-Content -LiteralPath ($StatePath + '.error')
    exit 1
}
