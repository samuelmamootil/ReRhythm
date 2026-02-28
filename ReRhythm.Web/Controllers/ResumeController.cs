using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class ResumeController : Controller
{
    private readonly TextractService _textract;
    private readonly RoadmapService _roadmap;
    private readonly ILogger<ResumeController> _logger;

    public ResumeController(TextractService textract, RoadmapService roadmap, ILogger<ResumeController> logger)
    {
        _textract = textract;
        _roadmap = roadmap;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Upload() => View();

    [HttpGet]
    public async Task<IActionResult> CheckUserId(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Json(new { exists = false });

        var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
        var existingPlan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
        
        return Json(new { exists = existingPlan != null });
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        IFormFile resume,
        [FromForm] string targetRole,
        [FromForm] string userId,
        [FromForm] string industry,
        [FromForm] int yearsOfExperience,
        [FromForm] string? personalityType,
        CancellationToken ct)
    {
        try
        {
            if (resume is null || resume.Length == 0)
                return Json(new { success = false, error = "Please upload a valid resume file." });

            // Validate file type
            var allowedExtensions = new[] { ".pdf", ".docx" };
            var ext = Path.GetExtension(resume.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return Json(new { success = false, error = "Only PDF and DOCX files are allowed." });

            // Validate userId format
            if (string.IsNullOrWhiteSpace(userId) || !System.Text.RegularExpressions.Regex.IsMatch(userId, @"^[a-zA-Z0-9_-]+$"))
                return Json(new { success = false, error = "User ID must contain only letters, numbers, hyphens, and underscores." });

            // Check if userId already exists
            var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
            var existingPlan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (existingPlan != null)
                return Json(new { success = false, error = "User ID already exists. Please choose a different one." });

            // Check cache for same user + role
            if (existingPlan != null && existingPlan.TargetRole == targetRole)
            {
                return Json(new { success = true, redirectUrl = Url.Action("Analysis", new { userId }) });
            }

            await using var stream = resume.OpenReadStream();
            var resumeText = await _textract.UploadAndParseResumeAsync(
            stream, resume.FileName, userId, ct);

            var plan = await _roadmap.GenerateRoadmapAsync(
                userId, resumeText, targetRole, industry, yearsOfExperience, personalityType, ct);

            return Json(new { success = true, redirectUrl = Url.Action("Analysis", new { userId }) });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Resume upload failed for user {UserId}", userId);
            return Json(new { success = false, error = $"Failed to process resume: {ex.Message}" });
        }
    }

    [HttpGet]
    public IActionResult Analyzing()
    {
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Analysis(string userId, CancellationToken ct)
    {
        var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
        var plan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return RedirectToAction("Upload");
        return View(plan);
    }
}
