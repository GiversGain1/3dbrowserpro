# 3D Browser Pro — gerador dos ícones (PowerShell + GDI+), sem dependências externas.
# Uso:  powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-icons.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function New-Pts([object[]]$pts) {
    $arr = New-Object 'System.Drawing.PointF[]' ($pts.Count)
    for ($i = 0; $i -lt $pts.Count; $i++) {
        $arr[$i] = [System.Drawing.PointF]::new([single]$pts[$i][0], [single]$pts[$i][1])
    }
    return , $arr
}

function New-IconBitmap([int]$size) {
    $ss = 4
    $W = [int]($size * $ss)
    $bmp = [System.Drawing.Bitmap]::new($W, $W, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # --- fundo: quadrado arredondado com gradiente violeta ---
    $corner = $W * 0.234
    $d = 2 * $corner
    $clip = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $clip.AddArc([single]0, [single]0, [single]$d, [single]$d, 180, 90)
    $clip.AddArc([single]($W - $d), [single]0, [single]$d, [single]$d, 270, 90)
    $clip.AddArc([single]($W - $d), [single]($W - $d), [single]$d, [single]$d, 0, 90)
    $clip.AddArc([single]0, [single]($W - $d), [single]$d, [single]$d, 90, 90)
    $clip.CloseFigure()

    $rect = [System.Drawing.RectangleF]::new(0, 0, [single]$W, [single]$W)
    $grad = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 0x8b, 0x3f, 0xf8),
        [System.Drawing.Color]::FromArgb(255, 0x14, 0x14, 0x3f),
        [single]45)
    $g.FillPath($grad, $clip)
    $grad.Dispose()

    $g.SetClip($clip)

    # --- brilho ciano (anéis concêntricos = queda radial suave) ---
    $gx = $W * 0.5
    $gy = $W * 0.42
    $gr = $W * 0.62
    $steps = 30
    $glow = [System.Drawing.Color]::FromArgb(5, 0x22, 0xd3, 0xee)
    for ($i = $steps; $i -ge 1; $i--) {
        $rr = $gr * ($i / $steps)
        $brush = [System.Drawing.SolidBrush]::new($glow)
        $g.FillEllipse($brush,
            [single]($gx - $rr), [single]($gy - $rr),
            [single](2 * $rr), [single](2 * $rr))
        $brush.Dispose()
    }

    # --- cubo isométrico ---
    $cx = $W * 0.5
    $cy = $W * 0.5
    $rad = $W * 0.344
    $h = $rad * 0.866

    $A = @($cx, ($cy - $rad))
    $B = @(($cx + $h), ($cy - $rad * 0.5))
    $C = @(($cx + $h), ($cy + $rad * 0.5))
    $D = @($cx, ($cy + $rad))
    $E = @(($cx - $h), ($cy + $rad * 0.5))
    $Fp = @(($cx - $h), ($cy - $rad * 0.5))
    $O = @($cx, $cy)

    $topBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0xc9, 0xf9, 0xff))
    $leftBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0x22, 0xd3, 0xee))
    $rightBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 0x60, 0x5c, 0xf5))

    $g.FillPolygon($topBrush, (New-Pts @($A, $B, $O, $Fp)))
    $g.FillPolygon($leftBrush, (New-Pts @($Fp, $O, $D, $E)))
    $g.FillPolygon($rightBrush, (New-Pts @($B, $C, $D, $O)))

    $topBrush.Dispose()
    $leftBrush.Dispose()
    $rightBrush.Dispose()

    # --- arestas ---
    $edgeWidth = [single]([Math]::Max(1.6, $W * 0.026))
    $edgePen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 0xe8, 0xfb, 0xff), $edgeWidth)
    $edgePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $edgePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $edgePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $segments = @(
        @($A, $B), @($B, $C), @($C, $D), @($D, $E), @($E, $Fp), @($Fp, $A),
        @($O, $B), @($O, $Fp), @($O, $D)
    )
    foreach ($seg in $segments) {
        $p1 = [System.Drawing.PointF]::new([single]$seg[0][0], [single]$seg[0][1])
        $p2 = [System.Drawing.PointF]::new([single]$seg[1][0], [single]$seg[1][1])
        $g.DrawLine($edgePen, $p1, $p2)
    }
    $edgePen.Dispose()

    # --- seta de download na face direita ---
    $ax = $cx + $rad * 0.42
    $ay = $cy + $rad * 0.08
    $sh = $rad * 0.42
    $arrowPen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [single]([Math]::Max(2, $W * 0.045)))
    $arrowPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arrowPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($arrowPen,
        [System.Drawing.PointF]::new([single]$ax, [single]($ay - $sh * 0.55)),
        [System.Drawing.PointF]::new([single]$ax, [single]($ay + $sh * 0.35)))
    $arrowPen.Dispose()

    $whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $g.FillPolygon($whiteBrush, (New-Pts @(
                @(($ax - $rad * 0.19), ($ay + $sh * 0.22)),
                @(($ax + $rad * 0.19), ($ay + $sh * 0.22)),
                @($ax, ($ay + $sh * 0.72)))))
    $whiteBrush.Dispose()

    $g.ResetClip()
    $g.Dispose()

    # --- reduz com alta qualidade preservando alfa ---
    $small = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $dest = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
    $src = [System.Drawing.Rectangle]::new(0, 0, $W, $W)
    $g2.DrawImage($bmp, $dest, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $g2.Dispose()
    $bmp.Dispose()

    return $small
}

foreach ($size in @(16, 32, 48, 128, 256)) {
    $bitmap = New-IconBitmap $size
    $file = Join-Path $outDir ("icon{0}.png" -f $size)
    $bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $info = Get-Item $file
    Write-Host ("ok  assets/icon{0}.png  {1}x{1}  {2} bytes" -f $size, $size, $info.Length)
}
