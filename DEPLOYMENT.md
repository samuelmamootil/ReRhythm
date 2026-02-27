# ReRhythm - Secure App Runner Deployment Guide

## 🔒 Security Architecture

### ✅ What's Secured:
- **Frontend**: Public App Runner (HTTPS only)
- **Backend**: Private VPC with VPC Endpoints (NO internet access)
- **Data at Rest**: KMS encryption for S3, DynamoDB, Secrets Manager
- **Data in Transit**: TLS 1.2+ for all connections
- **Secrets**: AWS Secrets Manager (no hardcoded credentials)
- **Access**: IAM least privilege roles
- **Logging**: CloudWatch + S3 access logs

### 🏗️ Architecture Flow:
```
Internet → App Runner (Public) → VPC Connector → Private VPC
                                                    ↓
                                    VPC Endpoints (Private)
                                    ├── S3 (Gateway)
                                    ├── DynamoDB (Gateway)
                                    ├── Bedrock Runtime (Interface)
                                    ├── Textract (Interface)
                                    └── Secrets Manager (Interface)
```

---

## 📋 Prerequisites

1. **AWS CLI** configured with admin credentials
2. **Docker** installed
3. **Route53 Hosted Zone** (optional, for custom domain)
4. **Bedrock Model Access** enabled in your AWS account

---

## 🚀 Deployment Steps

### Step 1: Create ECR Repository

```bash
# Set variables
export AWS_REGION=us-east-1
export AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
export ECR_REPO_NAME=rerythm
export ENVIRONMENT=rerythm-prod

# Create ECR repository
aws ecr create-repository \
  --repository-name $ECR_REPO_NAME \
  --region $AWS_REGION \
  --encryption-configuration encryptionType=KMS \
  --image-scanning-configuration scanOnPush=true

# Get ECR login
aws ecr get-login-password --region $AWS_REGION | \
  docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com
```

### Step 2: Build and Push Docker Image

```bash
# Build Docker image
docker build -t $ECR_REPO_NAME:latest .

# Tag image
docker tag $ECR_REPO_NAME:latest \
  $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME:latest

# Push to ECR
docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME:latest
```

### Step 3: Deploy CloudFormation Stack

#### Option A: Deploy with Custom Domain (Required)

```bash
# Get your Route53 Hosted Zone ID
export HOSTED_ZONE_ID=$(aws route53 list-hosted-zones \
  --query "HostedZones[?Name=='yourdomain.com.'].Id" \
  --output text | cut -d'/' -f3)

aws cloudformation create-stack \
  --stack-name $ENVIRONMENT-stack \
  --template-body file://cloudformation-apprunner-secure.yaml \
  --parameters \
    ParameterKey=EnvironmentName,ParameterValue=$ENVIRONMENT \
    ParameterKey=ECRImageUri,ParameterValue=$AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME:latest \
    ParameterKey=DomainName,ParameterValue=rerythm.yourdomain.com \
    ParameterKey=HostedZoneId,ParameterValue=$HOSTED_ZONE_ID \
    ParameterKey=BedrockModelId,ParameterValue=us.anthropic.claude-sonnet-4-5-20250929-v1:0 \
  --capabilities CAPABILITY_NAMED_IAM \
  --region $AWS_REGION

# Monitor stack creation
aws cloudformation wait stack-create-complete \
  --stack-name $ENVIRONMENT-stack \
  --region $AWS_REGION
```

### Step 4: Get Application URL

```bash
# Get App Runner URL
aws cloudformation describe-stacks \
  --stack-name $ENVIRONMENT-stack \
  --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceUrl'].OutputValue" \
  --output text

# Or with custom domain
aws cloudformation describe-stacks \
  --stack-name $ENVIRONMENT-stack \
  --query "Stacks[0].Outputs[?OutputKey=='CustomDomainUrl'].OutputValue" \
  --output text
```

---

## 🔄 Update Deployment (After Code Changes)

```bash
# Rebuild and push new image
docker build -t $ECR_REPO_NAME:latest .
docker tag $ECR_REPO_NAME:latest \
  $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME:latest
docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_REGION.amazonaws.com/$ECR_REPO_NAME:latest

# Trigger App Runner deployment
aws apprunner start-deployment \
  --service-arn $(aws cloudformation describe-stacks \
    --stack-name $ENVIRONMENT-stack \
    --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceArn'].OutputValue" \
    --output text) \
  --region $AWS_REGION
```

---

## 🗑️ Complete Teardown (Delete Everything)

```bash
# Delete CloudFormation stack (deletes all resources)
aws cloudformation delete-stack \
  --stack-name $ENVIRONMENT-stack \
  --region $AWS_REGION

# Wait for deletion
aws cloudformation wait stack-delete-complete \
  --stack-name $ENVIRONMENT-stack \
  --region $AWS_REGION

# Delete ECR repository
aws ecr delete-repository \
  --repository-name $ECR_REPO_NAME \
  --region $AWS_REGION \
  --force

echo "✅ All resources deleted. No residual costs."
```

---

## 📊 Monitoring & Logs

### View Application Logs
```bash
# Get log group name
LOG_GROUP=$(aws logs describe-log-groups \
  --log-group-name-prefix "/aws/apprunner/$ENVIRONMENT" \
  --query "logGroups[0].logGroupName" \
  --output text)

# Tail logs
aws logs tail $LOG_GROUP --follow
```

### View CloudWatch Alarms
```bash
aws cloudwatch describe-alarms \
  --alarm-name-prefix $ENVIRONMENT \
  --region $AWS_REGION
```

---

## 🔐 Security Best Practices Implemented

### 1. Network Security
- ✅ App Runner in VPC with private subnets
- ✅ VPC Endpoints (no internet gateway for backend)
- ✅ Security groups with least privilege
- ✅ No public IPs for backend resources

### 2. Encryption
- ✅ S3: KMS encryption at rest
- ✅ DynamoDB: KMS encryption at rest
- ✅ Secrets Manager: KMS encryption
- ✅ TLS 1.2+ for all transit

### 3. Access Control
- ✅ IAM roles with least privilege
- ✅ No hardcoded credentials
- ✅ Secrets in AWS Secrets Manager
- ✅ S3 bucket policies blocking public access

### 4. Monitoring
- ✅ CloudWatch alarms for CPU/Memory
- ✅ S3 access logging
- ✅ CloudWatch Logs for application
- ✅ DynamoDB Point-in-Time Recovery

### 5. Data Protection
- ✅ S3 versioning enabled
- ✅ S3 lifecycle policies (30-day retention)
- ✅ DynamoDB TTL for automatic cleanup
- ✅ Backup and recovery enabled

---

## 🎯 Pre-Presentation Checklist

### 1 Hour Before Demo:
```bash
# 1. Verify service is running
aws apprunner describe-service \
  --service-arn $(aws cloudformation describe-stacks \
    --stack-name $ENVIRONMENT-stack \
    --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceArn'].OutputValue" \
    --output text) \
  --query "Service.Status" \
  --output text
# Should return: RUNNING

# 2. Test health endpoint
curl -I $(aws cloudformation describe-stacks \
  --stack-name $ENVIRONMENT-stack \
  --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceUrl'].OutputValue" \
  --output text)
# Should return: HTTP/2 200

# 3. Warm up Bedrock connection (upload a test resume)
# This ensures first demo user doesn't hit cold start

# 4. Check CloudWatch metrics
aws cloudwatch get-metric-statistics \
  --namespace AWS/AppRunner \
  --metric-name RequestCount \
  --dimensions Name=ServiceName,Value=$ENVIRONMENT-web \
  --start-time $(date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%S) \
  --end-time $(date -u +%Y-%m-%dT%H:%M:%S) \
  --period 300 \
  --statistics Sum
```

### During Demo:
- App Runner auto-scales from 1 → 10 instances
- Handles 10-30 concurrent users easily
- No manual intervention needed

---

## 💰 Cost Estimate

### Presentation Day (10-30 users, 2 hours):
- App Runner: ~$0.50
- DynamoDB: ~$0.10
- S3: ~$0.05
- Bedrock: ~$2.00 (depends on usage)
- **Total: ~$3/demo**

### Monthly (Idle):
- App Runner: ~$5 (provisioned instances)
- DynamoDB: ~$0 (on-demand, no traffic)
- S3: ~$0.50 (storage)
- **Total: ~$6/month**

---

## 🆘 Troubleshooting

### Issue: App Runner deployment fails
```bash
# Check service events
aws apprunner list-operations \
  --service-arn <SERVICE_ARN> \
  --region $AWS_REGION
```

### Issue: Can't access backend services
```bash
# Verify VPC endpoints are active
aws ec2 describe-vpc-endpoints \
  --filters "Name=vpc-id,Values=<VPC_ID>" \
  --query "VpcEndpoints[*].[ServiceName,State]" \
  --output table
```

### Issue: High latency
```bash
# Check if VPC connector is attached
aws apprunner describe-service \
  --service-arn <SERVICE_ARN> \
  --query "Service.NetworkConfiguration.EgressConfiguration" \
  --output json
```

---

## 📚 Additional Resources

- [App Runner Documentation](https://docs.aws.amazon.com/apprunner/)
- [VPC Endpoints Guide](https://docs.aws.amazon.com/vpc/latest/privatelink/vpc-endpoints.html)
- [Bedrock Security Best Practices](https://docs.aws.amazon.com/bedrock/latest/userguide/security-best-practices.html)
- [AWS Pricing Calculator](https://calculator.aws)

---

## ✅ Compliance & Security Certifications

This architecture follows:
- AWS Well-Architected Framework
- OWASP Top 10 security practices
- PCI DSS encryption requirements
- GDPR data protection standards

---

**🎉 You're ready to deploy! Run the commands above and your secure, production-ready ReRhythm app will be live in ~15 minutes.**
