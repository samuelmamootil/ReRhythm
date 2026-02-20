using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Models;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class LessonController : Controller
{
    private readonly BedrockRAGService _ragService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<LessonController> _logger;

    public LessonController(
        BedrockRAGService ragService,
        DynamoDbService dynamoDb,
        ILogger<LessonController> logger)
    {
        _ragService = ragService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    // GET /Lesson/Index?userId=xxx&moduleId=yyy
    [HttpGet]
    public async Task<IActionResult> Index(
        string userId,
        string moduleId,
        CancellationToken ct)
    {
        var lesson = await _dynamoDb.GetLessonPlanAsync(userId, moduleId, ct);

        if (lesson is null)
            return RedirectToAction("Generate", new { userId, moduleId });

        return View(lesson);
    }

    // POST /Lesson/Generate — create a new 15-min lesson via RAG
    [HttpPost]
    public async Task<IActionResult> Generate(
        string userId,
        string moduleId,
        string topic,
        string targetRole,
        CancellationToken ct)
    {
        var query = $"""
            Create a 15-minute lesson on: {topic}
            For someone targeting: {targetRole}
            
            Include:
            - 5 flashcards (term/definition format from AWS docs)
            - 3 quiz questions with 4 options each and explanations
            - 1 mini hands-on lab description (15 minutes, AWS Console or CLI)
            - Official AWS documentation URL for this topic
            
            Output as structured JSON.
            """;

        var ragResponse = await _ragService.RetrieveAndGenerateAsync(
            query, string.Empty, targetRole, ct);

        // Parse response into LessonPlan
        var lesson = new LessonPlan
        {
            UserId = userId,
            ModuleId = moduleId,
            Topic = topic,
            TargetRole = targetRole,
            MiniLabDescription = ragResponse.GeneratedText,
            CreatedAt = DateTime.UtcNow
        };

        await _dynamoDb.SaveLessonPlanAsync(lesson, ct);

        _logger.LogInformation(
            "Lesson generated for user {UserId}, topic: {Topic}", userId, topic);

        return RedirectToAction("Index", new { userId, moduleId });
    }

    // POST /Lesson/Complete — mark lesson as done
    [HttpPost]
    public async Task<IActionResult> Complete(
        string userId,
        string moduleId,
        CancellationToken ct)
    {
        await _dynamoDb.MarkLessonCompleteAsync(userId, moduleId, ct);
        return Ok(new { success = true });
    }

    // GET /Lesson/Milestones?userId=xxx
    [HttpGet]
    public async Task<IActionResult> Milestones(string userId, CancellationToken ct)
    {
        var roadmap = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        return View(roadmap);
    }
}
