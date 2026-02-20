$pdfPath = "c:\Sam\2026\ReRhythm\ReRhythm.Web\Resume-Samuel-Mamootil-NVIDIA.pdf"
$bytes = [System.IO.File]::ReadAllBytes($pdfPath)
Write-Host "PDF Size: $($bytes.Length) bytes"
Write-Host "First 10 bytes: $($bytes[0..9] -join ', ')"
