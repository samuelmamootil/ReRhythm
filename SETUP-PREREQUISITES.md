# Prerequisites Setup Guide

## You need to install AWS CLI and Docker before deploying

### Step 1: Install AWS CLI

**Download and Install:**
1. Download AWS CLI v2 for Windows: https://awscli.amazonaws.com/AWSCLIV2.msi
2. Run the installer
3. Restart PowerShell

**Or use winget:**
```powershell
winget install Amazon.AWSCLI
```

**Verify installation:**
```powershell
aws --version
```

### Step 2: Configure AWS CLI

```powershell
aws configure
```

Enter:
- AWS Access Key ID: [Your access key]
- AWS Secret Access Key: [Your secret key]
- Default region: us-east-1
- Default output format: json

**Get credentials from AWS Console:**
1. Go to: https://console.aws.amazon.com/iam/
2. Click "Users" → Your username → "Security credentials"
3. Click "Create access key"
4. Copy Access Key ID and Secret Access Key

### Step 3: Install Docker Desktop

**Download and Install:**
1. Download Docker Desktop: https://www.docker.com/products/docker-desktop/
2. Run the installer
3. Restart your computer
4. Start Docker Desktop

**Verify installation:**
```powershell
docker --version
```

### Step 4: Verify Everything is Ready

```powershell
# Check AWS CLI
aws --version

# Check AWS credentials
aws sts get-caller-identity

# Check Docker
docker --version

# Check Docker is running
docker ps
```

### Step 5: Deploy ReRhythm

```powershell
cd C:\Sam\2026\ReRhythm
.\deploy.ps1
```

---

## Quick Install Commands (All at Once)

```powershell
# Install AWS CLI
winget install Amazon.AWSCLI

# Install Docker Desktop
winget install Docker.DockerDesktop

# Restart PowerShell, then configure AWS
aws configure
```

---

## Troubleshooting

### Issue: "aws is not recognized"
**Solution**: 
1. Close and reopen PowerShell
2. Or add to PATH: `C:\Program Files\Amazon\AWSCLIV2\`

### Issue: "docker is not recognized"
**Solution**: 
1. Make sure Docker Desktop is running
2. Restart PowerShell

### Issue: "Unable to locate credentials"
**Solution**: Run `aws configure` and enter your credentials

---

## Ready to Deploy?

Once all prerequisites are installed:

```powershell
.\deploy.ps1
```
