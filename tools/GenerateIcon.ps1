[CmdletBinding()]
param([string]$OutputPath)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "..\Assets\NetPulse.ico"
}

function New-RoundedRectangle([Drawing.RectangleF]$rect, [float]$radius) {
    $path = [Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($rect.Left, $rect.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.Left, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::Transparent)
    $scale = $size / 512.0

    $badge = New-RoundedRectangle ([Drawing.RectangleF]::new(20*$scale,20*$scale,472*$scale,472*$scale)) (108*$scale)
    $navy = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,13,35,58))
    $graphics.FillPath($navy, $badge)

    $network = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,78,134,173), [Math]::Max(1.5,24*$scale))
    $network.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $network.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLines($network, [Drawing.PointF[]]@(
        [Drawing.PointF]::new(112*$scale,172*$scale),
        [Drawing.PointF]::new(256*$scale,104*$scale),
        [Drawing.PointF]::new(400*$scale,172*$scale)))

    $pulse = [Drawing.Pen]::new([Drawing.Color]::FromArgb(255,40,215,239), [Math]::Max(2,38*$scale))
    $pulse.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pulse.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pulse.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawLines($pulse, [Drawing.PointF[]]@(
        [Drawing.PointF]::new(104*$scale,286*$scale), [Drawing.PointF]::new(176*$scale,286*$scale),
        [Drawing.PointF]::new(208*$scale,194*$scale), [Drawing.PointF]::new(270*$scale,384*$scale),
        [Drawing.PointF]::new(312*$scale,258*$scale), [Drawing.PointF]::new(336*$scale,286*$scale),
        [Drawing.PointF]::new(408*$scale,286*$scale)))

    foreach ($node in @(@(112,172,21,199,138),@(256,104,40,215,239),@(400,172,21,199,138))) {
        $outer = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,225,255,248))
        $inner = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,$node[2],$node[3],$node[4]))
        $graphics.FillEllipse($outer, ($node[0]-42)*$scale, ($node[1]-42)*$scale, 84*$scale, 84*$scale)
        $graphics.FillEllipse($inner, ($node[0]-30)*$scale, ($node[1]-30)*$scale, 60*$scale, 60*$scale)
        $outer.Dispose(); $inner.Dispose()
    }

    $stream = [IO.MemoryStream]::new()
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose(); $pulse.Dispose(); $network.Dispose(); $navy.Dispose(); $badge.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
    return ,$bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { New-IconPng $_ })
$directoryBytes = 6 + 16 * $images.Count
$offset = $directoryBytes
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$file = [IO.File]::Create($OutputPath)
$writer = [IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally { $writer.Dispose(); $file.Dispose() }

Write-Host "Created $OutputPath"
