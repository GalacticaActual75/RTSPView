# Run the shipped polling callback against fake window controls; no GUI or installer.
$ErrorActionPreference = 'Stop'
$source = Get-Content (Join-Path $PSScriptRoot '..\deployment\Show-UpdateProgress.ps1') -Raw
$callback = [scriptblock]::Create(($source -split '\$timer.Add_Tick\(\{',2)[1].Split(@('})'),[StringSplitOptions]::None)[0])
$StatusPath = Join-Path ([IO.Path]::GetTempPath()) ('RTSPView-progress-' + [Guid]::NewGuid().ToString('N') + '.json')
$WindowSession = 'test'
try {
    foreach ($state in @('complete','failed','installing','malformed','stale')) {
        $window = [pscustomobject]@{Closed=$false;Activated=$false}
        $window | Add-Member ScriptMethod Close { $this.Closed=$true }
        $window | Add-Member ScriptMethod Activate { $this.Activated=$true }
        $timer = [pscustomobject]@{Stopped=$false}; $timer | Add-Member ScriptMethod Stop { $this.Stopped=$true }
        $message=[pscustomobject]@{Text=''}; $detail=[pscustomobject]@{Text=''}; $title=[pscustomobject]@{Text=''}; $bar=[pscustomobject]@{Style='';Value=0}; $log=[pscustomobject]@{Enabled=$false}
        @{state=$state;message='Test result';windowSession='test';updatedAt=[DateTimeOffset]::UtcNow.AddMinutes($(if($state -eq 'stale'){-5}else{0})).ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $StatusPath
        if($state -eq 'malformed'){'{' | Set-Content -LiteralPath $StatusPath}
        & $callback
        if($window.Closed -ne ($state -eq 'complete')){throw "Unexpected window closure for $state"}
        if($state -eq 'failed' -and (!$timer.Stopped -or !$window.Activated)){throw 'Failure must remain visible'}
        Write-Output "PASS progress window $state"
    }
} finally {Remove-Item -LiteralPath $StatusPath -ErrorAction SilentlyContinue}
