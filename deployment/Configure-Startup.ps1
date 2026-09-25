param(
    [ValidateSet('Status','Enable','Disable')][string]$Mode = 'Status',
    [string]$UserSid,
    [switch]$AllowElevation,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'

function Get-StartupActionArguments([string]$Runner) {
    return '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $Runner + '" -AtLogon'
}
function Find-StartupTask {
    try { return Get-ScheduledTask -TaskName 'SpotMonitor Camera Wall' -ErrorAction Stop }
    catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { return $null }; throw }
}
if ($FunctionsOnly) { return }
if (!$UserSid) { $UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
# Validate the identity passed from the unelevated Controller/installer before UAC.
$null = New-Object Security.Principal.SecurityIdentifier($UserSid)
$root = Split-Path $PSScriptRoot -Parent
$runner = Join-Path $root 'Run-Appliance.ps1'
$taskName = 'SpotMonitor Camera Wall'
try {
    if (!(Test-Path -LiteralPath $runner -PathType Leaf)) { throw 'The installed startup launcher is missing.' }
    if ($Mode -ne 'Status') {
        $admin = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        if (!$admin -and $AllowElevation) {
            $arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $PSCommandPath + '" -Mode ' + $Mode + ' -UserSid ' + $UserSid
            $process = Start-Process -FilePath powershell.exe -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
            exit $process.ExitCode
        }
        if ($Mode -eq 'Enable') {
            $action = New-ScheduledTaskAction -Execute (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -Argument (Get-StartupActionArguments $runner) -WorkingDirectory $root
            $trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserSid
            $trigger.Delay = 'PT20S'
            $principal = New-ScheduledTaskPrincipal -UserId $UserSid -LogonType Interactive -RunLevel Limited
            $settings = New-ScheduledTaskSettingsSet -RestartCount 20 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew
            Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Opens RTSPView Live View at Windows sign-in and supervises the application.' -Force | Out-Null
        } else {
            $task = Find-StartupTask
            if ($task) { Disable-ScheduledTask -TaskName $taskName | Out-Null }
        }
    }
    $task = Find-StartupTask
    $enabled = $false; $repair = $false
    if ($task) {
        $enabled = $task.State -ne 'Disabled'
        $taskSid = $task.Principal.UserId
        try { if ($taskSid -notmatch '^S-1-') { $taskSid = (New-Object Security.Principal.NTAccount($taskSid)).Translate([Security.Principal.SecurityIdentifier]).Value } } catch { $taskSid = '' }
        $repair = $enabled -and ($taskSid -ne $UserSid -or $task.Principal.LogonType -ne 'Interactive' -or
            $task.Actions.Arguments -notcontains (Get-StartupActionArguments $runner) -or $task.Settings.DisallowStartIfOnBatteries -or $task.Settings.StopIfGoingOnBatteries)
    }
    @{ enabled=$enabled; managed=$true; repairNeeded=$repair; message=$(if($repair){'Startup needs repair for this Windows account. Apply the enabled setting to repair it.'}elseif($enabled){'Live View opens about 20 seconds after this Windows account signs in.'}else{'Live View will not open automatically at Windows sign-in.'}) } | ConvertTo-Json -Compress
    exit 0
} catch {
    Write-Error 'Windows startup could not be configured or inspected. Check permissions and Task Scheduler.' -ErrorAction Continue
    exit 1
}
