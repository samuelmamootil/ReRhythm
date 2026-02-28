using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public partial class RoadmapController : Controller
{
    private readonly RoadmapService _roadmapService;
    private readonly DynamoDbService _dynamoDb;
    private readonly BadgeService _badgeService;
    private readonly ILogger<RoadmapController> _logger;

    public RoadmapController(
        RoadmapService roadmapService,
        DynamoDbService dynamoDb,
        BadgeService badgeService,
        ILogger<RoadmapController> logger)
    {
        _roadmapService = roadmapService;
        _dynamoDb = dynamoDb;
        _badgeService = badgeService;
        _logger = logger;
    }

    // GET /Roadmap/Index?userId=xxx&customSkills=skill1,skill2
    [HttpGet]
    public async Task<IActionResult> Index(string userId, string? customSkills, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);

        if (plan is null)
            return RedirectToAction("Upload", "Resume");

        // Only regenerate if user added custom skills
        if (!string.IsNullOrEmpty(customSkills))
        {
            var additionalSkills = customSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(5)
                .Where(s => !plan.SkillsToAcquire.Contains(s))
                .ToList();
            
            if (additionalSkills.Any())
            {
                _logger.LogInformation("Regenerating roadmap with custom skills: {Skills}", string.Join(", ", additionalSkills));
                plan = await _roadmapService.GenerateRoadmapAsync(
                    userId, plan.OriginalResumeText, plan.TargetRole, plan.Industry,
                    plan.TotalYearsOfExperience, plan.YearsInTargetIndustry,
                    plan.FullName, plan.ContactInfo, plan.PersonalityType,
                    string.Join(", ", additionalSkills), ct);
            }
        }

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);
        
        // Get badge count
        int badgeCount = 0;
        try
        {
            var userBadges = await _badgeService.GetUserBadgesAsync(userId, ct);
            badgeCount = userBadges.Count;
        }
        catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
        {
            badgeCount = 0;
        }
        
        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        ViewBag.AllLessons = allLessons;
        ViewBag.BadgeCount = badgeCount;

        return View(plan);
    }

    // POST /Roadmap/Regenerate — force re-generate roadmap
    [HttpPost]
    public async Task<IActionResult> Regenerate(
        string userId,
        string resumeText,
        string targetRole,
        string industry,
        int totalYearsOfExperience,
        int yearsInTargetIndustry,
        CancellationToken ct)
    {
        // Get existing plan to preserve personality type, name, and contact
        var existingPlan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        var personalityType = existingPlan?.PersonalityType;
        var fullName = existingPlan?.FullName ?? "";
        var contactInfo = existingPlan?.ContactInfo ?? "";

        var plan = await _roadmapService.GenerateRoadmapAsync(
            userId, resumeText, targetRole, industry, totalYearsOfExperience, yearsInTargetIndustry,
            fullName, contactInfo, personalityType, null, ct);

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
        
        // Check and award badges
        List<string> newBadges;
        try
        {
            newBadges = await _badgeService.CheckAndAwardBadgesAsync(userId, completedCount, progressPercent, ct);
        }
        catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
        {
            newBadges = new List<string>();
            _logger.LogWarning("Badge table not found. Deploy CloudFormation stack to enable badge tracking.");
        }
        
        _logger.LogInformation("Lesson marked complete. Progress: {Completed}/{Total} ({Percent}%). New badges: {BadgeCount}", 
            completedCount, totalLessons, progressPercent, newBadges.Count);
        
        return Json(new { success = true, completedCount, totalLessons, progressPercent, newBadges });
    }

    // GET /Roadmap/DownloadCertificate?userId=xxx
    [HttpGet]
    public async Task<IActionResult> DownloadCertificate(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return NotFound("Roadmap not found.");

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedLessons = allLessons.Where(l => l.IsCompleted).ToList();
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);

        // Check if user completed all lessons
        if (completedLessons.Count < totalLessons)
            return BadRequest("Complete all 28 lessons to earn your certificate.");

        var certificateService = HttpContext.RequestServices.GetRequiredService<CertificateService>();
        var pdfBytes = certificateService.GenerateCompletionCertificate(plan, completedLessons);

        return File(pdfBytes, "application/pdf", $"ReRhythm_Certificate_{userId}_{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    // GET /Roadmap/DownloadResume?userId=xxx
    [HttpGet]
    public async Task<IActionResult> DownloadResume(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return NotFound("Roadmap not found.");

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedLessons = allLessons.Where(l => l.IsCompleted).ToList();

        var resumeGenerator = HttpContext.RequestServices.GetRequiredService<ResumeGeneratorService>();
        var pdfBytes = resumeGenerator.GenerateFutureReadyResume(plan, plan.OriginalResumeText, completedLessons);

        return File(pdfBytes, "application/pdf", $"Resume_{userId}_{DateTime.UtcNow:yyyyMMdd}.pdf");
    }

    // GET /Roadmap/Verify/{userId} - Public certificate verification
    [HttpGet]
    public async Task<IActionResult> Verify(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return View((RoadmapPlan?)null);

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return View((RoadmapPlan?)null);

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);
        var completionDate = allLessons.Any(l => l.IsCompleted) ? allLessons.Where(l => l.IsCompleted).Max(l => l.CreatedAt) : (DateTime?)null;

        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.IsVerified = completedCount == totalLessons && totalLessons > 0;
        ViewBag.CompletionDate = completionDate;

        return View(plan);
    }
}
