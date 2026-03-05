using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class MentoringController : Controller
{
    private readonly DynamoDbService _dynamoDb;

    public MentoringController(DynamoDbService dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return RedirectToAction("Upload", "Resume");

        // Gold tier only
        if (plan.SubscriptionTier != "Gold")
            return RedirectToAction("Upgrade", "Premium");

        ViewBag.UserId = userId;
        ViewBag.TargetRole = plan.TargetRole;
        ViewBag.Industry = plan.Industry;
        ViewBag.SubscriptionTier = plan.SubscriptionTier ?? "Basic";
        
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> BookSession(string userId, string mentorId, string sessionType, string preferredTime, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan?.SubscriptionTier != "Gold")
            return Json(new { success = false, error = "Gold subscription required" });

        // In a real implementation, this would integrate with a scheduling system
        return Json(new { 
            success = true, 
            message = "Session booking request sent! You'll receive a confirmation email within 24 hours.",
            sessionId = Guid.NewGuid().ToString()
        });
    }
}