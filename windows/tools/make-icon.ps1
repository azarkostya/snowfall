# Generates src\snowflake.ico (PNG-compressed entries 16..256 px) with the same snowflake as the tray icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\snowflake.ico'
$sizes = 16, 24, 32, 48, 64, 256
$pngs = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $size / 32.0
    $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 40, 90, 160)), (4.5 * $k)
    $core = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), (2.2 * $k)
    foreach ($pen in $outline, $core) {
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
        for ($i = 0; $i -lt 6; $i++) {
            $a = $i * [Math]::PI / 3
            $c = [Math]::Cos($a); $s = [Math]::Sin($a)
            $cx = 16 * $k; $cy = 16 * $k
            $g.DrawLine($pen, [single]$cx, [single]$cy, [single]($cx + 13 * $k * $c), [single]($cy + 13 * $k * $s))
            $bx = $cx + 8 * $k * $c; $by = $cy + 8 * $k * $s
            foreach ($side in -1, 1) {
                $b = $a + $side * [Math]::PI / 4
                $g.DrawLine($pen, [single]$bx, [single]$by, [single]($bx + 4.5 * $k * [Math]::Cos($b)), [single]($by + 4.5 * $k * [Math]::Sin($b)))
            }
        }
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}

$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
Write-Output "Wrote $out"
