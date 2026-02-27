# Create DynamoDB Table for Badge Achievements

aws dynamodb create-table \
    --table-name rerythm-prod-badge-achievements \
    --attribute-definitions \
        AttributeName=userId,AttributeType=S \
        AttributeName=badgeId,AttributeType=S \
    --key-schema \
        AttributeName=userId,KeyType=HASH \
        AttributeName=badgeId,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST \
    --region us-east-1

# Verify table creation
aws dynamodb describe-table \
    --table-name rerythm-prod-badge-achievements \
    --region us-east-1
