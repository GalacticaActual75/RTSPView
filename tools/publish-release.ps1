param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-beta\.\d+)?$')]
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
