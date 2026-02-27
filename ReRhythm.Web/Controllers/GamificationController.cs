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
        
        // Try to get badges, but don't fail if table doesn't exist yet
        List<BadgeAchievement> userBadges;
        try
        {
            userBadges = await _badgeService.GetUserBadgesAsync(userId);
        }
        catch (Amazon.DynamoDBv2.Model.ResourceNotFoundException)
        {
            userBadges = new List<BadgeAchievement>();
        }

        ViewBag.UserId = userId;
        ViewBag.CompletedCount = completedCount;
        ViewBag.TotalLessons = allLessons.Count;
        ViewBag.ProgressPercent = allLessons.Count > 0 ? (int)((completedCount / (double)allLessons.Count) * 100) : 0;
        ViewBag.UserBadges = userBadges;

        return View(roadmap);
    }
}
