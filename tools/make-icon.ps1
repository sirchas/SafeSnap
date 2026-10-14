# Generates src\SafeSnap\app.ico: a shield with a mosaic grid, at 16, 32, 48, and 256 px.
# Requires Windows PowerShell 5.1 and System.Drawing.

param(
    [string]$OutputPath = ""
)

Set-StrictMode -Version 2
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $root = Split-Path -Parent $PSScriptRoot
    $OutputPath = Join-Path $root "src\SafeSnap\app.ico"
}

function New-ShieldPoint([double]$x, [double]$y) {
    return New-Object System.Drawing.PointF -ArgumentList ([single]$x), ([single]$y)
}

function New-ShieldBitmap([int]$size) {
    $format = [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, $format
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $points = New-Object 'System.Drawing.PointF[]' 8
    $points[0] = New-ShieldPoint (0.50 * $size) (0.05 * $size)
    $points[1] = New-ShieldPoint (0.88 * $size) (0.18 * $size)
    $points[2] = New-ShieldPoint (0.82 * $size) (0.46 * $size)
    $points[3] = New-ShieldPoint (0.68 * $size) (0.70 * $size)
    $points[4] = New-ShieldPoint (0.50 * $size) (0.95 * $size)
    $points[5] = New-ShieldPoint (0.32 * $size) (0.70 * $size)
    $points[6] = New-ShieldPoint (0.18 * $size) (0.46 * $size)
    $points[7] = New-ShieldPoint (0.12 * $size) (0.18 * $size)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon($points)
    $graphics.SetClip($path)

    $bounds = New-Object System.Drawing.RectangleF -ArgumentList 0, 0, ([single]$size), ([single]$size)
    $top = [System.Drawing.Color]::FromArgb(255, 47, 125, 214)
    $bottom = [System.Drawing.Color]::FromArgb(255, 14, 48, 102)
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList $bounds, $top, $bottom, 90.0
    $graphics.FillPath($fill, $path)

    $cols = 3
    if ($size -ge 256) { $cols = 10 }
    elseif ($size -ge 48) { $cols = 6 }
    elseif ($size -ge 32) { $cols = 5 }

    $palette = @(
        [System.Drawing.Color]::FromArgb(255, 126, 208, 232),
        [System.Drawing.Color]::FromArgb(255, 244, 198, 84),
        [System.Drawing.Color]::FromArgb(255, 236, 242, 248),
        [System.Drawing.Color]::FromArgb(255, 72, 214, 156),
        [System.Drawing.Color]::FromArgb(255, 92, 146, 255),
        [System.Drawing.Color]::FromArgb(255, 255, 122, 102)
    )

    $gap = [Math]::Max(1.0, $size / 48.0)
    $span = 0.64 * $size
    $cell = $span / $cols
    $originX = 0.18 * $size
    $originY = 0.16 * $size
    for ($row = 0; $row -lt $cols; $row++) {
        for ($col = 0; $col -lt $cols; $col++) {
            $x = $originX + ($col * $cell) + ($gap / 2.0)
            $y = $originY + ($row * $cell) + ($gap / 2.0)
            $tile = New-Object System.Drawing.RectangleF -ArgumentList ([single]$x), ([single]$y), ([single]($cell - $gap)), ([single]($cell - $gap))
            $color = $palette[($col + ($row * 2)) % $palette.Length]
            $tileBrush = New-Object System.Drawing.SolidBrush $color
            $graphics.FillRectangle($tileBrush, $tile)
            $tileBrush.Dispose()
        }
    }

    $graphics.ResetClip()
    $penWidth = [Math]::Max(1.0, $size / 16.0)
    $outline = [System.Drawing.Color]::FromArgb(235, 244, 249, 255)
    $pen = New-Object System.Drawing.Pen -ArgumentList $outline, ([single]$penWidth)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($pen, $path)

    $pen.Dispose()
    $fill.Dispose()
    $path.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function Save-MultiIcon([string]$path, [System.Drawing.Bitmap[]]$bitmaps) {
    $pngs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($bitmap in $bitmaps) {
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngs.Add($stream.ToArray())
        $stream.Dispose()
    }

    $directory = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }

    $file = [System.IO.File]::Open($path, [System.IO.FileMode]::Create)
    $writer = New-Object System.IO.BinaryWriter $file
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$bitmaps.Length)
        $offset = 6 + (16 * $bitmaps.Length)
        for ($i = 0; $i -lt $bitmaps.Length; $i++) {
            $width = [int]$bitmaps[$i].Width
            $height = [int]$bitmaps[$i].Height
            $widthByte = [byte]0
            $heightByte = [byte]0
            if ($width -lt 256) { $widthByte = [byte]$width }
            if ($height -lt 256) { $heightByte = [byte]$height }
            $writer.Write($widthByte)
            $writer.Write($heightByte)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([int]$pngs[$i].Length)
            $writer.Write([int]$offset)
            $offset += $pngs[$i].Length
        }

        foreach ($png in $pngs) {
            $writer.Write($png)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}

$sizes = @(16, 32, 48, 256)
$images = New-Object System.Collections.Generic.List[System.Drawing.Bitmap]
try {
    foreach ($size in $sizes) {
        $images.Add((New-ShieldBitmap $size))
    }

    Save-MultiIcon $OutputPath $images.ToArray()
}
finally {
    foreach ($image in $images) {
        $image.Dispose()
    }
}

$icon = New-Object System.Drawing.Icon $OutputPath
$loaded = $icon.Width
$icon.Dispose()
Write-Output "Wrote $OutputPath (loader reported ${loaded}px)"
