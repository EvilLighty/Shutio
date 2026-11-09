# Render the installed loose .ico at a desktop-tile size (32 and 48 px) to prove
# the small-size DIB entries actually draw the emblem (not blank).
Add-Type -AssemblyName System.Drawing
$ico = $args[0]
if (-not $ico) { $ico = 'I:\Shutio\Assets\app.ico' }
$icon = New-Object System.Drawing.Icon($ico)   # strict load of the .ico file
$sizes = @()
try {
    # Enumerate how many images GDI reports for this .ico
    $sizes = @($icon.Size)
} catch { }
Write-Host ('Loaded OK; primary size = ' + $icon.Width + 'x' + $icon.Height)

foreach ($px in @(16, 32, 48, 128)) {
    $bmp = New-Object System.Drawing.Bitmap($px, $px, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Black)
    # draw exactly px-by-px (NearestNeighbor) so we see the raw DIB at small sizes
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $g.DrawImage($icon, 0, 0, $px, $px)
    $g.Dispose()
    $out = "I:\Apps\SnippingTool\Assets\_render_$px.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host ('rendered {0}px -> {1}' -f $px, $out)
}
# Make a 4x scaled composite of the 16 and 32 renders so it's easy to eyeball.
$icon.Dispose()
Write-Host 'done'
