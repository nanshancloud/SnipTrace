# 生成 src/SnipTrace/Assets/app.ico（多尺寸 PNG 条目）
# 用法：pwsh -File tools/make-icon.ps1 [-OutFile <路径>]
param(
    [string]$OutFile = (Join-Path $PSScriptRoot "..\src\SnipTrace\Assets\app.ico")
)

Add-Type -AssemblyName System.Drawing

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 圆角方块底
    $radius = [math]::Max(2, $size * 0.26)
    $diameter = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
    $path.AddArc($size - $diameter, 0, $diameter, $diameter, 270, 90)
    $path.AddArc($size - $diameter, $size - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc(0, $size - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()

    $accent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 107, 95))
    $g.FillPath($accent, $path)

    # 白色取景框 + 顶部刻线（小尺寸省略细节，避免糊成一团）
    $penWidth = [math]::Max(1.2, $size * 0.075)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), $penWidth
    if ($size -ge 24) {
        $g.DrawRectangle($pen, $size * 0.28, $size * 0.33, $size * 0.44, $size * 0.34)
        $g.DrawLine($pen, $size * 0.375, $size * 0.24, $size * 0.625, $size * 0.24)
    }

    # 中心圆点
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $dotSize = $size * 0.19
    $g.FillEllipse($dot, ($size - $dotSize) / 2, ($size - $dotSize) / 2, $dotSize, $dotSize)

    $g.Dispose()
    $accent.Dispose()
    $pen.Dispose()
    $dot.Dispose()
    $path.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$blobs = @()
foreach ($size in $sizes) {
    $bitmap = New-LogoBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $blobs += , $stream.ToArray()
    $bitmap.Dispose()
    $stream.Dispose()
}

$directory = Split-Path -Parent $OutFile
if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Force $directory | Out-Null }

$file = [System.IO.File]::Create($OutFile)
$writer = New-Object System.IO.BinaryWriter $file
$writer.Write([uint16]0)              # reserved
$writer.Write([uint16]1)              # type = icon
$writer.Write([uint16]$sizes.Count)   # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size = $sizes[$i]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)            # palette
    $writer.Write([byte]0)            # reserved
    $writer.Write([uint16]1)          # planes
    $writer.Write([uint16]32)         # bpp
    $writer.Write([uint32]$blobs[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $blobs[$i].Length
}

foreach ($blob in $blobs) { $writer.Write($blob) }
$writer.Flush()
$file.Close()

Write-Host "已生成 $OutFile（$($sizes -join '/') px）"
