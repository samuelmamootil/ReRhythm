using System.Text.Json;
using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;

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
        string industry,
        int totalYearsOfExperience,
        int yearsInTargetIndustry,
        string fullName,
        string contactInfo,
        string? personalityType,
        string? customSkills = null,
        CancellationToken ct = default)
    {
        var personalityContext = string.IsNullOrEmpty(personalityType) ? "" : $"""
            
            User's personality profile (RIASEC): {personalityType}
            Tailor the learning approach and project types to match their natural strengths and interests.
            """;

        var customSkillsContext = string.IsNullOrEmpty(customSkills) ? "" : $"""
            
            IMPORTANT: User wants to learn these specific skills: {customSkills}
            Include these skills in the 28-day roadmap with dedicated lessons and practice exercises.
            """;

        var query = $"""
            Generate a 28-day micro-sprint roadmap for someone targeting the role of {targetRole}.
            
            Context:
            - Target Role: {targetRole}
            - Industry: {industry}
            - Total Years of Experience: {totalYearsOfExperience}
            - Years in Target Industry: {yearsInTargetIndustry}
            {personalityContext}{customSkillsContext}
            
            Create 4 weekly modules with:
            - Daily 15-minute learning tasks
            - Skill gap identification based on the resume
            - AWS certification milestones
            - Portfolio project suggestions aligned with their personality and learning style
            - Interview preparation checkpoints
            
            Structure output as a milestone tracker showing skills unlocked per module.
            """;

        var ragResponse = await _ragService.RetrieveAndGenerateAsync(
            query, resumeText, targetRole, industry, totalYearsOfExperience, ct);

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
        plan.FullName = fullName;
        plan.ContactInfo = contactInfo;
        plan.OriginalResumeText = resumeText;
        plan.TargetRole = targetRole;
        plan.Industry = industry;
        plan.TotalYearsOfExperience = totalYearsOfExperience;
        plan.YearsInTargetIndustry = yearsInTargetIndustry;
        plan.PersonalityType = personalityType;
        plan.Citations = ragResponse.Citations;

        // Persist to DynamoDB
        await _dynamoDb.SaveRoadmapAsync(plan, ct);

        // Create lesson records for tracking
        foreach (var module in plan.Modules)
        {
            foreach (var sprint in module.DailySprints)
            {
                var lesson = new LessonPlan
                {
                    UserId = userId,
                    ModuleId = $"week{module.WeekNumber}-day{sprint.Day}",
                    WeekNumber = module.WeekNumber,
                    DayNumber = sprint.Day,
                    Topic = sprint.Topic,
                    TargetRole = targetRole,
                    IsCompleted = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _dynamoDb.SaveLessonPlanAsync(lesson, ct);
            }
        }

        _logger.LogInformation(
            "28-day roadmap generated and saved for user {UserId}, role: {Role}",
            userId, targetRole);

        return plan;
    }
}

public class RoadmapPlan
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ContactInfo { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public int TotalYearsOfExperience { get; set; }
    public int YearsInTargetIndustry { get; set; }
    public string? PersonalityType { get; set; }
    public string? RawContent { get; set; }
    public string OriginalResumeText { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<WeeklyModule> Modules { get; set; } = [];
    public List<CitationSource> Citations { get; set; } = [];
    public List<string> SkillsIdentified { get; set; } = [];
    public List<string> SkillsToAcquire { get; set; } = [];
    public string SubscriptionTier { get; set; } = "Basic";
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
