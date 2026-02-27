using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using System.Text;

namespace ReRhythm.Web.Controllers;

public partial class RoadmapController
{
    [HttpGet]
    public async Task<IActionResult> DownloadEnhancedResume(string userId, CancellationToken ct)
    {
        try
        {
            var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (plan == null)
                return NotFound("Roadmap not found");

            var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
            var completedCount = allLessons.Count(l => l.IsCompleted);
            var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);

            if (completedCount < totalLessons)
                return BadRequest("Complete all 28 lessons to unlock enhanced resume");

            var skillsLearned = plan.SkillsToAcquire ?? new List<string>();
            var completedProjects = plan.Modules?
                .Where(m => !string.IsNullOrEmpty(m.PortfolioProject))
                .Select(m => m.PortfolioProject!)
                .ToList() ?? new List<string>();

            var resumeBuilder = HttpContext.RequestServices.GetRequiredService<ResumeBuilderService>();
            var enhancedResume = await resumeBuilder.GenerateEnhancedResumeAsync(
                plan.OriginalResumeText ?? "No original resume",
                skillsLearned,
                plan.TargetRole ?? "Software Engineer",
                plan.Industry ?? "Technology",
                plan.YearsOfExperience,
                completedProjects
            );

            var bytes = Encoding.UTF8.GetBytes(enhancedResume);
            return File(bytes, "text/plain", $"ReRhythm_Enhanced_Resume_{userId}.txt");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating enhanced resume for user {UserId}", userId);
            return StatusCode(500, "Error generating resume");
        }
    }
}
