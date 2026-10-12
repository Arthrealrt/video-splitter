$ErrorActionPreference = 'Stop'
$ffmpegVersion = '9.0.2'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
$archiveName = "ffmpeg-$ffmpegVersion-essentials_build.zip"
$downloadUrl = "https://www.gyan.dev/ffmpeg/builds/packages/$archiveName"
$cacheDir = Join-Path $PSScriptRoot '.cache'
$archivePath = Join-Path $cacheDir $archiveName
$unpackDir = Join-Path $cacheDir "ffmpeg-$ffmpegVersion-unpacked"
New-Item -ItemType Directory -Force -Path $cacheDir | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
}
$actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if ($actualHash -ne $expectedHash) {
    throw "FFmpeg SHA-256 mismatch. Delete $archivePath and retry."
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $unpackDir -Force
$buildDir = Join-Path $unpackDir "ffmpeg-$ffmpegVersion-essentials_build"
foreach ($engineName in @('ffmpeg.exe', 'ffprobe.exe')) {
    Copy-Item -LiteralPath (Join-Path $buildDir "bin\$engineName") -Destination (Join-Path $PSScriptRoot $engineName) -Force
}
$licensesDir = Join-Path $PSScriptRoot 'licenses'
New-Item -ItemType Directory -Force -Path $licensesDir | Out-Null
Copy-Item -LiteralPath (Join-Path $buildDir 'LICENSE') -Destination (Join-Path $licensesDir 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $buildDir 'README.txt') -Destination (Join-Path $licensesDir 'README.txt') -Force
Write-Host "FFmpeg $ffmpegVersion installed; SHA-256 verified."
