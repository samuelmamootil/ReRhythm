#!/bin/bash
# ReRhythm - AWS CLI Deployment Script

set -e

ENVIRONMENT="rerythm-prod"
REGION="us-east-1"
ECR_REPO_NAME="rerythm"
IMAGE_TAG="latest"

echo "================================"
echo "ReRhythm Deployment Script"
echo "================================"

# Get AWS Account ID
echo ""
echo "Getting AWS Account ID..."
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
echo "Account ID: $ACCOUNT_ID"

ECR_URI="$ACCOUNT_ID.dkr.ecr.$REGION.amazonaws.com/$ECR_REPO_NAME"

# Step 1: Create ECR Repository
echo ""
echo "Creating ECR Repository..."
aws ecr describe-repositories --repository-names $ECR_REPO_NAME --region $REGION 2>/dev/null || \
aws ecr create-repository \
  --repository-name $ECR_REPO_NAME \
  --region $REGION \
  --encryption-configuration encryptionType=KMS \
  --image-scanning-configuration scanOnPush=true

echo "ECR Repository ready"

# Step 2: Docker Login
echo ""
echo "Logging into ECR..."
aws ecr get-login-password --region $REGION | docker login --username AWS --password-stdin $ECR_URI
echo "Docker login successful"

# Step 3: Build Docker Image
echo ""
echo "Building Docker image..."
docker build -t ${ECR_REPO_NAME}:${IMAGE_TAG} .
echo "Docker image built"

# Step 4: Tag and Push
echo ""
echo "Pushing image to ECR..."
docker tag ${ECR_REPO_NAME}:${IMAGE_TAG} ${ECR_URI}:${IMAGE_TAG}
docker push ${ECR_URI}:${IMAGE_TAG}
echo "Image pushed to ECR"

# Step 5: Deploy CloudFormation Stack
echo ""
echo "Deploying CloudFormation stack..."

# Upload template to S3
S3_BUCKET="cf-templates-$ACCOUNT_ID-$REGION"
S3_KEY="rerythm-template-$(date +%Y%m%d%H%M%S).yaml"

# Create S3 bucket if it doesn't exist
aws s3 mb "s3://$S3_BUCKET" --region $REGION 2>/dev/null || true

# Upload template
aws s3 cp cloudformation-apprunner-secure.yaml "s3://$S3_BUCKET/$S3_KEY" --region $REGION

TEMPLATE_URL="https://s3.$REGION.amazonaws.com/$S3_BUCKET/$S3_KEY"

# Check if stack exists
if aws cloudformation describe-stacks --stack-name "$ENVIRONMENT-stack" --region $REGION 2>/dev/null; then
    echo "Stack exists, updating..."
    aws cloudformation update-stack \
        --stack-name "$ENVIRONMENT-stack" \
        --template-url $TEMPLATE_URL \
        --parameters \
            ParameterKey=EnvironmentName,ParameterValue=$ENVIRONMENT \
            ParameterKey=ECRImageUri,ParameterValue=${ECR_URI}:${IMAGE_TAG} \
            ParameterKey=BedrockModelId,ParameterValue=us.anthropic.claude-sonnet-4-5-20250929-v1:0 \
        --capabilities CAPABILITY_NAMED_IAM \
        --region $REGION 2>/dev/null || echo "No updates needed"
    
    echo "Waiting for stack update..."
    aws cloudformation wait stack-update-complete --stack-name "$ENVIRONMENT-stack" --region $REGION 2>/dev/null || true
else
    echo "Creating new stack..."
    aws cloudformation create-stack \
        --stack-name "$ENVIRONMENT-stack" \
        --template-url $TEMPLATE_URL \
        --parameters \
            ParameterKey=EnvironmentName,ParameterValue=$ENVIRONMENT \
            ParameterKey=ECRImageUri,ParameterValue=${ECR_URI}:${IMAGE_TAG} \
            ParameterKey=BedrockModelId,ParameterValue=us.anthropic.claude-sonnet-4-5-20250929-v1:0 \
        --capabilities CAPABILITY_NAMED_IAM \
        --region $REGION
    
    echo "Waiting for stack creation (10-15 minutes)..."
    aws cloudformation wait stack-create-complete --stack-name "$ENVIRONMENT-stack" --region $REGION
fi

echo "CloudFormation stack deployed"

# Step 6: Get Outputs
echo ""
echo "================================"
echo "Deployment Complete!"
echo "================================"

SERVICE_URL=$(aws cloudformation describe-stacks \
    --stack-name "$ENVIRONMENT-stack" \
    --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceUrl'].OutputValue" \
    --output text \
    --region $REGION)

echo ""
echo "Application URL:"
echo "   $SERVICE_URL"
echo ""
echo "Your app is ready to use!"
echo ""
echo "Next Steps:"
echo "   1. Visit the URL above to test your application"
echo "   2. Upload a test resume to warm up Bedrock"
echo "   3. Check CloudWatch logs: aws logs tail /aws/apprunner/rerythm --follow"
echo ""
echo "Deployment successful!"
