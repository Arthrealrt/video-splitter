$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'download-ffmpeg.ps1')
$distDir = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$packagePath = Join-Path $distDir 'video-splitter-v1.1.0-windows-x64.zip'
$packageFiles = @('视频分段工具.exe', 'ffmpeg.exe', 'ffprobe.exe', 'VideoSplitter.cs', 'app.manifest', 'build.ps1', '使用说明.txt', '验证结果.txt', 'LICENSE.txt', 'licenses')
$resolvedFiles = $packageFiles | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $resolvedFiles -DestinationPath $packagePath -Force
Write-Host "Package ready: $packagePath"
