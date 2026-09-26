param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\src\UsageBar.Windows\Assets\UsageBar.ico')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Bounds,
        [single]$Radius
    )

    $diameter = [Math]::Max(1.0, $Radius * 2)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$frames = [System.Collections.Generic.List[object]]::new()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(0.5, $size * 0.055)
    $tile = [System.Drawing.RectangleF]::new($inset, $inset, $size - 2 * $inset, $size - 2 * $inset)
    $tilePath = New-RoundedPath -Bounds $tile -Radius ($size * 0.24)
    $tileBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(37, 99, 235))
    $graphics.FillPath($tileBrush, $tilePath)

    $barBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $baseline = $size * 0.77
    $barWidth = $size * 0.12
    $gap = $size * 0.10
    $firstX = $size * 0.26
    $heights = @(($size * 0.23), ($size * 0.38), ($size * 0.53))
    for ($index = 0; $index -lt $heights.Count; $index++) {
        $bar = [System.Drawing.RectangleF]::new($firstX + $index * ($barWidth + $gap), $baseline - $heights[$index], $barWidth, $heights[$index])
        $barPath = New-RoundedPath -Bounds $bar -Radius ([Math]::Min($barWidth / 2, $size * 0.07))
        $graphics.FillPath($barBrush, $barPath)
        $barPath.Dispose()
    }

    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add([PSCustomObject]@{ Size = $size; Data = $memory.ToArray() })

    $memory.Dispose()
    $barBrush.Dispose()
    $tileBrush.Dispose()
    $tilePath.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$destination = [System.IO.Path]::GetFullPath($OutputPath)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
$stream = [System.IO.File]::Create($destination)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
    $writer.Write($dimension)
    $writer.Write($dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Data.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Data.Length
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Data) }
$writer.Dispose()
$stream.Dispose()

Write-Host "Wrote $destination"
