param([string]$cmd, [string]$a, [string]$b, [string]$c, [string]$d)
Add-Type -AssemblyName PresentationCore
switch ($cmd) {
  'dump' {   # png -> raw
    $fs=[System.IO.File]::OpenRead($a)
    $f=[System.Windows.Media.Imaging.BitmapDecoder]::Create($fs,[System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad).Frames[0]
    $conv=[System.Windows.Media.Imaging.FormatConvertedBitmap]::new($f,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
    $stride=$conv.PixelWidth*4; $bytes=New-Object byte[] ($stride*$conv.PixelHeight)
    $conv.CopyPixels($bytes,$stride,0)
    [System.IO.File]::WriteAllBytes($b,$bytes); $fs.Dispose()
    Write-Host ("dumped " + $conv.PixelWidth + "x" + $conv.PixelHeight + " -> " + $b)
  }
  'load' {   # raw w h -> png
    $bytes=[System.IO.File]::ReadAllBytes($a)
    $img=[System.Windows.Media.Imaging.BitmapSource]::Create([int]$b,[int]$c,96,96,[System.Windows.Media.PixelFormats]::Bgra32,$null,$bytes,([int]$b*4))
    $enc=New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($img))
    $out=[System.IO.File]::Create($d); $enc.Save($out); $out.Dispose()
    Write-Host ("loaded " + $b + "x" + $c + " -> " + $d)
  }
  'md5' {
    Write-Host ((Get-FileHash $a -Algorithm MD5).Hash)
  }
  'cmp' {   # raw raw label
    $x=[System.IO.File]::ReadAllBytes($a); $y=[System.IO.File]::ReadAllBytes($b)
    if ($x.Length -ne $y.Length) { Write-Host ("FAIL length " + $x.Length + " vs " + $y.Length); exit 1 }
    $diff=0; for ($i=0; $i -lt $x.Length; $i++) { if ($x[$i] -ne $y[$i]) { $diff++ } }
    if ($diff -eq 0) { Write-Host ("PASS identical " + $x.Length + " bytes") } else { Write-Host ("FAIL " + $diff + " byte diffs"); exit 1 }
  }
}
