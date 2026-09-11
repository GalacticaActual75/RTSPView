# Changes only the obsolete update-source description in the installed dashboard.
# Run in an administrator PowerShell on the RTSPView host.
param([string]$InstallDirectory)
$ErrorActionPreference = 'Stop'
if (-not $InstallDirectory) {
    $keys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{B49BC897-9B86-4C97-85BA-9FA1CF27A835}_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{B49BC897-9B86-4C97-85BA-9FA1CF27A835}_is1'
    )
    $locations = @($keys | ForEach-Object {
        if (Test-Path -LiteralPath $_) { (Get-ItemProperty -LiteralPath $_).InstallLocation }
    } | Where-Object { $_ } | Select-Object -Unique)
    if ($locations.Count -ne 1) { throw 'Specify -InstallDirectory with the installed RTSPView or SpotMonitor folder.' }
    $InstallDirectory = $locations[0]
}
$directory = (Resolve-Path -LiteralPath $InstallDirectory).ProviderPath
$page = Join-Path $directory 'Controller\wwwroot\index.html'
if (-not (Test-Path -LiteralPath $page -PathType Leaf)) { throw 'The selected folder does not contain the installed dashboard.' }
$old = 'Install verified RTSPView releases from the private LAN update channel.'
$new = 'Install verified RTSPView releases from GitHub. Stable and Beta both use the public repository.'
$content = [IO.File]::ReadAllText($page)
if ($content.Contains($new) -and -not $content.Contains($old)) {
    Write-Output 'The GitHub update description is already patched.'
    exit 0
}
if (-not $content.Contains($old)) { throw 'Expected wording was not found; no files were changed.' }
$backup = Join-Path $directory ('Controller\index.html.before-github-label-' + [Guid]::NewGuid().ToString('N') + '.bak')
$temporary = $page + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $content.Replace($old, $new), [Text.UTF8Encoding]::new($false))
    [IO.File]::Replace($temporary, $page, $backup)
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output 'Dashboard wording corrected. Refresh the browser (Ctrl+F5). No app restart is needed.'
Write-Output "Original page backup: $backup"
Write-Output 'This text-only patch does not change the updater, stream configuration, passwords or network access.'
