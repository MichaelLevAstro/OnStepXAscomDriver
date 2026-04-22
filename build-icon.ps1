# Generates a multi-resolution .ico from AppIcon.png.
param(
    [string]$Source = (Join-Path $PSScriptRoot 'AppIcon.png'),
    [string]$Dest   = (Join-Path $PSScriptRoot 'src\OnStepX.Hub\AppIcon.ico')
)

Add-Type -AssemblyName System.Drawing

$sizes = 16,24,32,48,64,128,256
$src = [System.Drawing.Image]::FromFile($Source)
try {
    $pngBlobs = @()
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $s, $s
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality= [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($src, 0, 0, $s, $s)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngBlobs += ,$ms.ToArray()
    }
} finally { $src.Dispose() }

$ms  = New-Object System.IO.MemoryStream
$bw  = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)             # reserved
$bw.Write([uint16]1)             # type=icon
$bw.Write([uint16]$sizes.Count)  # count
$offset = 6 + (16 * $sizes.Count)
for ($i=0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngBlobs[$i].Length
    $bw.Write([byte]($(if ($s -eq 256) {0} else {$s})))  # width (0 => 256)
    $bw.Write([byte]($(if ($s -eq 256) {0} else {$s})))  # height
    $bw.Write([byte]0)           # color palette
    $bw.Write([byte]0)           # reserved
    $bw.Write([uint16]1)         # color planes
    $bw.Write([uint16]32)        # bits per pixel
    $bw.Write([uint32]$len)      # size of blob
    $bw.Write([uint32]$offset)   # offset
    $offset += $len
}
foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($Dest, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Host "Wrote $Dest ($([System.IO.FileInfo]::new($Dest).Length) bytes)"
