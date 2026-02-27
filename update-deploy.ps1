#!/usr/bin/env pwsh
# Quick update script - builds and deploys new Docker image without CloudFormation

$ErrorActionPreference = "Stop"

$REGION = "us-east-1"
$ACCOUNT_ID = "250025622388"
$ECR_REPO = "rerythm"
$IMAGE_TAG = "latest"
$CLUSTER = "rerythm-prod-cluster"
$SERVICE = "rerythm-prod-service"

Write-Host "`nLogging into ECR..." -ForegroundColor Cyan
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com"
if ($LASTEXITCODE -ne 0) { throw "ECR login failed" }

Write-Host "`nBuilding Docker image..." -ForegroundColor Cyan
docker build -t "${ECR_REPO}:${IMAGE_TAG}" .
if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }

Write-Host "`nTagging image..." -ForegroundColor Cyan
docker tag "${ECR_REPO}:${IMAGE_TAG}" "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/${ECR_REPO}:${IMAGE_TAG}"

Write-Host "`nPushing to ECR..." -ForegroundColor Cyan
docker push "$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/${ECR_REPO}:${IMAGE_TAG}"
if ($LASTEXITCODE -ne 0) { throw "Docker push failed" }

Write-Host "`nForcing ECS deployment..." -ForegroundColor Cyan
aws ecs update-service --cluster $CLUSTER --service $SERVICE --force-new-deployment --region $REGION | Out-Null
if ($LASTEXITCODE -ne 0) { throw "ECS update failed" }

Write-Host "`nDeployment initiated successfully!" -ForegroundColor Green
Write-Host "Monitor progress: https://console.aws.amazon.com/ecs/v2/clusters/$CLUSTER/services/$SERVICE" -ForegroundColor Yellow
Write-Host "Deployment takes ~3-5 minutes`n" -ForegroundColor Yellow
