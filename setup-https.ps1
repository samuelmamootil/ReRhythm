# Generate self-signed certificate and upload to ACM
param(
    [Parameter(Mandatory=$false)]
    [string]$Region = "us-east-1"
)

Write-Host "Generating self-signed certificate..." -ForegroundColor Yellow

# Use OpenSSL (comes with Git for Windows)
$openssl = "C:\Program Files\Git\usr\bin\openssl.exe"
if (-not (Test-Path $openssl)) {
    $openssl = "openssl" # Try system PATH
}

# Generate private key and certificate
& $openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem -days 365 -subj "/CN=*.elb.amazonaws.com" 2>$null

if (-not (Test-Path cert.pem) -or -not (Test-Path key.pem)) {
    Write-Host "Failed to generate certificate. Install OpenSSL or Git for Windows" -ForegroundColor Red
    exit 1
}

Write-Host "Certificate generated" -ForegroundColor Green

# Upload to ACM
Write-Host "Uploading to ACM..." -ForegroundColor Yellow
$certArn = aws acm import-certificate `
    --certificate fileb://cert.pem `
    --private-key fileb://key.pem `
    --region $Region `
    --query CertificateArn `
    --output text

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to upload certificate" -ForegroundColor Red
    Remove-Item cert.pem, key.pem -ErrorAction SilentlyContinue
    exit 1
}

Write-Host "Certificate uploaded: $certArn" -ForegroundColor Green

# Clean up
Remove-Item cert.pem, key.pem -ErrorAction SilentlyContinue

Write-Host "`nHTTPS enabled! Run .\deploy.ps1 to update your stack" -ForegroundColor Cyan
