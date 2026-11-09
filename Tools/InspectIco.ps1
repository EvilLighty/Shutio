# Inspect an .ico file's entries at the byte level and flag malformed DIBs.
# Usage: InspectIco.ps1 <path-to.ico>   (defaults to the installed copy)
$ico = $args[0]
if (-not $ico) { $ico = 'I:\Shutio\Assets\app.ico' }
if (-not (Test-Path $ico)) { Write-Host "Not found: $ico"; return }
$bytes = [System.IO.File]::ReadAllBytes($ico)

function S16($b,$o){ [BitConverter]::ToInt16($b,$o) }
function I32($b,$o){ [BitConverter]::ToInt32($b,$o) }

$count = S16 $bytes 4
Write-Host ("FILE : {0}  ({1} bytes)" -f $ico, $bytes.Length)
Write-Host ("Entries: {0}" -f $count)
$off = 6
$problems = 0
for ($i = 0; $i -lt $count; $i++) {
    $w = $bytes[$off];      if ($w -eq 0) { $w = 256 }
    $h = $bytes[$off + 1];  if ($h -eq 0) { $h = 256 }
    $bInRes = I32 $bytes ($off + 8)
    $imgOff = I32 $bytes ($off + 12)

    $b0 = $bytes[$imgOff]; $b1 = $bytes[$imgOff + 1]
    if ($b0 -eq 0x89 -and $b1 -eq 0x50) {
        Write-Host ("  {0,3}x{1,-3}  PNG entry   {2,7} bytes @ {3,7}   [OK: PNG image]" -f $w, $h, $bInRes, $imgOff)
    } else {
        $biSize      = I32  $bytes $imgOff
        $biWidth     = I32  $bytes ($imgOff + 4)
        $biHeight    = I32  $bytes ($imgOff + 8)
        $biPlanes    = S16  $bytes ($imgOff + 12)
        $biBitCount  = S16  $bytes ($imgOff + 14)
        $biComp      = I32  $bytes ($imgOff + 16)
        $biSizeImage = I32  $bytes ($imgOff + 20)
        $px        = $w * $h * 4
        $maskRow   = [int]([Math]::Ceiling($w / 32.0)) * 4
        $mask      = $maskRow * $h
        $expected  = 40 + $px + $mask
        $ok = ($biSize -eq 40 -and $biBitCount -eq 32 -and $biComp -eq 0 -and $biSizeImage -eq $px -and $expected -eq $bInRes)
        if (-not $ok) { $problems++ }
        $verdict = if ($ok) { 'OK' } else { '*** MALFORMED ***' }
        Write-Host ("  {0,3}x{1,-3}  DIB   biSize={2} planes={3} bitCount={4} comp={5} sizeImg={6,-6} px={7} mask={8} expected={9} actual={10}  {11}" -f `
            $w, $h, $biSize, $biPlanes, $biBitCount, $biComp, $biSizeImage, $px, $mask, $expected, $bInRes, $verdict)
    }
    $off += 16
}
Write-Host ''
if ($problems -gt 0) {
    Write-Host ("RESULT: {0} malformed DIB entr(ies) found" -f $problems)
} else {
    Write-Host 'RESULT: all entries look valid'
}
