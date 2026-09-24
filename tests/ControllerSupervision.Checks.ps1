$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/../deployment/Run-Appliance.ps1" -FunctionsOnly
$script:running = $false
$script:launches = 0
$state = @{ Failures = 0 }
$now = [datetime]'2026-09-22T00:00:00Z'
$probe = { $script:running }
$launch = { $script:launches++ }
Invoke-ControllerSupervisionStep $state $now $probe $launch
Invoke-ControllerSupervisionStep $state $now.AddSeconds(14) $probe $launch
if ($script:launches -ne 0) { throw 'Supervisor interrupted the application handoff grace period.' }
Invoke-ControllerSupervisionStep $state $now.AddSeconds(15) $probe $launch
Invoke-ControllerSupervisionStep $state $now.AddSeconds(16) $probe $launch
if ($script:launches -ne 1) { throw 'Supervisor failed to relaunch or ignored backoff.' }
Invoke-ControllerSupervisionStep $state $now.AddSeconds(20) $probe $launch
if ($script:launches -ne 2) { throw 'Supervisor did not retry a failed launch.' }
$script:running = $true
Invoke-ControllerSupervisionStep $state $now.AddSeconds(21) $probe $launch
Invoke-ControllerSupervisionStep $state $now.AddSeconds(90) $probe $launch
if ($script:launches -ne 2 -or $state.Failures -ne 0) { throw 'Healthy Controller was duplicated or backoff did not reset.' }
$script:running = $false
Invoke-ControllerSupervisionStep $state $now.AddSeconds(91) $probe $launch
$script:running = $true
Invoke-ControllerSupervisionStep $state $now.AddSeconds(100) $probe $launch
if ($script:launches -ne 2) { throw 'An application-managed restart was duplicated.' }
'PASS Controller supervision: grace period, bounded retries, healthy reset and existing-process handoff.'
