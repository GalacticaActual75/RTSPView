# Fixed firewall operation; no web-supplied commands, addresses or paths.
$ErrorActionPreference = 'Stop'
try {
    if (-not (Get-NetConnectionProfile | Where-Object NetworkCategory -eq 'Private')) { exit 2 }
    $name = 'RTSPView-Admin-LAN'
    $rule = Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue
    if ($rule) { Remove-NetFirewallRule -Name $name }
    New-NetFirewallRule -Name $name -DisplayName 'RTSPView Admin - Private LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5080 -RemoteAddress LocalSubnet -Profile Private -Enabled True | Out-Null
    foreach ($legacy in @('SpotMonitor Web Admin - Private LAN','SpotMonitor Web Admin - Block Public','SpotMonitor Web Admin - LAN Only')) {
        Get-NetFirewallRule -DisplayName $legacy -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    }
    exit 0
} catch { exit 1 }
