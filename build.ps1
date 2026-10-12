$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $PSScriptRoot
try {
    & $compilerPath /nologo /target:winexe /optimize+ /platform:x64 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /win32manifest:app.manifest /out:视频分段工具.exe VideoSplitter.cs
    if ($LASTEXITCODE -ne 0) { throw '编译失败' }
} finally { Pop-Location }
