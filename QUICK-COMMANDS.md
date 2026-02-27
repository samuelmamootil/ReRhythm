# ⚡ Quick Command Reference

## 🚀 Deploy

```powershell
# One-click deploy
.\deploy.ps1

# With custom domain
.\deploy.ps1 -DomainName "rerythm.yourdomain.com" -HostedZoneId "Z123456"
```

---

## 📊 Monitor

```powershell
# Get service URL
aws cloudformation describe-stacks --stack-name rerythm-prod-stack --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceUrl'].OutputValue" --output text

# Check service status
aws apprunner describe-service --service-arn $(aws cloudformation describe-stacks --stack-name rerythm-prod-stack --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceArn'].OutputValue" --output text) --query "Service.Status"

# Tail logs
aws logs tail /aws/apprunner/rerythm-prod-web --follow

# Check health
curl -I https://<your-app-url>
```

---

## 🔄 Update

```powershell
# Rebuild and redeploy
docker build -t rerythm:latest .
docker tag rerythm:latest <ECR_URI>:latest
docker push <ECR_URI>:latest
aws apprunner start-deployment --service-arn <SERVICE_ARN>
```

---

## 🔍 Debug

```powershell
# View recent logs
aws logs tail /aws/apprunner/rerythm-prod-web --since 10m

# Check VPC endpoints
aws ec2 describe-vpc-endpoints --filters "Name=vpc-id,Values=<VPC_ID>" --query "VpcEndpoints[*].[ServiceName,State]"

# Check CloudWatch alarms
aws cloudwatch describe-alarms --alarm-name-prefix rerythm-prod

# View stack events
aws cloudformation describe-stack-events --stack-name rerythm-prod-stack --max-items 10
```

---

## 🗑️ Delete

```powershell
# Delete everything
aws cloudformation delete-stack --stack-name rerythm-prod-stack
aws ecr delete-repository --repository-name rerythm --force
```

---

## 💰 Cost

```powershell
# View current month costs
aws ce get-cost-and-usage --time-period Start=2025-01-01,End=2025-01-31 --granularity MONTHLY --metrics BlendedCost
```

---

## 🔒 Security

```powershell
# Check S3 encryption
aws s3api get-bucket-encryption --bucket rerythm-prod-resume-uploads-<ACCOUNT_ID>

# Check DynamoDB encryption
aws dynamodb describe-table --table-name rerythm-prod-roadmap-responses --query "Table.SSEDescription"

# List secrets
aws secretsmanager list-secrets

# Scan ECR image
aws ecr start-image-scan --repository-name rerythm --image-id imageTag=latest
```

---

## 📱 Quick URLs

- **CloudFormation Console**: https://console.aws.amazon.com/cloudformation
- **App Runner Console**: https://console.aws.amazon.com/apprunner
- **CloudWatch Logs**: https://console.aws.amazon.com/cloudwatch/home#logsV2:log-groups
- **Cost Explorer**: https://console.aws.amazon.com/cost-management/home
- **ECR Console**: https://console.aws.amazon.com/ecr
