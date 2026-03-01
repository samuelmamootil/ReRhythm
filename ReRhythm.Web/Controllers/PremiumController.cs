using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class PremiumController : Controller
{
    [HttpGet]
    public IActionResult Upgrade()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> UpgradeUser(string userId, CancellationToken ct)
    {
        var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
        var plan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
        
        if (plan == null)
            return Json(new { success = false, error = "User not found" });
        
        plan.SubscriptionTier = "Gold";
        await dynamoDb.SaveRoadmapAsync(plan, ct);
        
        return Json(new { success = true, message = "Upgraded to Gold successfully!" });
    }
}
