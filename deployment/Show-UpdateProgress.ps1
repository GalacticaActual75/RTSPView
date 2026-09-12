param([Parameter(Mandatory = $true)][string]$StatusPath, [string]$WindowSession = 'staging')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$window = New-Object System.Windows.Forms.Form
$window.Text = 'RTSPView update'
$window.ClientSize = New-Object System.Drawing.Size(560, 260)
$window.StartPosition = 'CenterScreen'
$window.FormBorderStyle = 'FixedDialog'
$window.MaximizeBox = $false
$window.TopMost = $true
$window.Font = New-Object System.Drawing.Font('Segoe UI', 11)
$title = New-Object System.Windows.Forms.Label
$title.SetBounds(24, 20, 512, 30)
$title.Text = 'Updating RTSPView'
$title.Font = New-Object System.Drawing.Font('Segoe UI', 16, ([System.Drawing.FontStyle]::Bold))
$message = New-Object System.Windows.Forms.Label
$message.SetBounds(24, 64, 512, 68)
$message.Text = 'Preparing update...'
$bar = New-Object System.Windows.Forms.ProgressBar
$bar.SetBounds(24, 140, 512, 18)
$bar.Style = 'Marquee'
$detail = New-Object System.Windows.Forms.Label
$detail.SetBounds(24, 169, 512, 35)
$detail.Font = New-Object System.Drawing.Font('Segoe UI', 9)
$detail.Text = 'The camera wall may close and reconnect. Closing this window does not cancel the update.'
$close = New-Object System.Windows.Forms.Button
$close.SetBounds(426, 213, 110, 32)
$close.Text = 'Close'
$close.Add_Click({ $window.Close() })
$log = New-Object System.Windows.Forms.Button
$log.SetBounds(24, 213, 140, 32)
$log.Text = 'Open update log'
$log.Enabled = $false
$script:logPath = $null
$log.Add_Click({ if ($script:logPath -and (Test-Path -LiteralPath $script:logPath)) { Start-Process -FilePath 'notepad.exe' -ArgumentList ('"' + $script:logPath + '"') -WindowStyle Normal } })
$window.Controls.AddRange(@($title, $message, $bar, $detail, $close, $log))
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 400
$timer.Add_Tick({
    try {
        $status = Get-Content -LiteralPath $StatusPath -Raw | ConvertFrom-Json
        if ($status.windowSession -and $status.windowSession -ne $WindowSession) { $window.Close(); return }
        $message.Text = $status.message
        if ($status.logPath) { $script:logPath = $status.logPath; $log.Enabled = Test-Path -LiteralPath $script:logPath }
        if ($status.state -in @('complete', 'failed')) {
            if ($status.state -eq 'complete') { $timer.Stop(); $window.Close(); return }
            $bar.Style = 'Continuous'
            $bar.Value = if ($status.state -eq 'complete') { 100 } else { 0 }
            $title.Text = if ($status.state -eq 'complete') { 'Update complete' } else { 'Update needs attention' }
            $detail.Text = 'This result stays open until you close it.'
            $timer.Stop()
            $window.Activate()
        } elseif (([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse($status.updatedAt)).TotalMinutes -gt 3) {
            $detail.Text = 'Still waiting for the updater. If this persists, check the update log. Do not start another installer.'
        }
    } catch { $detail.Text = 'Waiting for update status. Closing this window does not cancel the update.' }
})
$window.Add_Shown({ $timer.Start(); $window.Activate() })
try { [System.Windows.Forms.Application]::Run($window) }
finally { $timer.Stop(); $timer.Dispose(); $window.Dispose() }
