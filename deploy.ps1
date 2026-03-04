# ReRhythm - Quick Deployment Script for Windows
# Run this script to deploy your app to AWS App Runner

param(
    [Parameter(Mandatory=$false)]
    [string]$Environment = "rerythm-prod",
    
    [Parameter(Mandatory=$false)]
    [string]$Region = "us-east-1"
)

$ErrorActionPreference = "Continue"

Write-Host "ReRhythm Deployment Script" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan

# Get AWS Account ID
Write-Host "`nGetting AWS Account ID..." -ForegroundColor Yellow
$AccountId = aws sts get-caller-identity --query Account --output text
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to get AWS Account ID. Is AWS CLI configured?" -ForegroundColor Red
    exit 1
}
Write-Host "Account ID: $AccountId" -ForegroundColor Green

# Variables
$EcrRepoName = "rerythm"
$EcrUri = "$AccountId.dkr.ecr.$Region.amazonaws.com/$EcrRepoName"
$ImageTag = "latest"

Write-Host "`nDomain Configuration:" -ForegroundColor Yellow
Write-Host "   Using App Runner default domain (no custom DNS needed)" -ForegroundColor Cyan
Write-Host "   Your app will be accessible via App Runner URL" -ForegroundColor Cyan

# Step 1: Create ECR Repository (if not exists)
Write-Host "`nCreating ECR Repository..." -ForegroundColor Yellow
$ErrorActionPreference = "SilentlyContinue"
$existingRepo = aws ecr describe-repositories --repository-names $EcrRepoName --region $Region 2>&1
$ErrorActionPreference = "Continue"
if ($LASTEXITCODE -ne 0) {
    aws ecr create-repository --repository-name $EcrRepoName --region $Region --encryption-configuration encryptionType=KMS --image-scanning-configuration scanOnPush=true
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to create ECR repository" -ForegroundColor Red
        exit 1
    }
    Write-Host "ECR Repository created" -ForegroundColor Green
} else {
    Write-Host "ECR Repository already exists" -ForegroundColor Green
}

# Step 2: Docker Login
Write-Host "`nLogging into ECR..." -ForegroundColor Yellow
aws ecr get-login-password --region $Region | docker login --username AWS --password-stdin $EcrUri
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker login failed" -ForegroundColor Red
    exit 1
}
Write-Host "Docker login successful" -ForegroundColor Green

# Step 3: Build Docker Image
Write-Host "`nBuilding Docker image..." -ForegroundColor Yellow
docker build --no-cache -t ${EcrRepoName}:${ImageTag} .
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker build failed" -ForegroundColor Red
    exit 1
}
Write-Host "Docker image built" -ForegroundColor Green

# Step 4: Tag and Push
Write-Host "`nPushing image to ECR..." -ForegroundColor Yellow
docker tag ${EcrRepoName}:${ImageTag} ${EcrUri}:${ImageTag}
docker push ${EcrUri}:${ImageTag}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker push failed" -ForegroundColor Red
    exit 1
}
Write-Host "Image pushed to ECR" -ForegroundColor Green

# Step 5: Deploy CloudFormation Stack
Write-Host "`nDeploying CloudFormation stack..." -ForegroundColor Yellow

# Auto-create HTTPS certificate if missing
$ErrorActionPreference = "SilentlyContinue"
$certArn = aws acm list-certificates --region $Region --query "CertificateSummaryList[?contains(DomainName, 'elb.amazonaws.com')].CertificateArn | [0]" --output text 2>&1
$ErrorActionPreference = "Continue"

if (-not $certArn -or $certArn -eq "None") {
    Write-Host "No certificate found. Creating self-signed certificate..." -ForegroundColor Yellow
    
    # Use OpenSSL (comes with Git for Windows)
    $openssl = "C:\Program Files\Git\usr\bin\openssl.exe"
    if (-not (Test-Path $openssl)) { $openssl = "openssl" }
    
    & $openssl req -x509 -newkey rsa:2048 -nodes -keyout key.pem -out cert.pem -days 365 -subj "/CN=*.elb.amazonaws.com" 2>$null
    
    if (Test-Path cert.pem) {
        $certArn = aws acm import-certificate --certificate fileb://cert.pem --private-key fileb://key.pem --region $Region --query CertificateArn --output text
        Remove-Item cert.pem, key.pem -ErrorAction SilentlyContinue
        Write-Host "Certificate created: $certArn" -ForegroundColor Green
    } else {
        Write-Host "Failed to create certificate. Deploying without HTTPS" -ForegroundColor Yellow
        $certArn = ""
    }
} else {
    Write-Host "Found existing certificate: $certArn" -ForegroundColor Green
}

# Upload template to S3 bucket
$s3Bucket = "cf-templates-$AccountId-$Region"
$s3Key = "rerythm-template-$(Get-Date -Format 'yyyyMMddHHmmss').yaml"

# Create S3 bucket if it doesn't exist
$ErrorActionPreference = "SilentlyContinue"
aws s3 mb "s3://$s3Bucket" --region $Region 2>&1 | Out-Null
$ErrorActionPreference = "Continue"

# Upload template
aws s3 cp cloudformation-ecs-fargate.yaml "s3://$s3Bucket/$s3Key" --region $Region
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to upload template to S3" -ForegroundColor Red
    exit 1
}

$templateUrl = "https://s3.$Region.amazonaws.com/$s3Bucket/$s3Key"

$params = @(
    "ParameterKey=EnvironmentName,ParameterValue=$Environment",
    "ParameterKey=ECRImageUri,ParameterValue=${EcrUri}:${ImageTag}",
    "ParameterKey=BedrockModelId,ParameterValue=us.anthropic.claude-sonnet-4-5-20250929-v1:0",
    "ParameterKey=CertificateArn,ParameterValue=$certArn"
)

# Check if stack exists
$ErrorActionPreference = "SilentlyContinue"
$stackExists = aws cloudformation describe-stacks --stack-name "$Environment-stack" --region $Region 2>&1
$ErrorActionPreference = "Continue"
if ($LASTEXITCODE -eq 0) {
    Write-Host "Stack exists, updating..." -ForegroundColor Yellow
    aws cloudformation update-stack `
        --stack-name "$Environment-stack" `
        --template-url $templateUrl `
        --parameters $params `
        --capabilities CAPABILITY_NAMED_IAM `
        --region $Region
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Waiting for stack update to complete..." -ForegroundColor Yellow
        aws cloudformation wait stack-update-complete --stack-name "$Environment-stack" --region $Region
    } else {
        Write-Host "No updates to perform" -ForegroundColor Cyan
    }
} else {
    Write-Host "Creating new stack..." -ForegroundColor Yellow
    aws cloudformation create-stack `
        --stack-name "$Environment-stack" `
        --template-url $templateUrl `
        --parameters $params `
        --capabilities CAPABILITY_NAMED_IAM `
        --region $Region
    
    Write-Host "Waiting for stack creation to complete (this may take 10-15 minutes)..." -ForegroundColor Yellow
    aws cloudformation wait stack-create-complete --stack-name "$Environment-stack" --region $Region
}

if ($LASTEXITCODE -ne 0) {
    Write-Host "CloudFormation deployment failed" -ForegroundColor Red
    Write-Host "Check AWS Console for details: https://console.aws.amazon.com/cloudformation" -ForegroundColor Yellow
    exit 1
}

Write-Host "CloudFormation stack deployed" -ForegroundColor Green

# Step 6: Verify SES Email for Forum Notifications
Write-Host "`nVerifying SES email for forum notifications..." -ForegroundColor Yellow
$sesEmail = "mamootilsamuel1@gmail.com"
$ErrorActionPreference = "SilentlyContinue"
$emailVerified = aws ses get-identity-verification-attributes --identities $sesEmail --region $Region --query "VerificationAttributes.'$sesEmail'.VerificationStatus" --output text 2>&1
$ErrorActionPreference = "Continue"

if ($emailVerified -ne "Success") {
    Write-Host "Sending verification email to $sesEmail..." -ForegroundColor Yellow
    aws ses verify-email-identity --email-address $sesEmail --region $Region 2>&1 | Out-Null
    Write-Host "Verification email sent to $sesEmail" -ForegroundColor Cyan
    Write-Host "   Please check your inbox and click the verification link" -ForegroundColor White
} else {
    Write-Host "Email $sesEmail already verified" -ForegroundColor Green
}

# Step 7: Get Outputs
Write-Host "`nDeployment Complete!" -ForegroundColor Green
Write-Host "================================" -ForegroundColor Green

$serviceUrl = aws cloudformation describe-stacks `
    --stack-name "$Environment-stack" `
    --query "Stacks[0].Outputs[?OutputKey=='LoadBalancerURL'].OutputValue" `
    --output text `
    --region $Region

Write-Host "`nApplication URL:" -ForegroundColor Cyan
Write-Host "   $serviceUrl" -ForegroundColor White
Write-Host "`n   Your app is ready to use!" -ForegroundColor Green

Write-Host "`nNext Steps:" -ForegroundColor Cyan
Write-Host "   1. Visit the URL above to test your application" -ForegroundColor White
Write-Host "   2. Upload a test resume to warm up Bedrock" -ForegroundColor White
Write-Host "   3. Check CloudWatch logs: aws logs tail /ecs/$Environment --follow" -ForegroundColor White

Write-Host "`nCost Monitoring:" -ForegroundColor Cyan
Write-Host "   View costs: https://console.aws.amazon.com/cost-management/home" -ForegroundColor White

Write-Host "`nDeployment successful! Your app is live!" -ForegroundColor Green
