param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $scale = $Size / 256.0
    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $outer = New-RoundedRectPath 3 3 250 250 48
    $outerBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 16, 23, 31))
    $graphics.FillPath($outerBrush, $outer)

    $outerPen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 45, 61, 75), 4 * $scale)
    $outerPen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Inset
    $graphics.DrawPath($outerPen, $outer)

    $panels = @(
        @{ X = 22.0;  Accent = [System.Drawing.Color]::FromArgb(255, 74, 210, 197) },
        @{ X = 95.0;  Accent = [System.Drawing.Color]::FromArgb(255, 255, 119, 98) },
        @{ X = 168.0; Accent = [System.Drawing.Color]::FromArgb(255, 255, 196, 84) }
    )

    $panelWidth = 66.0
    $panelTop = 34.0
    $panelHeight = 188.0
    $panelRadius = 11.0
    $panelFill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 6, 13, 19))
    $glyphBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 246, 250, 252))

    foreach ($panel in $panels) {
        $panelPath = New-RoundedRectPath `
            ($panel.X * $scale) `
            ($panelTop * $scale) `
            ($panelWidth * $scale) `
            ($panelHeight * $scale) `
            ($panelRadius * $scale)
        $graphics.FillPath($panelFill, $panelPath)

        $panelPen = [System.Drawing.Pen]::new($panel.Accent, [Math]::Max(1.4, 5 * $scale))
        $panelPen.Alignment = [System.Drawing.Drawing2D.PenAlignment]::Inset
        $graphics.DrawPath($panelPen, $panelPath)
        $panelPen.Dispose()
        $panelPath.Dispose()
    }

    # Left and right panes use a simple landscape mark; the center pane gets the play symbol.
    foreach ($panel in @($panels[0], $panels[2])) {
        $sunRect = [System.Drawing.RectangleF]::new(
            [float](($panel.X + 16) * $scale),
            [float](68 * $scale),
            [float](12 * $scale),
            [float](12 * $scale))
        $graphics.FillEllipse($glyphBrush, $sunRect)

        $mountain = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $mountain.AddPolygon([System.Drawing.PointF[]]@(
            [System.Drawing.PointF]::new([float](($panel.X + 11) * $scale), [float](145 * $scale)),
            [System.Drawing.PointF]::new([float](($panel.X + 30) * $scale), [float](112 * $scale)),
            [System.Drawing.PointF]::new([float](($panel.X + 44) * $scale), [float](132 * $scale)),
            [System.Drawing.PointF]::new([float](($panel.X + 55) * $scale), [float](121 * $scale)),
            [System.Drawing.PointF]::new([float](($panel.X + 55) * $scale), [float](145 * $scale))
        ))
        $graphics.FillPath($glyphBrush, $mountain)
        $mountain.Dispose()
    }

    $play = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $play.AddPolygon([System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new([float](116 * $scale), [float](92 * $scale)),
        [System.Drawing.PointF]::new([float](116 * $scale), [float](164 * $scale)),
        [System.Drawing.PointF]::new([float](166 * $scale), [float](128 * $scale))
    ))
    $graphics.FillPath($glyphBrush, $play)

    $play.Dispose()
    $glyphBrush.Dispose()
    $panelFill.Dispose()
    $outerPen.Dispose()
    $outerBrush.Dispose()
    $outer.Dispose()
    $graphics.Dispose()
    return $bitmap
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$pngFrames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 128, 256)

foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngFrames.Add($stream.ToArray())
    $stream.Dispose()
    $bitmap.Dispose()
}

$icoPath = Join-Path $OutputDirectory 'app.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)

    $offset = 6 + (16 * $sizes.Count)
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $size = $sizes[$i]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$pngFrames[$i].Length)
        $writer.Write([uint32]$offset)
        $offset += $pngFrames[$i].Length
    }

    foreach ($frame in $pngFrames) {
        $writer.Write($frame)
    }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$preview = New-IconBitmap 256
$previewPath = Join-Path $OutputDirectory 'app-icon.png'
$preview.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

Write-Output "Created $icoPath"
Write-Output "Created $previewPath"
