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
    public async Task<IActionResult> UpgradeUser(string userId, string tier, CancellationToken ct)
    {
        var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
        var plan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
        
        if (plan == null)
            return Json(new { success = false, error = "User not found" });
        
        if (tier != "Silver" && tier != "Gold")
            return Json(new { success = false, error = "Invalid tier selected" });
        
        plan.SubscriptionTier = tier;
        await dynamoDb.SaveRoadmapAsync(plan, ct);
        
        return Json(new { success = true, message = $"Upgraded to {tier} successfully!" });
    }
}
