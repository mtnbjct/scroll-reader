# Generates src/ScrollReader/Assets/app.ico — a dark circle with a text bar
# and the red ORP pivot dot. Run from the repo root: pwsh scripts/make-icon.ps1
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0x26, 0x2A, 0x33))
    $g.FillEllipse($bg, 0, 0, $s - 1, $s - 1)

    # text bar
    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xF2, 0xF3, 0xF5))
    $barH = [Math]::Max(2, $s * 0.14)
    $g.FillRectangle($bar, $s * 0.20, ($s - $barH) / 2, $s * 0.60, $barH)

    # ORP pivot dot, slightly left of center
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0xFF, 0x5C, 0x5C))
    $d = $s * 0.34
    $g.FillEllipse($dot, $s * 0.42 - $d / 2, ($s - $d) / 2, $d, $d)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    @{ Size = $s; Bytes = $ms.ToArray() }
}

$outDir = "src/ScrollReader/Assets"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim)      # width, height (0 = 256)
    $w.Write([byte]0); $w.Write([byte]0)            # palette, reserved
    $w.Write([uint16]1); $w.Write([uint16]32)       # planes, bpp
    $w.Write([uint32]$img.Bytes.Length)
    $w.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $w.Write($img.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes("$outDir/app.ico", $out.ToArray())
"wrote $outDir/app.ico ($($out.Length) bytes)"
