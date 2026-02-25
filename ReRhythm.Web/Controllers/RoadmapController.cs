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

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);
        
        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        ViewBag.AllLessons = allLessons;

        return View(plan);
    }

    // POST /Roadmap/Regenerate — force re-generate roadmap
    [HttpPost]
    public async Task<IActionResult> Regenerate(
        string userId,
        string resumeText,
        string targetRole,
        string industry,
        int yearsOfExperience,
        CancellationToken ct)
    {
        var plan = await _roadmapService.GenerateRoadmapAsync(
            userId, resumeText, targetRole, industry, yearsOfExperience, ct);

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

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        ViewBag.UserId = userId;
        ViewBag.AllLessons = allLessons;
        return View(module);
    }

    // GET /Roadmap/Tracker?userId=xxx
    [HttpGet]
    public async Task<IActionResult> Tracker(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("FindUser");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return RedirectToAction("FindUser");

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);

        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        ViewBag.AllLessons = allLessons;

        return View(plan);
    }

    // GET /Roadmap/FindUser
    [HttpGet]
    public IActionResult FindUser()
    {
        return View();
    }

    // POST /Roadmap/FindUser
    [HttpPost]
    public async Task<IActionResult> FindUser(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Json(new { success = false, error = "Please enter a User ID." });

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return Json(new { success = false, error = "User ID not found. Please upload a resume first." });

        return Json(new { success = true, redirectUrl = Url.Action("Tracker", new { userId }) });
    }

    // POST /Roadmap/CompleteLesson
    [HttpPost]
    public async Task<IActionResult> CompleteLesson(string userId, int weekNumber, int dayNumber, CancellationToken ct)
    {
        var moduleId = $"week{weekNumber}-day{dayNumber}";
        _logger.LogInformation("Marking lesson complete: userId={UserId}, moduleId={ModuleId}", userId, moduleId);
        
        await _dynamoDb.MarkLessonCompleteAsync(userId, moduleId, ct);
        
        var completedCount = await _dynamoDb.GetCompletedLessonCountAsync(userId, ct);
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        var totalLessons = plan?.Modules.Sum(m => m.DailySprints.Count) ?? 0;
        var progressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        
        _logger.LogInformation("Lesson marked complete. Progress: {Completed}/{Total} ({Percent}%)", 
            completedCount, totalLessons, progressPercent);
        
        return Json(new { success = true, completedCount, totalLessons, progressPercent });
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
