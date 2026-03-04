using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class JobController : Controller
{
    private readonly DynamoDbService _dynamoDb;

    public JobController(DynamoDbService dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<IActionResult> Search(string userId)
    {
        var roadmap = await _dynamoDb.GetLatestRoadmapAsync(userId);
        if (roadmap == null)
            return RedirectToAction("Upload", "Resume");

        // Gold tier check
        if (roadmap.SubscriptionTier != "Gold")
            return RedirectToAction("Upgrade", "Premium");

        ViewBag.UserId = userId;
        return View(roadmap);
    }
}
