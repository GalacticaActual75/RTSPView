param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d{1,6}\.\d{1,6}\.\d{1,6}(-beta\.\d{1,6})?(-patch\.[1-9]\d{0,5})?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$releaseStatus = git status --porcelain
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect release checkout.' }
if ($releaseStatus.Length -ne 0) { throw 'Commit all changes before publishing a release.' }
$tag = "v$Version"
git tag -a $tag -m "RTSPView $Version"
if ($LASTEXITCODE -ne 0) { throw "Tag creation failed." }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "Tag push failed." }
Write-Host "GitHub is building release $tag."
