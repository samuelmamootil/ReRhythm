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
        ResumeData? resumeData = null,
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
        var jsonText = ragResponse.GeneratedText;
        try
        {
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
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse roadmap JSON. Raw response: {Response}", jsonText);
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
        plan.ParsedResumeData = resumeData;
        
        // Parse email and phone from ContactInfo
        var emailMatch = System.Text.RegularExpressions.Regex.Match(contactInfo, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
        plan.Email = emailMatch.Success ? emailMatch.Value : string.Empty;
        
        var phoneMatch = System.Text.RegularExpressions.Regex.Match(contactInfo, @"\+?[1-9]\d{0,3}[\s\-]?\(?\d{1,4}\)?[\s\-]?\d{1,4}[\s\-]?\d{1,9}");
        plan.PhoneNumber = phoneMatch.Success ? phoneMatch.Value.Trim() : string.Empty;
        
        plan.OriginalResumeText = resumeText;
        plan.TargetRole = targetRole;
        plan.Industry = industry;
        plan.TotalYearsOfExperience = totalYearsOfExperience;
        plan.YearsInTargetIndustry = yearsInTargetIndustry;
        plan.PersonalityType = personalityType;
        plan.Citations = ragResponse.Citations ?? new List<CitationSource>();
        plan.Modules = plan.Modules ?? new List<WeeklyModule>();
        plan.SkillsIdentified = plan.SkillsIdentified ?? new List<string>();
        plan.SkillsToAcquire = plan.SkillsToAcquire ?? new List<string>();

        // Persist to DynamoDB
        await _dynamoDb.SaveRoadmapAsync(plan, ct);

        // Create lesson records for tracking
        if (plan.Modules != null)
        {
            foreach (var module in plan.Modules)
            {
                if (module.DailySprints == null) continue;
                
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
        }

        _logger.LogInformation(
            "28-day roadmap generated and saved for user {UserId}, role: {Role}",
            userId, targetRole);

        return plan;
    }

    public async Task<List<string>> GenerateAdvancedTopicsAsync(
        string targetRole,
        string industry,
        List<string> completedSkills,
        CancellationToken ct = default)
    {
        var prompt = $@"Based on a {targetRole} in {industry} who has completed these skills: {string.Join(", ", completedSkills)}

Generate exactly 4 advanced learning topics for continued growth. Topics should be:
- Specific to {targetRole} career advancement
- Build upon completed skills
- Industry-relevant for {industry}
- Focused on senior/leadership level concepts

Respond with ONLY a JSON array of 4 topic strings, no other text:
[""topic 1"", ""topic 2"", ""topic 3"", ""topic 4""]";

        try
        {
            var response = await _ragService.GenerateSimpleResponseAsync(prompt, ct);
            _logger.LogInformation("AI Response for advanced topics: {Response}", response);
            
            var topics = JsonSerializer.Deserialize<List<string>>(response.Trim());
            return topics ?? new List<string> { "System Design Patterns", "Leadership & Mentoring", "Advanced Architecture", "Performance Optimization" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate advanced topics for {Role} in {Industry}", targetRole, industry);
            return new List<string> { "System Design Patterns", "Leadership & Mentoring", "Advanced Architecture", "Performance Optimization" };
        }
    }
}

public class RoadmapPlan
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string ContactInfo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
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
    
    // Store complete parsed resume data
    public ResumeData? ParsedResumeData { get; set; }
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
