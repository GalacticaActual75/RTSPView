$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../deployment/Configure-Startup.ps1" -FunctionsOnly
$runner = "C:\Program Files\RTSPView\Run-Appliance.ps1"
$arguments = Get-StartupActionArguments $runner
if ($arguments -ne '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\Program Files\RTSPView\Run-Appliance.ps1" -AtLogon') { throw 'Startup action lost quoting, hidden launch or sign-in intent.' }
# Exercise lookup failure handling with mocked Task Scheduler; never modify a host task.
function Get-ScheduledTask { param($TaskName, $ErrorAction); Write-Error -Message 'Missing task' -Category ObjectNotFound -ErrorAction Stop }
if ($null -ne (Find-StartupTask)) { throw 'Missing task should be absent.' }
function Get-ScheduledTask { param($TaskName, $ErrorAction); Write-Error -Message 'Access denied' -Category PermissionDenied -ErrorAction Stop }
$rejected=$false
try { Find-StartupTask } catch { $rejected=$true }
if (!$rejected) { throw 'Access denial was falsely reported as disabled startup.' }
function Get-ScheduledTask { param($TaskName, $ErrorAction); return [pscustomobject]@{State='Ready';TaskName=$TaskName} }
if ((Find-StartupTask).State -ne 'Ready') { throw 'Existing task status was lost.' }
'PASS startup launcher arguments and task lookup: absent, permission-denied and existing task states.'
