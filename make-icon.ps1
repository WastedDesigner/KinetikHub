# Draws icon.ico: a dark rounded tile with four small rounded squares in Kinetik's colours, one per app.
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Png([int]$s) {
    $b = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $m = [Math]::Max(0.5, $s * 0.06); $w = $s - 2 * $m; $r = $s * 0.2
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 34, 40))), (RoundRect $m $m $w $w $r))
    $cols = @(@(77, 166, 255), @(40, 190, 200), @(240, 88, 88), @(255, 170, 60))
    $t = $s * 0.27; $gap = $s * 0.07; $x0 = ($s - 2 * $t - $gap) / 2
    for ($i = 0; $i -lt 4; $i++) {
        $x = $x0 + ($i % 2) * ($t + $gap); $y = $x0 + [Math]::Floor($i / 2) * ($t + $gap); $c = $cols[$i]
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, $c[0], $c[1], $c[2]))), (RoundRect $x $y $t $t ($t * 0.28)))
    }
    $g.Dispose()
    $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 256
$imgs = $sizes | ForEach-Object { , (Png $_) }
$out = New-Object IO.MemoryStream
$bw = New-Object IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $imgs[$i].Length
    $bw.Write([byte]($s % 256)); $bw.Write([byte]($s % 256)); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
    $offset += $len
}
foreach ($img in $imgs) { $bw.Write($img) }
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'icon.ico'), $out.ToArray())
Write-Output "Wrote icon.ico"

