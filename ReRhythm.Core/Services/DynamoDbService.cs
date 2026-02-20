using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using ReRhythm.Core.Models;
using System.Text.Json;

namespace ReRhythm.Core.Services;

public class DynamoDbService
{
    private readonly IAmazonDynamoDB _dynamo;
    private readonly IConfiguration _config;

    private string RoadmapTable => _config["ReRhythm:RoadmapTableName"]!;
    private string LessonTable  => _config["ReRhythm:LessonTableName"]!;

    public DynamoDbService(IAmazonDynamoDB dynamo, IConfiguration config)
    {
        _dynamo = dynamo;
        _config = config;
    }

    // ── Roadmap Methods ────────────────────────────────────────────────────────

    public async Task SaveRoadmapAsync(RoadmapPlan plan, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"]      = new AttributeValue { S = plan.UserId },
            ["createdAt"]   = new AttributeValue { S = plan.GeneratedAt.ToString("O") },
            ["targetRole"]  = new AttributeValue { S = plan.TargetRole },
            ["roadmapJson"] = new AttributeValue { S = JsonSerializer.Serialize(plan) },
            // TTL: keep active profiles for 30 days per privacy policy
            ["ttl"] = new AttributeValue
            {
                N = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds().ToString()
            }
        };

        await _dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = RoadmapTable,
            Item = item
        }, ct);
    }

    public async Task<RoadmapPlan?> GetLatestRoadmapAsync(
        string userId,
        CancellationToken ct = default)
    {
        var response = await _dynamo.QueryAsync(new QueryRequest
        {
            TableName = RoadmapTable,
            KeyConditionExpression = "userId = :uid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":uid"] = new AttributeValue { S = userId }
            },
            ScanIndexForward = false, // latest first
            Limit = 1
        }, ct);

        var item = response.Items.FirstOrDefault();
        if (item is null) return null;

        return JsonSerializer.Deserialize<RoadmapPlan>(
            item["roadmapJson"].S,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // ── Lesson Plan Methods ────────────────────────────────────────────────────

    public async Task SaveLessonPlanAsync(LessonPlan lesson, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"]      = new AttributeValue { S = lesson.UserId },
            ["moduleId"]    = new AttributeValue { S = lesson.ModuleId },
            ["topic"]       = new AttributeValue { S = lesson.Topic },
            ["targetRole"]  = new AttributeValue { S = lesson.TargetRole },
            ["lessonJson"]  = new AttributeValue { S = JsonSerializer.Serialize(lesson) },
            ["isCompleted"] = new AttributeValue { BOOL = lesson.IsCompleted },
            ["createdAt"]   = new AttributeValue { S = lesson.CreatedAt.ToString("O") }
        };

        await _dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = LessonTable,
            Item = item
        }, ct);
    }

    public async Task<LessonPlan?> GetLessonPlanAsync(
        string userId,
        string moduleId,
        CancellationToken ct = default)
    {
        var response = await _dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = LessonTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"]   = new AttributeValue { S = userId },
                ["moduleId"] = new AttributeValue { S = moduleId }
            }
        }, ct);

        if (!response.IsItemSet) return null;

        return JsonSerializer.Deserialize<LessonPlan>(
            response.Item["lessonJson"].S,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task MarkLessonCompleteAsync(
        string userId,
        string moduleId,
        CancellationToken ct = default)
    {
        await _dynamo.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = LessonTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"]   = new AttributeValue { S = userId },
                ["moduleId"] = new AttributeValue { S = moduleId }
            },
            UpdateExpression = "SET isCompleted = :val",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":val"] = new AttributeValue { BOOL = true }
            }
        }, ct);
    }

    public async Task<List<LessonPlan>> GetAllLessonsForUserAsync(
        string userId,
        CancellationToken ct = default)
    {
        var response = await _dynamo.QueryAsync(new QueryRequest
        {
            TableName = LessonTable,
            KeyConditionExpression = "userId = :uid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":uid"] = new AttributeValue { S = userId }
            },
            ScanIndexForward = true // oldest first = chronological sprint order
        }, ct);

        return response.Items
            .Select(item => JsonSerializer.Deserialize<LessonPlan>(
                item["lessonJson"].S,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
            .Where(l => l is not null)
            .ToList();
    }

    public async Task<int> GetCompletedLessonCountAsync(
        string userId,
        CancellationToken ct = default)
    {
        var response = await _dynamo.QueryAsync(new QueryRequest
        {
            TableName = LessonTable,
            KeyConditionExpression = "userId = :uid",
            FilterExpression = "isCompleted = :done",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":uid"]  = new AttributeValue { S = userId },
                [":done"] = new AttributeValue { BOOL = true }
            },
            Select = Select.COUNT
        }, ct);

        return response.Count ?? 0;
    }
}
