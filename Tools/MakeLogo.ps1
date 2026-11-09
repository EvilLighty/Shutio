# Crop the Shutio emblem (tight bounding box, no wordmark) from I:\shutio.png
# and place it CENTERED on a 1024x1024 black canvas (contain, aspect preserved).
# Output: Assets\logo.png
Add-Type -AssemblyName System.Drawing
$src = 'I:\shutio.png'
$out = 'I:\Apps\SnippingTool\Assets\logo.png'

$img = [System.Drawing.Image]::FromFile((Resolve-Path $src))
$bmp = New-Object System.Drawing.Bitmap($img)
$W = $bmp.Width; $H = $bmp.Height
$threshold = 90
function Ink($x, $y) { $p = $bmp.GetPixel($x, $y); return ($p.R -gt $threshold -or $p.G -gt $threshold -or $p.B -gt $threshold) }

# Row ink counts (sample every 2px for speed)
[int[]]$rowInk = New-Object int[] $H
for ($y = 0; $y -lt $H; $y++) { $c = 0; for ($x = 0; $x -lt $W; $x += 2) { if (Ink $x $y) { $c++ } }; $rowInk[$y] = $c }

# Topmost filled band = the emblem (the wordmark is the band below it)
$minInk = 6; $inBand = $false; $start = 0; $ey0 = 0; $ey1 = 0
for ($y = 0; $y -lt $H; $y++) {
    if ($rowInk[$y] -gt $minInk -and -not $inBand) { $inBand = $true; $start = $y }
    if ($rowInk[$y] -le $minInk -and $inBand) { $inBand = $false; $ey0 = $start; $ey1 = $y - 1; break }
}

# x-extent within the emblem band
$xmin = $W; $xmax = 0
for ($y = $ey0; $y -le $ey1; $y++) {
    for ($x = 0; $x -lt $W; $x += 2) {
        if (Ink $x $y) { if ($x -lt $xmin) { $xmin = $x }; if ($x -gt $xmax) { $xmax = $x } }
    }
}
$bw = $xmax - $xmin + 1
$bh = $ey1 - $ey0 + 1
Write-Host ("emblem tight bbox: x {0} y {1} w {2} h {3} center=({4},{5})" -f $xmin, $ey0, $bw, $bh, (($xmin+$xmax)/2), (($ey0+$ey1)/2))

# Add a little breathing-room padding around the emblem (still well clear of the wordmark below)
$pad = [int]([Math]::Max($bw, $bh) * 0.08)
$cx0 = [Math]::Max(0, $xmin - $pad)
$cy0 = [Math]::Max(0, $ey0 - $pad)
$cx1 = [Math]::Min($W - 1, $xmax + $pad)
$cy1 = [Math]::Min($H - 1, $ey1 + $pad)
$rw = $cx1 - $cx0 + 1
$rh = $cy1 - $cy0 + 1
Write-Host ("crop rect: x {0} y {1} w {2} h {3} (bottom {4})" -f $cx0, $cy0, $rw, $rh, $cy1)

$rect = New-Object System.Drawing.Rectangle($cx0, $cy0, $rw, $rh)
$crop = $bmp.Clone($rect, $bmp.PixelFormat)
$bmp.Dispose(); $img.Dispose()

# Target: 1024x1024 black canvas. Fit the emblem CONTAINED (aspect preserved) and CENTER it.
$target = 1024
$canvas = New-Object System.Drawing.Bitmap($target, $target, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::Black)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

$scale = [Math]::Min($target / [double]$rw, $target / [double]$rh)
$dw = [int]($rw * $scale); $dh = [int]($rh * $scale)
$dx = [int](($target - $dw) / 2); $dy = [int](($target - $dh) / 2)
Write-Host ("draw rect on 1024 canvas: x {0} y {1} w {2} h {3}  (centered)" -f $dx, $dy, $dw, $dh)
$g.DrawImage($crop, $dx, $dy, $dw, $dh)
$g.Dispose(); $crop.Dispose()

$canvas.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Dispose()
Write-Host ('Saved logo.png ' + [math]::Round((Get-Item $out).Length/1KB, 1) + ' KB (1024x1024, emblem centered)')
