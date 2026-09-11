param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-beta\.\d+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
if ((git status --porcelain).Length -ne 0) { throw 'Commit all changes before publishing a release.' }
$tag = "v$Version"
git tag -a $tag -m "RTSPView $Version"
git push origin $tag
Write-Host "GitHub is building release $tag."
