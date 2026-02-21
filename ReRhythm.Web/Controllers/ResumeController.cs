using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;

namespace ReRhythm.Web.Controllers;

public class ResumeController : Controller
{
    private readonly TextractService _textract;
    private readonly RoadmapService _roadmap;

    public ResumeController(TextractService textract, RoadmapService roadmap)
    {
        _textract = textract;
        _roadmap = roadmap;
    }

    [HttpGet]
    public IActionResult Upload() => View();

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        IFormFile resume,
        [FromForm] string targetRole,
        [FromForm] string userId,
        [FromForm] string industry,
        [FromForm] int yearsOfExperience,
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
            if (string.IsNullOrWhiteSpace(userId) || !System.Text.RegularExpressions.Regex.IsMatch(userId, @"^[a-zA-Z0-9-]+$"))
                return Json(new { success = false, error = "User ID must contain only letters, numbers, and hyphens." });

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
            userId, resumeText, targetRole, ct);

            return Json(new { success = true, redirectUrl = Url.Action("Analysis", new { userId }) });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, error = "Failed to process resume. Please ensure the file is a valid PDF or DOCX document." });
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
