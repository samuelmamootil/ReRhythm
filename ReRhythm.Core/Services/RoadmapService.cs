using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ReRhythm.Core.Services;

public class RoadmapService
{
    private readonly BedrockRAGService _ragService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<RoadmapService> _logger;

    public RoadmapService(
        BedrockRAGService ragService,
        DynamoDbService dynamoDb,
        ILogger<RoadmapService> logger)
    {
        _ragService = ragService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    public async Task<RoadmapPlan> GenerateRoadmapAsync(
        string userId,
        string resumeText,
        string targetRole,
        CancellationToken ct = default)
    {
        // Build the RAG query for roadmap generation
        var query = $"""
            Generate a 28-day micro-sprint roadmap for someone targeting the role of {targetRole}.
            Create 4 weekly modules with:
            - Daily 15-minute learning tasks
            - Skill gap identification based on the resume
            - AWS certification milestones
            - Portfolio project suggestions
            - Interview preparation checkpoints
            
            Structure output as a milestone tracker showing skills unlocked per module.
            """;

        var ragResponse = await _ragService.RetrieveAndGenerateAsync(
            query, resumeText, targetRole, ct);

        // Parse the JSON response from Bedrock
        RoadmapPlan plan;
        try
        {
            var jsonText = ragResponse.GeneratedText;
            
            // Remove markdown code fences if present
            if (jsonText.Contains("```json"))
            {
                var startIndex = jsonText.IndexOf("```json") + 7;
                var endIndex = jsonText.LastIndexOf("```");
                if (endIndex > startIndex)
                {
                    jsonText = jsonText.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
            else if (jsonText.Contains("```"))
            {
                var startIndex = jsonText.IndexOf("```") + 3;
                var endIndex = jsonText.LastIndexOf("```");
                if (endIndex > startIndex)
                {
                    jsonText = jsonText.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
            
            plan = JsonSerializer.Deserialize<RoadmapPlan>(
                jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Empty roadmap response");
        }
        catch (JsonException)
        {
            // Fallback: wrap raw text in structured plan
            plan = new RoadmapPlan
            {
                UserId = userId,
                TargetRole = targetRole,
                RawContent = ragResponse.GeneratedText,
                GeneratedAt = DateTime.UtcNow
            };
        }

        plan.UserId = userId;
        plan.Citations = ragResponse.Citations;

        // Persist to DynamoDB
        await _dynamoDb.SaveRoadmapAsync(plan, ct);

        _logger.LogInformation(
            "28-day roadmap generated and saved for user {UserId}, role: {Role}",
            userId, targetRole);

        return plan;
    }
}

public class RoadmapPlan
{
    public string UserId { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public string? RawContent { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<WeeklyModule> Modules { get; set; } = [];
    public List<CitationSource> Citations { get; set; } = [];
    public List<string> SkillsIdentified { get; set; } = [];
    public List<string> SkillsToAcquire { get; set; } = [];
}

public class WeeklyModule
{
    public int WeekNumber { get; set; }
    public string Theme { get; set; } = string.Empty;
    public List<DailySprint> DailySprints { get; set; } = [];
    public List<string> MilestonesUnlocked { get; set; } = [];
    public string PortfolioProject { get; set; } = string.Empty;
}

public class DailySprint
{
    public int Day { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string LessonFormat { get; set; } = string.Empty; // flashcard, quiz, lab
    public int EstimatedMinutes { get; set; } = 15;
    public string ResourceRef { get; set; } = string.Empty;
}
