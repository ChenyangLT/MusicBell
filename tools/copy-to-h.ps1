# 把 dist 里的 1.2.0 产物归档到 H:\Software\电教\MusicBell
# 用法：powershell -ExecutionPolicy Bypass -File tools\copy-to-h.ps1
$ErrorActionPreference = 'Stop'
$ver = '1.2.0'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$dst  = 'H:\Software\电教\MusicBell'

if (-not (Test-Path 'H:\')) {
    Write-Host 'H 盘当前没有挂载，插上/映射好再跑一次。' -ForegroundColor Yellow
    exit 1
}

New-Item -ItemType Directory -Path $dst -Force | Out-Null
Get-ChildItem $dst | Where-Object { $_.Name -match 'MusicBell-(Setup-)?1\.' -and $_.Name -notmatch [regex]::Escape($ver) } | Remove-Item -Force -Recurse
if (Test-Path (Join-Path $dst 'MusicBell')) { Remove-Item (Join-Path $dst 'MusicBell') -Recurse -Force }

Copy-Item (Join-Path $dist 'MusicBell') -Destination (Join-Path $dst 'MusicBell') -Recurse -Force
Copy-Item (Join-Path $dist "MusicBell-$ver-win-x64.zip") -Destination $dst -Force
Copy-Item (Join-Path $dist "MusicBell-Setup-$ver.exe")   -Destination $dst -Force

Get-ChildItem $dst | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize