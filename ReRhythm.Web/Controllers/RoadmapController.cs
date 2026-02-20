using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class RoadmapController : Controller
{
    private readonly RoadmapService _roadmapService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<RoadmapController> _logger;

    public RoadmapController(
        RoadmapService roadmapService,
        DynamoDbService dynamoDb,
        ILogger<RoadmapController> logger)
    {
        _roadmapService = roadmapService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    // GET /Roadmap/Index?userId=xxx
    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);

        if (plan is null)
            return RedirectToAction("Upload", "Resume");

        var completedCount = await _dynamoDb.GetCompletedLessonCountAsync(userId, ct);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);
        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;

        return View(plan);
    }

    // POST /Roadmap/Regenerate — force re-generate roadmap
    [HttpPost]
    public async Task<IActionResult> Regenerate(
        string userId,
        string resumeText,
        string targetRole,
        CancellationToken ct)
    {
        var plan = await _roadmapService.GenerateRoadmapAsync(
            userId, resumeText, targetRole, ct);

        return RedirectToAction("Index", new { userId });
    }

    // GET /Roadmap/Module?userId=xxx&weekNumber=1
    [HttpGet]
    public async Task<IActionResult> Module(
        string userId,
        int weekNumber,
        CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        var module = plan?.Modules.FirstOrDefault(m => m.WeekNumber == weekNumber);

        if (module is null)
            return NotFound("Module not found.");

        ViewBag.UserId = userId;
        return View(module);
    }

    // GET /Roadmap/Tracker?userId=xxx
    [HttpGet]
    public async Task<IActionResult> Tracker(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return RedirectToAction("Upload", "Resume");

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);

        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        ViewBag.AllLessons = allLessons;

        return View(plan);
    }

    // GET /Roadmap/DownloadResume?userId=xxx
    [HttpGet]
    public async Task<IActionResult> DownloadResume(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return NotFound("Roadmap not found.");

        var resumeGenerator = HttpContext.RequestServices.GetRequiredService<ResumeGeneratorService>();
        var pdfBytes = resumeGenerator.GenerateFutureReadyResume(plan, "Original resume content placeholder");

        return File(pdfBytes, "application/pdf", $"FutureReadyResume_{userId}_{DateTime.UtcNow:yyyyMMdd}.pdf");
    }
}
