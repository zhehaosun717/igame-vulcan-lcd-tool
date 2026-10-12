# Generates app.ico (multi-size, PNG-compressed) - a dark rounded tile with a wide "LCD strip" showing cyan bars.
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-Frame([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.Clear([System.Drawing.Color]::Transparent)
  function RR($x, $y, $w, $h, $r) { $p = New-Object System.Drawing.Drawing2D.GraphicsPath; $d = $r * 2; $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90); $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90); $p.CloseFigure(); $p }
  $pad = [Math]::Max(1, $s * 0.04)
  $tile = RR $pad $pad ($s - 2 * $pad) ($s - 2 * $pad) ($s * 0.22)
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)), [System.Drawing.Color]::FromArgb(255, 30, 41, 59), [System.Drawing.Color]::FromArgb(255, 8, 12, 24))
  $g.FillPath($br, $tile)
  # LCD strip
  $sx = $s * 0.14; $sw = $s * 0.72; $sh = $s * 0.30; $sy = $s * 0.35
  $strip = RR $sx $sy $sw $sh ($s * 0.05)
  $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 2, 6, 14))), $strip)
  if ($s -ge 24) { $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 34, 211, 238), [Math]::Max(1, $s * 0.025))), $strip) }
  # bars inside the strip
  $n = 5; $bw = $sw * 0.11; $gap = ($sw - $n * $bw) / ($n + 1)
  $hs = 0.35, 0.7, 0.5, 0.9, 0.6
  for ($i = 0; $i -lt $n; $i++) {
    $bh = ($sh * 0.72) * $hs[$i]; $bx = $sx + $gap + $i * ($bw + $gap); $by = $sy + $sh * 0.86 - $bh
    $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 211, 238))), [single]$bx, [single]$by, [single]$bw, [single]$bh)
  }
  # small "GPU fan" dots under the strip
  if ($s -ge 32) {
    $r = $s * 0.035
    foreach ($fx in 0.34, 0.5, 0.66) { $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 100, 116, 139))), [single]($s * $fx - $r), [single]($s * 0.76 - $r), [single](2 * $r), [single](2 * $r)) }
  }
  $g.Dispose(); $bmp
}

$pngs = foreach ($s in $sizes) { $b = New-Frame $s; $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , @{ size = $s; data = $ms.ToArray() } }
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($p in $pngs) {
  $dim = if ($p.size -ge 256) { 0 } else { $p.size }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$p.data.Length); $w.Write([uint32]$offset)
  $offset += $p.data.Length
}
foreach ($p in $pngs) { $w.Write($p.data) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $root 'app.ico'), $out.ToArray())
"app.ico written: $((Get-Item (Join-Path $root 'app.ico')).Length) bytes, $($pngs.Count) sizes"
# also a 256px PNG preview for a quick look
$prev = New-Frame 256; $prev.Save((Join-Path $root 'tools\app-icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png); $prev.Dispose()
