Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Drawing.Drawing2D

$outDir = Join-Path $PSScriptRoot '..\src\MusicBell.App\Assets'
$outDir = [System.IO.Path]::GetFullPath($outDir)
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [double]$size * 0.06
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($size - 2*$pad), ($size - 2*$pad)
    $radius = [double]$size * 0.26

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255,255,95,126)), ([System.Drawing.Color]::FromArgb(255,176,124,255)), 45.0
    $g.FillPath($brush, $path)

    # 高光
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(60,255,255,255)), ([System.Drawing.Color]::FromArgb(0,255,255,255)), 90.0
    $g.FillPath($hl, $path)

    # 音符（八分音符，纯几何绘制）
    $w = [double]$size
    $noteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(245,255,255,255))
    $stemW = [double]$w * 0.075
    $stemX = [double]$w * 0.575
    $stemTop = [double]$w * 0.27
    $stemBottom = [double]$w * 0.66
    $g.FillRectangle($noteBrush, [float]$stemX, [float]$stemTop, [float]$stemW, [float]($stemBottom - $stemTop))
    # 符头
    $hw = [double]$w * 0.20
    $hh = [double]$w * 0.145
    $g.FillEllipse($noteBrush, [float]($stemX - $hw * 0.62), [float]($stemBottom - $hh * 0.45), [float]$hw, [float]$hh)
    # 符尾
    $flag = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fx = [float]($stemX + $stemW)
    $fy = [float]$stemTop
    $flag.AddBezier($fx, $fy, [float]($fx + $w*0.16), [float]($fy + $w*0.10), [float]($fx + $w*0.14), [float]($fy + $w*0.22), [float]($fx + $w*0.03), [float]($fy + $w*0.30))
    $flag.AddLine([float]($fx + $w*0.03), [float]($fy + $w*0.30), [float]$fx, [float]($fy + $w*0.16))
    $flag.CloseFigure()
    $g.FillPath($noteBrush, $flag)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,@($s, $ms.ToArray())
    $bmp.Dispose()
}

$icoPath = Join-Path $outDir 'app.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($p in $pngs) {
    $s = $p[0]; $bytes = $p[1]
    $dim = 0
    if ($s -lt 256) { $dim = $s }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0); $bw.Write([Byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($p in $pngs) { $bw.Write($p[1]) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

# 同时保存一张 256 PNG 便于预览
$preview = New-IconBitmap 256
$preview.Save((Join-Path $outDir 'app-256.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Output ("OK " + $icoPath + " " + (Get-Item $icoPath).Length + " bytes")
