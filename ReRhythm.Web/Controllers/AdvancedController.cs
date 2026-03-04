using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class AdvancedController : Controller
{
    private readonly DynamoDbService _dynamoDb;
    private readonly RoadmapService _roadmapService;

    public AdvancedController(DynamoDbService dynamoDb, RoadmapService roadmapService)
    {
        _dynamoDb = dynamoDb;
        _roadmapService = roadmapService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return RedirectToAction("Upload", "Resume");

        // Check if user completed 28 lessons
        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;
        
        if (completedCount < totalLessons)
            return RedirectToAction("Tracker", "Roadmap", new { userId });

        // Generate advanced topics
        var topics = await _roadmapService.GenerateAdvancedTopicsAsync(
            plan.TargetRole, 
            plan.Industry, 
            plan.SkillsToAcquire, 
            ct);

        ViewBag.UserId = userId;
        ViewBag.TargetRole = plan.TargetRole;
        ViewBag.Industry = plan.Industry;
        ViewBag.Topics = topics;
        ViewBag.SubscriptionTier = plan.SubscriptionTier ?? "Basic";

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Topic(string userId, string topicName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(topicName))
            return RedirectToAction("Index", new { userId });

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return RedirectToAction("Upload", "Resume");

        ViewBag.UserId = userId;
        ViewBag.TopicName = topicName;
        ViewBag.TargetRole = plan.TargetRole;
        ViewBag.Industry = plan.Industry;
        ViewBag.SubscriptionTier = plan.SubscriptionTier ?? "Basic";

        return View();
    }
}
