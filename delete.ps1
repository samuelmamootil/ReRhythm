param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "rerythm-prod",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "us-east-1"
)

Write-Host "ReRhythm Stack Deletion" -ForegroundColor Red
Write-Host "======================" -ForegroundColor Red

# Get AWS Account ID
$AccountId = aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to get AWS Account ID" -ForegroundColor Red
    exit 1
}

# Get S3 bucket name from stack
Write-Host "`nGetting S3 bucket name..." -ForegroundColor Yellow
$bucketName = aws cloudformation describe-stacks --stack-name "$Environment-stack" --region $Region --query "Stacks[0].Outputs[?OutputKey=='ResumeUploadBucketName'].OutputValue" --output text 2>$null

if ($bucketName) {
    Write-Host "Found S3 bucket: $bucketName" -ForegroundColor Green
    Write-Host "Emptying S3 bucket..." -ForegroundColor Yellow
    aws s3 rm "s3://$bucketName" --recursive --region $Region
    Write-Host "S3 bucket emptied" -ForegroundColor Green
}

# Delete CloudFormation stack
Write-Host "`nDeleting CloudFormation stack..." -ForegroundColor Yellow
aws cloudformation delete-stack --stack-name "$Environment-stack" --region $Region

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to initiate stack deletion" -ForegroundColor Red
    exit 1
}

Write-Host "Waiting for stack deletion (this may take 5-10 minutes)..." -ForegroundColor Yellow
aws cloudformation wait stack-delete-complete --stack-name "$Environment-stack" --region $Region

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nStack deleted successfully!" -ForegroundColor Green
} else {
    Write-Host "`nStack deletion failed or timed out" -ForegroundColor Red
    Write-Host "Check AWS Console: https://console.aws.amazon.com/cloudformation" -ForegroundColor Yellow
}
