param([string]$Output = (Join-Path $PSScriptRoot '../src/EternalGG.App/Assets/erebusgg.ico'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$brandSizes = @(16,24,32,48,64,128,256)
$brandImages = @()
foreach ($brandSize in $brandSizes) {
    $brandBitmap = [Drawing.Bitmap]::new($brandSize, $brandSize)
    $brandGraphics = [Drawing.Graphics]::FromImage($brandBitmap)
    $brandGraphics.SmoothingMode = 'AntiAlias'
    $brandGraphics.Clear([Drawing.Color]::Transparent)
    $brandGraphics.ScaleTransform($brandSize / 48.0, $brandSize / 48.0)
    $brandTile = [Drawing.Drawing2D.GraphicsPath]::new()
    foreach ($brandArc in @(@(0,0,180),@(26,0,270),@(26,26,0),@(0,26,90))) {
        $brandTile.AddArc($brandArc[0], $brandArc[1], 22, 22, $brandArc[2], 90)
    }
    $brandTile.CloseFigure()
    $brandWhite = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(250,250,250))
    $brandBlack = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(17,17,17))
    $brandGraphics.FillPath($brandWhite, $brandTile)
    $brandPoints = @(@(9,8),@(40,8),@(34,16),@(17,16),@(17,20),@(34,20),@(28,28),@(17,28),@(17,32),@(28,32),@(22,40),@(9,40)) | ForEach-Object { [Drawing.PointF]::new($_[0],$_[1]) }
    $brandGraphics.FillPolygon($brandBlack,[Drawing.PointF[]]$brandPoints)
    $brandStream = [IO.MemoryStream]::new()
    $brandBitmap.Save($brandStream,[Drawing.Imaging.ImageFormat]::Png)
    $brandImages += ,$brandStream.ToArray()
    $brandStream.Dispose()
    $brandTile.Dispose()
    $brandWhite.Dispose()
    $brandBlack.Dispose()
    $brandGraphics.Dispose()
    $brandBitmap.Dispose()
}
$brandFile = [IO.File]::Create([IO.Path]::GetFullPath($Output))
$brandWriter = [IO.BinaryWriter]::new($brandFile)
try {
    $brandWriter.Write([uint16]0)
    $brandWriter.Write([uint16]1)
    $brandWriter.Write([uint16]$brandSizes.Count)
    $brandOffset = 6 + 16 * $brandSizes.Count
    for ($brandIndex = 0; $brandIndex -lt $brandSizes.Count; $brandIndex++) {
        $brandDimension = if ($brandSizes[$brandIndex] -eq 256) { 0 } else { $brandSizes[$brandIndex] }
        $brandWriter.Write([byte]$brandDimension)
        $brandWriter.Write([byte]$brandDimension)
        $brandWriter.Write([uint16]0)
        $brandWriter.Write([uint16]1)
        $brandWriter.Write([uint16]32)
        $brandWriter.Write([uint32]$brandImages[$brandIndex].Length)
        $brandWriter.Write([uint32]$brandOffset)
        $brandOffset += $brandImages[$brandIndex].Length
    }
    foreach ($brandBytes in $brandImages) { $brandWriter.Write([byte[]]$brandBytes) }
}
finally { $brandWriter.Dispose() }
