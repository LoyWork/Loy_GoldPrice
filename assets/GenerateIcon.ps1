# Original vector-style artwork for Loy Gold Price. No external image dependencies.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-GoldBitmap([int]$size) {
    $scale = 4
    $large = New-Object Drawing.Bitmap ($size * $scale), ($size * $scale)
    $g = [Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform(($size * $scale / 32.0), ($size * $scale / 32.0))
    $background = New-Object Drawing.Drawing2D.GraphicsPath
    $background.AddArc(1, 1, 10, 10, 180, 90)
    $background.AddArc(21, 1, 10, 10, 270, 90)
    $background.AddArc(21, 21, 10, 10, 0, 90)
    $background.AddArc(1, 21, 10, 10, 90, 90)
    $background.CloseFigure()
    $baseBrush = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#20252D'))
    $g.FillPath($baseBrush, $background)
    $top = [Drawing.PointF[]]@([Drawing.PointF]::new(9,10), [Drawing.PointF]::new(23,10), [Drawing.PointF]::new(27,19), [Drawing.PointF]::new(5,19))
    $front = [Drawing.PointF[]]@([Drawing.PointF]::new(5,19), [Drawing.PointF]::new(27,19), [Drawing.PointF]::new(24,24), [Drawing.PointF]::new(8,24))
    $topBrush = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(8,10)), ([Drawing.Point]::new(24,20)), ([Drawing.ColorTranslator]::FromHtml('#FFE5A1')), ([Drawing.ColorTranslator]::FromHtml('#E9AF39'))
    $frontBrush = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#B97820'))
    $g.FillPolygon($topBrush, $top)
    $g.FillPolygon($frontBrush, $front)
    $edge = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#FFE2A0')), 0.8
    $g.DrawLine($edge, 6, 19, 26, 19)
    $shine = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#FFF0C5')), 1.2
    $shine.StartCap = $shine.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($shine, 11, 12, 15, 12)
    $g.Dispose(); $background.Dispose(); $baseBrush.Dispose(); $topBrush.Dispose(); $frontBrush.Dispose(); $edge.Dispose(); $shine.Dispose()
    $result = New-Object Drawing.Bitmap $size, $size
    $resize = [Drawing.Graphics]::FromImage($result)
    $resize.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $resize.DrawImage($large, 0, 0, $size, $size)
    $resize.Dispose(); $large.Dispose()
    return ,$result
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = New-GoldBitmap $size
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $frames += ,$stream.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $PSScriptRoot 'gold.png'), [Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose(); $stream.Dispose()
}
$file = [IO.File]::Create((Join-Path $PSScriptRoot 'gold.ico'))
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Host 'Generated gold.ico (16-256 px) and gold.png'
