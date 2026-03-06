using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class NetworkingService
{
    private readonly IAmazonDynamoDB _dynamoDb;
    private readonly DynamoDbService _dynamoDbService;
    private readonly ILogger<NetworkingService> _logger;
    private readonly string _connectionsTable;
    private readonly string _roadmapsTable;

    public NetworkingService(
        IAmazonDynamoDB dynamoDb,
        DynamoDbService dynamoDbService,
        ILogger<NetworkingService> logger,
        Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _dynamoDb = dynamoDb;
        _dynamoDbService = dynamoDbService;
        _logger = logger;
        _connectionsTable = config["ReRhythm:ConnectionsTable"] ?? "rerythm-prod-connections";
        _roadmapsTable = config["ReRhythm:RoadmapsTable"] ?? "rerythm-roadmaps";
    }

    public async Task<List<UserProfile>> GetMembersByIndustryAsync(string industry, string currentUserId, CancellationToken ct)
    {
        try
        {
            var allRoadmaps = await _dynamoDbService.GetAllRoadmapsAsync(ct);
            
            // Group by UserId and take only the latest roadmap per user
            var latestRoadmaps = allRoadmaps
                .Where(r => r.Industry == industry && r.UserId != currentUserId)
                .GroupBy(r => r.UserId)
                .Select(g => g.OrderByDescending(r => r.GeneratedAt).First())
                .Take(50)
                .ToList();

            var profiles = new List<UserProfile>();
            var seenUserIds = new HashSet<string>();
            
            foreach (var plan in latestRoadmaps)
            {
                // Extra safety check to prevent duplicates
                if (seenUserIds.Contains(plan.UserId))
                    continue;
                    
                seenUserIds.Add(plan.UserId);
                
                try
                {
                    var lessons = await _dynamoDbService.GetAllLessonsForUserAsync(plan.UserId, ct);
                    var completed = lessons.Count(l => l.IsCompleted);
                    var total = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;

                    profiles.Add(new UserProfile
                    {
                        UserId = plan.UserId,
                        Name = plan.FullName?.Split('\n')[0]?.Trim() ?? "User",
                        TargetRole = plan.TargetRole,
                        Industry = plan.Industry,
                        CompletedLessons = completed,
                        TotalLessons = total,
                        JoinedAt = plan.GeneratedAt
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load profile for user {UserId}", plan.UserId);
                }
            }

            return profiles.OrderByDescending(p => p.CompletedLessons).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members by industry {Industry}", industry);
            return new List<UserProfile>();
        }
    }

    public async Task<string> SendConnectionRequestAsync(ConnectionRequest request, CancellationToken ct)
    {
        await _dynamoDb.PutItemAsync(new PutItemRequest
        {
            TableName = _connectionsTable,
            Item = new Dictionary<string, AttributeValue>
            {
                ["ConnectionId"] = new AttributeValue { S = request.ConnectionId },
                ["FromUserId"] = new AttributeValue { S = request.FromUserId },
                ["ToUserId"] = new AttributeValue { S = request.ToUserId },
                ["Status"] = new AttributeValue { S = request.Status },
                ["Message"] = new AttributeValue { S = request.Message },
                ["CreatedAt"] = new AttributeValue { S = request.CreatedAt.ToString("o") }
            }
        }, ct);

        return request.ConnectionId;
    }

    public async Task<List<ConnectionRequest>> GetPendingRequestsAsync(string userId, CancellationToken ct)
    {
        var response = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _connectionsTable,
            IndexName = "ToUserId-Status-Index",
            KeyConditionExpression = "ToUserId = :userId AND #status = :status",
            ExpressionAttributeNames = new Dictionary<string, string>
            {
                ["#status"] = "Status"
            },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId },
                [":status"] = new AttributeValue { S = "Pending" }
            }
        }, ct);

        return response.Items.Select(item => new ConnectionRequest
        {
            ConnectionId = item["ConnectionId"].S,
            FromUserId = item["FromUserId"].S,
            ToUserId = item["ToUserId"].S,
            Status = item["Status"].S,
            Message = item["Message"].S,
            CreatedAt = DateTime.Parse(item["CreatedAt"].S)
        }).ToList();
    }

    public async Task<List<string>> GetConnectionsAsync(string userId, CancellationToken ct)
    {
        var sent = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _connectionsTable,
            IndexName = "FromUserId-Status-Index",
            KeyConditionExpression = "FromUserId = :userId AND #status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId },
                [":status"] = new AttributeValue { S = "Accepted" }
            }
        }, ct);

        var received = await _dynamoDb.QueryAsync(new QueryRequest
        {
            TableName = _connectionsTable,
            IndexName = "ToUserId-Status-Index",
            KeyConditionExpression = "ToUserId = :userId AND #status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":userId"] = new AttributeValue { S = userId },
                [":status"] = new AttributeValue { S = "Accepted" }
            }
        }, ct);

        var connections = sent.Items.Select(i => i["ToUserId"].S)
            .Concat(received.Items.Select(i => i["FromUserId"].S))
            .Distinct()
            .ToList();

        return connections;
    }

    public async Task UpdateConnectionStatusAsync(string connectionId, string status, CancellationToken ct)
    {
        await _dynamoDb.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _connectionsTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["ConnectionId"] = new AttributeValue { S = connectionId }
            },
            UpdateExpression = "SET #status = :status",
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":status"] = new AttributeValue { S = status }
            }
        }, ct);
    }
}
