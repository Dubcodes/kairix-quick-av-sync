[CmdletBinding()]
param([string] $OutputPath = (Join-Path $PSScriptRoot '..\src\Kairix.QuickAVSync\Assets\Kairix.QuickAVSync.ico'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-OffsetTimelineBitmap([int] $size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size); $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias; $graphics.Clear([System.Drawing.Color]::FromArgb(16, 25, 29)); $s = $size / 256.0
        $teal = [System.Drawing.Color]::FromArgb(80, 215, 192); $white = [System.Drawing.Color]::FromArgb(245, 251, 252); $coral = [System.Drawing.Color]::FromArgb(255, 141, 103)
        $wave = [System.Drawing.Pen]::new($teal, [Math]::Max(1, 16*$s)); $wave.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round; $wave.StartCap = $wave.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $line = [System.Drawing.Pen]::new($white, [Math]::Max(1, 12*$s)); $line.StartCap = $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round; $frame = [System.Drawing.Pen]::new($teal, [Math]::Max(1, 14*$s)); $frame.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $whiteBrush = [System.Drawing.SolidBrush]::new($white); $coralBrush = [System.Drawing.SolidBrush]::new($coral)
        try {
            [System.Drawing.PointF[]]$points = @([System.Drawing.PointF]::new(34*$s,84*$s),[System.Drawing.PointF]::new(66*$s,84*$s),[System.Drawing.PointF]::new(78*$s,60*$s),[System.Drawing.PointF]::new(96*$s,108*$s),[System.Drawing.PointF]::new(116*$s,43*$s),[System.Drawing.PointF]::new(134*$s,84*$s),[System.Drawing.PointF]::new(172*$s,84*$s))
            $graphics.DrawLines($wave, $points); $graphics.DrawRectangle($frame, 42*$s,150*$s,114*$s,53*$s); $graphics.DrawLine($frame,80*$s,160*$s,80*$s,193*$s); $graphics.DrawLine($frame,116*$s,160*$s,116*$s,193*$s); $graphics.DrawLine($line,174*$s,49*$s,174*$s,205*$s)
            $r = [Math]::Max(2, 12*$s); $graphics.FillEllipse($whiteBrush,174*$s-$r,84*$s-$r,2*$r,2*$r); $graphics.FillEllipse($coralBrush,174*$s-$r,176*$s-$r,2*$r,2*$r)
        } finally { $wave.Dispose(); $line.Dispose(); $frame.Dispose(); $whiteBrush.Dispose(); $coralBrush.Dispose() }
        return $bitmap
    } finally { $graphics.Dispose() }
}

$resolved = [System.IO.Path]::GetFullPath($OutputPath); [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolved)) | Out-Null; $temporary = [System.IO.Path]::GetTempFileName()
try {
    $sizes = @(16,24,32,48,64,128,256); $images = @()
    foreach ($size in $sizes) { $bitmap = New-OffsetTimelineBitmap $size; $stream = [System.IO.MemoryStream]::new(); try { $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png); $images += ,$stream.ToArray() } finally { $stream.Dispose(); $bitmap.Dispose() } }
    $writer = [System.IO.BinaryWriter]::new([System.IO.File]::Open($temporary, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write))
    try {
        $writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count); $offset = 6 + 16*$sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) { $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }; $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$images[$index].Length); $writer.Write([UInt32]$offset); $offset += $images[$index].Length }
        foreach ($image in $images) { $writer.Write($image) }
    } finally { $writer.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $resolved -Force; Write-Host "Generated $resolved with $($sizes -join ', ') pixel PNG entries."
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
