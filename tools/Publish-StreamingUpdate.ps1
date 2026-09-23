param([Parameter(Mandatory)][string]$Repository, [Parameter(Mandatory)][string]$Commit)
$ErrorActionPreference = 'Stop'
$package = 'artifacts/stream-resolver/stream-resolver'
$versions = Get-Content "$package/versions.json" -Raw | ConvertFrom-Json
$sequence = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$tag = "streaming-$sequence"
$archive = "artifacts/streaming-$sequence.zip"
Compress-Archive -Path "$package/*" -DestinationPath $archive -CompressionLevel Optimal
$manifest = [ordered]@{
  Sequence = $sequence; Protocol = 1; Platform = 'win-x64'
  Url = "https://github.com/$Repository/releases/download/$tag/streaming-$sequence.zip"
  Sha256 = (Get-FileHash $archive -Algorithm SHA256).Hash
  Size = (Get-Item $archive).Length
  YtDlp = $versions.'yt-dlp'; Streamlink = $versions.streamlink; Ejs = $versions.'yt-dlp-ejs'
}
$payload = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Compress))
$key = [Security.Cryptography.RSA]::Create()
try {
  $key.ImportFromPem($env:STREAMING_UPDATE_SIGNING_KEY)
  $signature = $key.SignData($payload, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pss)
} finally { $key.Dispose(); $env:STREAMING_UPDATE_SIGNING_KEY = $null }
$envelope = @{payload=[Convert]::ToBase64String($payload); signature=[Convert]::ToBase64String($signature)} | ConvertTo-Json -Compress
[IO.File]::WriteAllText('artifacts/manifest.json', $envelope)
$notes = "Automatically tested streaming components: yt-dlp $($versions.'yt-dlp'), Streamlink $($versions.streamlink). Requires RTSPView streaming updater protocol 1. Website availability is not guaranteed."
gh release create $tag $archive --repo $Repository --target $Commit --prerelease --latest=false --title $tag --notes $notes
if ($LASTEXITCODE) { throw 'Package publication failed' }
gh release view streaming-current --repo $Repository *> $null
if ($LASTEXITCODE) {
  gh release create streaming-current artifacts/manifest.json --repo $Repository --target $Commit --prerelease --latest=false --title 'Automatic streaming components' --notes $notes
} else {
  gh release upload streaming-current artifacts/manifest.json --repo $Repository --clobber
}
if ($LASTEXITCODE) { throw 'Manifest publication failed' }
