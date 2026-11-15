# Draws the Saturn V stencil markings as white-on-transparent PNG masks:
# upright letters stacked vertically, as painted on the vehicle ("USA" on the S-IC, "UNITED STATES" on the S-II).
# Usage: powershell -NoProfile -File Tools\make_markings.ps1 -OutDir <folder>
param([string]$OutDir = "Assets\Apollo\Textures\Markings")

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Make-Vertical([string[]]$lines, [string]$file) {
    $cap = 400                      # letter height in pixels
    $pitch = [int]($cap * 1.32)     # letter-to-letter spacing (centre to centre)
    $w = [int]($cap * 1.15)
    $h = $pitch * $lines.Count
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = 'AntiAlias'
    $family = New-Object System.Drawing.FontFamily 'Arial'
    $brush = [System.Drawing.Brushes]::White
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($t)) { continue }
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddString($t, $family, [int][System.Drawing.FontStyle]::Bold, 1000, (New-Object System.Drawing.Point 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
        $b = $path.GetBounds()
        # scale so the glyph's ink height equals the cap height, then centre it in its cell
        $s = $cap / 716.0               # Arial cap height is ~0.716 em
        $m = New-Object System.Drawing.Drawing2D.Matrix
        $cx = $w / 2.0; $cy = $pitch * ($i + 0.5)
        $m.Translate([float]($cx - ($b.X + $b.Width / 2) * $s), [float]($cy - ($b.Y + $b.Height / 2) * $s))
        $m.Scale([float]$s, [float]$s)
        $path.Transform($m)
        $g.FillPath($brush, $path)
        $path.Dispose()
    }
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $file), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    "wrote $file ($w x $h)"
}

Make-Vertical @('U','S','A') 'Marking_USA.png'
Make-Vertical @('U','N','I','T','E','D','','S','T','A','T','E','S') 'Marking_UNITED_STATES.png'
