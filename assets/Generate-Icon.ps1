$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$frames=@()
foreach($size in @(16,20,24,32,48,64,256)) {
    $bitmap=New-Object Drawing.Bitmap($size,$size)
    $g=[Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)
    $g.ScaleTransform($size/32.0,$size/32.0)
    $path=New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(1,1,10,10,180,90);$path.AddArc(21,1,10,10,270,90)
    $path.AddArc(21,21,10,10,0,90);$path.AddArc(1,21,10,10,90,90);$path.CloseFigure()
    $background=New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255,24,31,43))
    $g.FillPath($background,$path)
    foreach($arrow in @(@{X=10;Start=8;End=24;Tip=19;Color='#70E3CA'},@{X=22;Start=24;End=8;Tip=13;Color='#B7A1FF'})) {
        $pen=New-Object Drawing.Pen([Drawing.ColorTranslator]::FromHtml($arrow.Color),3)
        $pen.StartCap=[Drawing.Drawing2D.LineCap]::Round;$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round;$pen.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
        $g.DrawLine($pen,[single]$arrow.X,[single]$arrow.Start,[single]$arrow.X,[single]$arrow.End)
        $g.DrawLines($pen,[Drawing.PointF[]]@((New-Object Drawing.PointF(($arrow.X-4),$arrow.Tip)),(New-Object Drawing.PointF($arrow.X,$arrow.End)),(New-Object Drawing.PointF(($arrow.X+4),$arrow.Tip))))
        $pen.Dispose()
    }
    $g.Dispose();$path.Dispose();$background.Dispose()
    $stream=New-Object IO.MemoryStream;$bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
    $frames+=@{Size=$size;Bytes=$stream.ToArray()};$stream.Dispose()
    if($size -eq 256){$bitmap.Save((Join-Path $PSScriptRoot 'icon-preview.png'),[Drawing.Imaging.ImageFormat]::Png)}
    $bitmap.Dispose()
}
$writer=New-Object IO.BinaryWriter([IO.File]::Create((Join-Path $PSScriptRoot 'traffic.ico')))
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset=6+16*$frames.Count
    foreach($frame in $frames){$edge=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$edge);$writer.Write([byte]$edge);$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length}
    foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
}finally{$writer.Dispose()}
