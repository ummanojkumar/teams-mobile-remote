# Generates default branded MSIX tile, logo, and splash screen assets
Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $PSScriptRoot "Assets"
if (-not (Test-Path $assetsDir)) {
    New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
}

function Create-Logo([int]$width, [int]$height, [string]$filename, [string]$text) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Background gradient in Teams purple shades
    $rect = New-Object System.Drawing.Rectangle(0, 0, $width, $height)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(70, 78, 184), [System.Drawing.Color]::FromArgb(35, 38, 102), 45.0)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()

    # Subtle inner border
    $margin = [Math]::Max(2, [int]($width * 0.05))
    $innerRect = New-Object System.Drawing.Rectangle($margin, $margin, ($width - (2 * $margin)), ($height - (2 * $margin)))
    $penColor = [System.Drawing.Color]::FromArgb(80, 255, 255, 255)
    $innerPen = New-Object System.Drawing.Pen($penColor, [Math]::Max(1, [int]($width * 0.015)))
    $g.DrawRectangle($innerPen, $innerRect)
    $innerPen.Dispose()

    # Center label
    $fontSize = [Math]::Max(7, [int]($height * 0.22))
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString($text, $font, $textBrush, [System.Drawing.RectangleF]::FromLTRB(0, 0, $width, $height), $sf)

    $textBrush.Dispose()
    $font.Dispose()
    $sf.Dispose()
    $g.Dispose()

    $outPath = Join-Path $assetsDir $filename
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Generated $filename ($width x $height)"
}

Create-Logo 44 44 "Square44x44Logo.png" "TR"
Create-Logo 150 150 "Square150x150Logo.png" "Teams Remote"
Create-Logo 310 150 "Wide310x150Logo.png" "Teams Mobile Remote"
Create-Logo 310 310 "Square310x310Logo.png" "Teams Mobile Remote"
Create-Logo 50 50 "StoreLogo.png" "TR"
Create-Logo 620 300 "SplashScreen.png" "Teams Mobile Remote"
