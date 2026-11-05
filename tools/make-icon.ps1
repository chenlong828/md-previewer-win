# 生成应用图标：resources/app.ico（多尺寸）+ artifacts/icon-preview.png（256 预览，便于肉眼核对）
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\make-icon.ps1
# 依赖 System.Drawing（Windows PowerShell 自带），尺寸 16/24/32/48/64/128/256，PNG 压缩条目。
param(
    [string]$OutIcon = 'resources\app.ico',
    [string]$PreviewPath = 'artifacts\icon-preview.png'
)

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角底：深蓝 → 亮蓝斜向渐变
    $rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $radius = [float]($size * 0.22)
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
        [System.Drawing.Color]::FromArgb(255, 14, 165, 233),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($brush, $path)

    # 白色 “M”：两条竖线 + 中间 V，画出 Markdown 的棱角感
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float]([Math]::Max(1.4, $size * 0.11)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $left = $size * 0.24; $right = $size * 0.76
    $top = $size * 0.28; $bottom = $size * 0.72; $mid = $size * 0.52
    $midX = ($left + $right) / 2
    $points = @(
        (New-Object System.Drawing.PointF($left, $bottom)),
        (New-Object System.Drawing.PointF($left, $top)),
        (New-Object System.Drawing.PointF($midX, $mid)),
        (New-Object System.Drawing.PointF($right, $top)),
        (New-Object System.Drawing.PointF($right, $bottom))
    )
    $g.DrawLines($pen, [System.Drawing.PointF[]]$points)

    if ($size -ge 32) {
        # 底部一条短横线，暗示“正文/预览”
        $bar = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(220, 255, 255, 255))
        $w = $size * 0.34; $h = [float][Math]::Max(1.5, $size * 0.055)
        $g.FillRectangle($bar, [float](($size - $w) / 2), [float]($size * 0.80), [float]$w, $h)
        $bar.Dispose()
    }

    $pen.Dispose(); $brush.Dispose(); $path.Dispose(); $g.Dispose()
    return $bmp
}

# ICO 容器：每帧 PNG 字节（Vista+ 支持），Directories 里精确记录尺寸
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $size; Bytes = $ms.ToArray() }
    if ($size -eq 256) {
        $preview = Join-Path $root $PreviewPath
        New-Item -ItemType Directory -Force (Split-Path -Parent $preview) | Out-Null
        $bmp.Save($preview, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $ms.Dispose(); $bmp.Dispose()
}

$iconPath = Join-Path $root $OutIcon
New-Item -ItemType Directory -Force (Split-Path -Parent $iconPath) | Out-Null
$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([UInt16]0)                  # reserved
$writer.Write([UInt16]1)                  # type = icon
$writer.Write([UInt16]$pngs.Count)        # frame count
$offset = 6 + 16 * $pngs.Count
foreach ($frame in $pngs) {
    $size = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }  # 256 记作 0
    $writer.Write($size)                  # width
    $writer.Write($size)                  # height
    $writer.Write([byte]0)                # palette
    $writer.Write([byte]0)                # reserved
    $writer.Write([UInt16]1)              # color planes
    $writer.Write([UInt16]32)             # bpp
    $writer.Write([UInt32]$frame.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $pngs) { $writer.Write($frame.Bytes) }
$writer.Flush()
[System.IO.File]::WriteAllBytes($iconPath, $stream.ToArray())
$writer.Dispose(); $stream.Dispose()

Write-Output ("icon: {0} ({1} bytes, {2} frames)" -f $iconPath, (Get-Item $iconPath).Length, $pngs.Count)
Write-Output ("preview: {0}" -f (Join-Path $root $PreviewPath))
