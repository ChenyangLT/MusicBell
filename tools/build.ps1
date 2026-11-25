<# MusicBell 一键打包脚本
   输出：dist\MusicBell\（主程序 + plugin 子目录）以及 dist\MusicBell-1.0.0-win-x64.zip #>
param(
    [switch]$FrameworkDependent,   # 依赖系统已安装的 .NET 8 桌面运行时（体积小）
    [switch]$NoZip,
    [switch]$SkipPlugin,
    [switch]$NoInstaller
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root "dist"
$out  = Join-Path $dist "MusicBell"
$ver  = "1.1.0"

Write-Host "== 停止正在运行的 MusicBell ==" -ForegroundColor Cyan
Get-Process -Name MusicBell -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force -Path $out | Out-Null

$selfContained = (-not $FrameworkDependent).ToString().ToLower()
Write-Host "== 发布主程序（self-contained=$selfContained）==" -ForegroundColor Cyan
& dotnet publish (Join-Path $root "src\MusicBell.App\MusicBell.App.csproj") -c Release -r win-x64 --self-contained $selfContained -p:PublishSingleFile=false -p:DebugType=none -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "主程序发布失败" }

if (-not $SkipPlugin) {
    Write-Host "== 发布 ClassIsland 插件 ==" -ForegroundColor Cyan
    $plugTmp = Join-Path $dist "_plugin"
    if (Test-Path $plugTmp) { Remove-Item $plugTmp -Recurse -Force }
    & dotnet publish (Join-Path $root "src\MusicBell.ClassIsland\MusicBell.ClassIsland.csproj") -c Release -o $plugTmp --nologo
    if ($LASTEXITCODE -ne 0) { throw "插件发布失败" }
    $plugDst = Join-Path $out "plugin"
    New-Item -ItemType Directory -Force -Path $plugDst | Out-Null
    Copy-Item (Join-Path $plugTmp "*") $plugDst -Recurse -Force
    Remove-Item (Join-Path $plugDst "*.pdb") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $plugDst "*.runtimeconfig.json") -Force -ErrorAction SilentlyContinue
    Remove-Item $plugTmp -Recurse -Force
    Write-Host ("   插件已放入 " + $plugDst)
}

Copy-Item (Join-Path $root "README.md") $out -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $root "docs") (Join-Path $out "docs") -Recurse -Force -ErrorAction SilentlyContinue

if (-not $NoZip) {
    $zip = Join-Path $dist ("MusicBell-" + $ver + "-win-x64.zip")
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "== 打包 zip ==" -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal
    Write-Host ("   " + $zip)
}

if (-not $NoInstaller) {
    $iscc = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($iscc) {
        Write-Host "== 编译安装包 ==" -ForegroundColor Cyan
        & $iscc "/DMyVersion=$ver" (Join-Path $root "tools\installer.iss") | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "   安装包编译失败（不影响便携版）" -ForegroundColor Yellow }
    } else {
        Write-Host "   未找到 Inno Setup，跳过安装包" -ForegroundColor Yellow
    }
}

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("== 完成：" + $out + "  共 " + [math]::Round($size,1) + " MB ==") -ForegroundColor Green
