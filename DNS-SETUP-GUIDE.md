# 🌐 DNS Setup Guide - Route53 Hosted Zone

## Prerequisites

You need a domain name. You can either:
1. **Register a new domain** through Route53 (~$12/year)
2. **Use an existing domain** and point nameservers to Route53

---

## Option 1: Register New Domain in Route53

### Step 1: Register Domain
```bash
# List available domains
aws route53domains check-domain-availability --domain-name rerythm-demo.com

# Register domain (example)
aws route53domains register-domain \
  --domain-name rerythm-demo.com \
  --duration-in-years 1 \
  --admin-contact FirstName=John,LastName=Doe,ContactType=PERSON,AddressLine1="123 Main St",City=Seattle,State=WA,CountryCode=US,ZipCode=98101,PhoneNumber=+1.2065551234,Email=john@example.com \
  --registrant-contact FirstName=John,LastName=Doe,ContactType=PERSON,AddressLine1="123 Main St",City=Seattle,State=WA,CountryCode=US,ZipCode=98101,PhoneNumber=+1.2065551234,Email=john@example.com \
  --tech-contact FirstName=John,LastName=Doe,ContactType=PERSON,AddressLine1="123 Main St",City=Seattle,State=WA,CountryCode=US,ZipCode=98101,PhoneNumber=+1.2065551234,Email=john@example.com
```

**Note**: Route53 automatically creates a hosted zone when you register a domain.

### Step 2: Get Hosted Zone ID
```bash
aws route53 list-hosted-zones --query "HostedZones[?Name=='rerythm-demo.com.'].Id" --output text
```

**Output**: `/hostedzone/Z1234567890ABC`

Use `Z1234567890ABC` as your HostedZoneId parameter.

---

## Option 2: Use Existing Domain (Transfer DNS to Route53)

### Step 1: Create Hosted Zone
```bash
aws route53 create-hosted-zone \
  --name rerythm.yourdomain.com \
  --caller-reference $(date +%s)
```

### Step 2: Get Nameservers
```bash
aws route53 get-hosted-zone --id Z1234567890ABC --query "DelegationSet.NameServers"
```

**Output**:
```json
[
    "ns-123.awsdns-12.com",
    "ns-456.awsdns-45.net",
    "ns-789.awsdns-78.org",
    "ns-012.awsdns-01.co.uk"
]
```

### Step 3: Update Domain Nameservers

Go to your domain registrar (GoDaddy, Namecheap, etc.) and update nameservers to the ones from Step 2.

**Wait 24-48 hours for DNS propagation.**

### Step 4: Verify DNS Propagation
```bash
nslookup rerythm.yourdomain.com
```

---

## Option 3: Quick Demo Setup (Free Subdomain)

For hackathon demos, you can use a free subdomain service:

### Using FreeDNS (freedns.afraid.org)
1. Sign up at https://freedns.afraid.org
2. Create a subdomain (e.g., `rerythm.mooo.com`)
3. Get DNS records
4. Manually create CNAME record pointing to App Runner URL

**Note**: This won't work with CloudFormation automated DNS setup. You'll need to manually create the CNAME after deployment.

---

## Quick Setup for Hackathon (Recommended)

### If you don't have a domain:

1. **Register a cheap domain** (~$1-3 for .xyz, .site, .online):
   ```bash
   aws route53domains check-domain-availability --domain-name rerythm-demo.xyz
   aws route53domains register-domain --domain-name rerythm-demo.xyz --duration-in-years 1 ...
   ```

2. **Get Hosted Zone ID**:
   ```bash
   export HOSTED_ZONE_ID=$(aws route53 list-hosted-zones --query "HostedZones[?Name=='rerythm-demo.xyz.'].Id" --output text | cut -d'/' -f3)
   echo $HOSTED_ZONE_ID
   ```

3. **Deploy**:
   ```powershell
   .\deploy.ps1 -DomainName "rerythm-demo.xyz" -HostedZoneId $HOSTED_ZONE_ID
   ```

---

## Verify DNS Setup

### Check Hosted Zone
```bash
aws route53 list-hosted-zones
```

### Check DNS Records
```bash
aws route53 list-resource-record-sets --hosted-zone-id Z1234567890ABC
```

### Test DNS Resolution
```bash
nslookup rerythm.yourdomain.com
dig rerythm.yourdomain.com
```

---

## Troubleshooting

### Issue: "Hosted zone not found"
**Solution**: Create hosted zone first:
```bash
aws route53 create-hosted-zone --name yourdomain.com --caller-reference $(date +%s)
```

### Issue: "DNS not resolving"
**Solution**: Wait for DNS propagation (5-10 minutes for Route53, up to 48 hours for external registrars)

### Issue: "Certificate validation failed"
**Solution**: App Runner automatically handles SSL certificates. Wait 5-10 minutes after deployment.

---

## Cost

- **Hosted Zone**: $0.50/month
- **DNS Queries**: $0.40 per million queries (first 1 billion queries/month)
- **Domain Registration**: $12-50/year (depends on TLD)

**For hackathon demo**: ~$0.50 for the month

---

## Example: Complete Setup

```bash
# 1. Register domain
aws route53domains register-domain --domain-name rerythm-demo.xyz --duration-in-years 1 ...

# 2. Wait for registration (5-10 minutes)
aws route53domains get-domain-detail --domain-name rerythm-demo.xyz

# 3. Get hosted zone ID (auto-created)
export HOSTED_ZONE_ID=$(aws route53 list-hosted-zones --query "HostedZones[?Name=='rerythm-demo.xyz.'].Id" --output text | cut -d'/' -f3)

# 4. Deploy app
.\deploy.ps1 -DomainName "rerythm-demo.xyz" -HostedZoneId $HOSTED_ZONE_ID

# 5. Wait for deployment (15 minutes)

# 6. Test
curl -I https://rerythm-demo.xyz
```

---

## For Your Presentation

**Show judges**:
- Custom domain (looks professional)
- HTTPS automatically configured
- DNS managed through Route53
- Infrastructure as Code includes DNS setup

**Talking point**:
> "We're using Route53 for DNS management with automatic SSL certificate provisioning through App Runner. The entire DNS configuration is defined in our CloudFormation template, making it reproducible and version-controlled."

---

## Quick Reference

```bash
# List hosted zones
aws route53 list-hosted-zones

# Get hosted zone ID
aws route53 list-hosted-zones --query "HostedZones[?Name=='yourdomain.com.'].Id" --output text

# List DNS records
aws route53 list-resource-record-sets --hosted-zone-id Z1234567890ABC

# Test DNS
nslookup yourdomain.com
dig yourdomain.com
```

---

**Need help? Check AWS Route53 documentation: https://docs.aws.amazon.com/route53/**
