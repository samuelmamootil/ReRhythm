using Amazon;
using Amazon.BedrockAgentRuntime;
using Amazon.DynamoDBv2;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.Textract;
using Microsoft.Extensions.Configuration;

namespace ReRhythm.Infrastructure;

/// <summary>
/// Factory that builds AWS SDK clients.
/// On EC2, credentials come automatically from the IAM instance role — no keys needed.
/// </summary>
public static class AwsClientFactory
{
    public static IAmazonS3 CreateS3Client(IConfiguration config)
    {
        var region = RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "us-east-1");
        return new AmazonS3Client(region);
    }

    public static IAmazonTextract CreateTextractClient(IConfiguration config)
    {
        var region = RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "us-east-1");
        return new AmazonTextractClient(region);
    }

    public static IAmazonDynamoDB CreateDynamoDbClient(IConfiguration config)
    {
        var region = RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "us-east-1");
        return new AmazonDynamoDBClient(region);
    }

    public static IAmazonBedrockAgentRuntime CreateBedrockAgentRuntimeClient(IConfiguration config)
    {
        var region = RegionEndpoint.GetBySystemName(config["AWS:Region"] ?? "us-east-1");
        return new AmazonBedrockAgentRuntimeClient(region);
    }
}
