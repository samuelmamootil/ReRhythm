# 🎯 ReRhythm - Presentation Day Quick Reference

## 🚀 Quick Deploy (One Command)

```powershell
.\deploy.ps1
```

That's it! Wait 15 minutes, get your URL, and you're live.

---

## 📋 Pre-Demo Checklist (1 Hour Before)

### 1. Verify Service Status
```powershell
aws apprunner describe-service --service-arn <ARN> --query "Service.Status"
```
✅ Should return: `RUNNING`

### 2. Test Health
```powershell
curl -I https://<your-app-url>
```
✅ Should return: `HTTP/2 200`

### 3. Warm Up (Upload Test Resume)
- Visit your app URL
- Upload a sample resume
- This warms up Bedrock connection
- First real user won't hit cold start

### 4. Get Your Demo URL
```powershell
aws cloudformation describe-stacks --stack-name rerythm-prod-stack --query "Stacks[0].Outputs[?OutputKey=='AppRunnerServiceUrl'].OutputValue" --output text
```

---

## 🎤 Demo Talking Points

### Security Architecture
> "Our frontend is publicly accessible via App Runner, but all backend services—S3, DynamoDB, Bedrock, and Textract—are isolated in a private VPC with VPC endpoints. Zero internet exposure for sensitive data."

### Encryption
> "Data is encrypted at rest using AWS KMS for S3 and DynamoDB, and in transit using TLS 1.2+. All secrets are stored in AWS Secrets Manager, not hardcoded."

### Scalability
> "App Runner auto-scales from 1 to 10 instances based on traffic. It can handle 100+ concurrent users with our current configuration."

### Cost Efficiency
> "We're using serverless services—DynamoDB on-demand, App Runner with auto-scaling. When idle, this costs about $6/month. During demos, it scales automatically."

### Infrastructure as Code
> "Everything is defined in CloudFormation. We can tear down and recreate the entire environment in 15 minutes. No manual configuration, no drift."

---

## 🔥 Live Demo Flow

1. **Show Architecture Diagram** (Assets/architecture.png)
   - Point out VPC endpoints
   - Highlight encryption layers

2. **Upload Resume**
   - Show Textract parsing
   - Mention PII protection

3. **Generate Roadmap**
   - Show Bedrock inference
   - Highlight 28-day structure

4. **Track Progress**
   - Complete a lesson
   - Show badge system

5. **Download Certificate**
   - Show PDF generation
   - Mention verification URL

---

## 📊 Monitoring During Demo

### Watch Real-Time Logs
```powershell
aws logs tail /aws/apprunner/rerythm-prod-web --follow
```

### Check Active Instances
```powershell
aws cloudwatch get-metric-statistics --namespace AWS/AppRunner --metric-name ActiveInstances --dimensions Name=ServiceName,Value=rerythm-prod-web --start-time $(date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%S) --end-time $(date -u +%Y-%m-%dT%H:%M:%S) --period 60 --statistics Average
```

---

## 🆘 Emergency Troubleshooting

### App is slow
- **Cause**: Cold start or Bedrock throttling
- **Fix**: Already warmed up in pre-demo checklist

### Can't upload resume
- **Cause**: S3 permissions or VPC endpoint issue
- **Check**: `aws s3 ls s3://rerythm-prod-resume-uploads-<ACCOUNT_ID>`

### Roadmap generation fails
- **Cause**: Bedrock model access or VPC endpoint
- **Check**: `aws bedrock list-foundation-models --region us-east-1`

### 500 Error
- **Check logs**: `aws logs tail /aws/apprunner/rerythm-prod-web --since 5m`

---

## 💰 Cost Breakdown (Show This!)

| Service | Demo Day | Monthly (Idle) |
|---------|----------|----------------|
| App Runner | $0.50 | $5 |
| DynamoDB | $0.10 | $0 |
| S3 | $0.05 | $0.50 |
| Bedrock | $2.00 | $0 |
| **Total** | **~$3** | **~$6** |

> "This architecture costs less than a coffee per month when idle, and scales automatically for production."

---

## 🏆 AWS Hackathon Highlights

### What Judges Love:
1. ✅ **Security-First Design** - VPC endpoints, encryption, least privilege
2. ✅ **Cloud-Native** - Serverless, auto-scaling, managed services
3. ✅ **Infrastructure as Code** - Reproducible, version-controlled
4. ✅ **Cost-Optimized** - Pay-per-use, scales to zero
5. ✅ **Production-Ready** - Monitoring, logging, disaster recovery

### Key Differentiators:
- "No third-party AI providers—100% AWS Bedrock"
- "Private VPC with zero internet exposure for backend"
- "Complete teardown and rebuild in 15 minutes"
- "Encryption at rest and in transit by default"

---

## 🎬 Closing Statement

> "ReRhythm demonstrates how to build a secure, scalable, cost-effective AI application using AWS best practices. From VPC isolation to KMS encryption, from auto-scaling to infrastructure as code—this is production-ready architecture that can be deployed, monitored, and torn down with a single command."

---

## 📞 Support Resources

- **CloudFormation Console**: https://console.aws.amazon.com/cloudformation
- **App Runner Console**: https://console.aws.amazon.com/apprunner
- **CloudWatch Logs**: https://console.aws.amazon.com/cloudwatch/home#logsV2:log-groups
- **Cost Explorer**: https://console.aws.amazon.com/cost-management/home

---

## 🗑️ Post-Demo Cleanup

```powershell
# Delete everything (no residual costs)
aws cloudformation delete-stack --stack-name rerythm-prod-stack
aws ecr delete-repository --repository-name rerythm --force
```

---

**🎉 You're ready! Break a leg!**
