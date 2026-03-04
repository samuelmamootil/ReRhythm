using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;
using System.Text.Json;

namespace ReRhythm.Core.Services;

public class DynamoDbService
{
    private readonly IAmazonDynamoDB _dynamo;
    private readonly IConfiguration _config;
    private readonly ILogger<DynamoDbService> _logger;

    private string RoadmapTable => _config["ReRhythm:RoadmapTableName"]!;
    private string LessonTable  => _config["ReRhythm:LessonTableName"]!;
    private string BadgeTable   => _config["ReRhythm:BadgeTableName"]!;

    public DynamoDbService(IAmazonDynamoDB dynamo, IConfiguration config, ILogger<DynamoDbService> logger)
    {
        _dynamo = dynamo;
        _config = config;
        _logger = logger;
    }

    // ── Roadmap Methods ────────────────────────────────────────────────────────

    public async Task SaveRoadmapAsync(RoadmapPlan plan, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"]      = new AttributeValue { S = plan.UserId },
            ["createdAt"]   = new AttributeValue { S = plan.GeneratedAt.ToString("O") },
            ["targetRole"]  = new AttributeValue { S = plan.TargetRole },
            ["industry"]    = new AttributeValue { S = plan.Industry },
            ["totalYearsOfExperience"] = new AttributeValue { N = plan.TotalYearsOfExperience.ToString() },
            ["yearsInTargetIndustry"] = new AttributeValue { N = plan.YearsInTargetIndustry.ToString() },
            ["fullName"]    = new AttributeValue { S = plan.FullName },
            ["contactInfo"] = new AttributeValue { S = plan.ContactInfo },
            ["personalityType"] = new AttributeValue { S = plan.PersonalityType ?? "" },
            ["subscriptionTier"] = new AttributeValue { S = plan.SubscriptionTier },
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
        _logger.LogInformation("Fetching lesson: userId={UserId}, moduleId={ModuleId}", userId, moduleId);
        
        // First get the lesson
        var lesson = await GetLessonPlanAsync(userId, moduleId, ct);
        if (lesson == null)
        {
            _logger.LogWarning("Lesson not found: userId={UserId}, moduleId={ModuleId}", userId, moduleId);
            return;
        }
        
        _logger.LogInformation("Marking lesson complete: {Topic}", lesson.Topic);
        
        // Update completion status
        lesson.IsCompleted = true;
        
        // Save back to DynamoDB
        await _dynamo.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = LessonTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"]   = new AttributeValue { S = userId },
                ["moduleId"] = new AttributeValue { S = moduleId }
            },
            UpdateExpression = "SET isCompleted = :val, lessonJson = :json",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":val"] = new AttributeValue { BOOL = true },
                [":json"] = new AttributeValue { S = JsonSerializer.Serialize(lesson) }
            }
        }, ct);
        
        _logger.LogInformation("Lesson marked complete successfully");
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

    public async Task<int> GetCompletedLessonsCountAsync(string userId, CancellationToken ct = default)
        => await GetCompletedLessonCountAsync(userId, ct);

    public async Task<List<RoadmapPlan>> GetAllRoadmapsAsync(CancellationToken ct = default)
    {
        var response = await _dynamo.ScanAsync(new ScanRequest
        {
            TableName = RoadmapTable,
            Limit = 1000
        }, ct);

        return response.Items
            .Select(item => JsonSerializer.Deserialize<RoadmapPlan>(
                item["roadmapJson"].S,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!)
            .Where(p => p is not null)
            .ToList();
    }

    // ── Badge Achievement Methods ──────────────────────────────────────────────

    public async Task SaveBadgeAchievementAsync(BadgeAchievement badge, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"]      = new AttributeValue { S = badge.UserId },
            ["badgeId"]     = new AttributeValue { S = badge.BadgeId },
            ["badgeName"]   = new AttributeValue { S = badge.BadgeName },
            ["badgeIcon"]   = new AttributeValue { S = badge.BadgeIcon },
            ["unlockedAt"]  = new AttributeValue { S = badge.UnlockedAt.ToString("O") },
            ["lessonsCompletedAtUnlock"] = new AttributeValue { N = badge.LessonsCompletedAtUnlock.ToString() },
            ["progressPercentAtUnlock"] = new AttributeValue { N = badge.ProgressPercentAtUnlock.ToString() }
        };

        await _dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = BadgeTable,
            Item = item
        }, ct);
    }

    public async Task<List<BadgeAchievement>> GetUserBadgesAsync(string userId, CancellationToken ct = default)
    {
        var response = await _dynamo.QueryAsync(new QueryRequest
        {
            TableName = BadgeTable,
            KeyConditionExpression = "userId = :uid",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":uid"] = new AttributeValue { S = userId }
            }
        }, ct);

        return response.Items.Select(item => new BadgeAchievement
        {
            UserId = item["userId"].S,
            BadgeId = item["badgeId"].S,
            BadgeName = item["badgeName"].S,
            BadgeIcon = item["badgeIcon"].S,
            UnlockedAt = DateTime.Parse(item["unlockedAt"].S),
            LessonsCompletedAtUnlock = int.Parse(item["lessonsCompletedAtUnlock"].N),
            ProgressPercentAtUnlock = int.Parse(item["progressPercentAtUnlock"].N)
        }).ToList();
    }

    public async Task<bool> HasBadgeAsync(string userId, string badgeId, CancellationToken ct = default)
    {
        var response = await _dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = BadgeTable,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userId"]  = new AttributeValue { S = userId },
                ["badgeId"] = new AttributeValue { S = badgeId }
            }
        }, ct);

        return response.IsItemSet;
    }

    // ── Community & Referral Methods ───────────────────────────────────────────

    public async Task<int> GetTotalUsersCountAsync(CancellationToken ct = default)
    {
        var response = await _dynamo.ScanAsync(new ScanRequest
        {
            TableName = RoadmapTable,
            Select = Select.COUNT
        }, ct);
        return response.Count ?? 0;
    }

    public async Task<int> GetCompletedUsersCountAsync(CancellationToken ct = default)
    {
        var allUsers = await GetAllRoadmapsAsync(ct);
        var completedCount = 0;

        foreach (var plan in allUsers)
        {
            var lessons = await GetAllLessonsForUserAsync(plan.UserId, ct);
            var totalLessons = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;
            var completed = lessons.Count(l => l.IsCompleted);
            if (totalLessons > 0 && completed >= totalLessons)
                completedCount++;
        }

        return completedCount;
    }

    public async Task<int> GetReferralCountAsync(string userId, CancellationToken ct = default)
    {
        var response = await _dynamo.ScanAsync(new ScanRequest
        {
            TableName = RoadmapTable,
            FilterExpression = "referredBy = :refId AND subscriptionTier = :tier",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":refId"] = new AttributeValue { S = userId },
                [":tier"] = new AttributeValue { S = "Gold" }
            },
            Select = Select.COUNT
        }, ct);
        return response.Count ?? 0;
    }
}
