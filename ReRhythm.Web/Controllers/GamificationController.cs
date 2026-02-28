using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public class GamificationController : Controller
{
    private readonly DynamoDbService _dynamoDb;
    private readonly BadgeService _badgeService;

    public GamificationController(DynamoDbService dynamoDb, BadgeService badgeService)
    {
        _dynamoDb = dynamoDb;
        _badgeService = badgeService;
    }

    public async Task<IActionResult> Dashboard(string userId)
    {
        var roadmap = await _dynamoDb.GetLatestRoadmapAsync(userId);
        if (roadmap == null)
            return RedirectToAction("Upload", "Resume");

        var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId);
        var completedCount = allLessons.Count(l => l.IsCompleted);
        var totalLessons = roadmap.Modules.Sum(m => m.DailySprints.Count);
        var progressPercent = totalLessons > 0 ? (int)((completedCount / (double)totalLessons) * 100) : 0;
        
        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = totalLessons;
        ViewBag.ProgressPercent = progressPercent;

        return View(roadmap);
    }
}
