using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class InsightsController : Controller
{
    private readonly DynamoDbService _dynamoDb;
    private readonly AnalyticsService _analytics;

    public InsightsController(DynamoDbService dynamoDb, AnalyticsService analytics)
    {
        _dynamoDb = dynamoDb;
        _analytics = analytics;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null)
            return RedirectToAction("Upload", "Resume");

        ViewBag.UserId = userId;
        ViewBag.SubscriptionTier = plan.SubscriptionTier ?? "Basic";

        // Check if Gold tier
        if (plan.SubscriptionTier != "Gold")
        {
            return View("PremiumRequired");
        }

        // Get all insights
        var industryInsights = await _analytics.GetIndustryInsightsAsync(plan.Industry, ct);
        var skillGap = await _analytics.GetSkillGapAnalysisAsync(userId, ct);
        var resumeScore = await _analytics.CalculateResumeScoreAsync(plan, ct);
        var salaryInsights = _analytics.GetSalaryInsights(plan.TargetRole, plan.Industry, plan.TotalYearsOfExperience);

        ViewBag.IndustryInsights = industryInsights;
        ViewBag.SkillGap = skillGap;
        ViewBag.ResumeScore = resumeScore;
        ViewBag.SalaryInsights = salaryInsights;
        ViewBag.TargetRole = plan.TargetRole;
        ViewBag.Industry = plan.Industry;

        return View(plan);
    }
}
