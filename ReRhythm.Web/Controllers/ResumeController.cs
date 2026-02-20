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
        CancellationToken ct)
    {
        if (resume is null || resume.Length == 0)
            return BadRequest("Please upload a valid PDF resume.");

        var allowedExtensions = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".tiff", ".tif" };
        var ext = Path.GetExtension(resume.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest($"Unsupported file format. Please upload: {string.Join(", ", allowedExtensions)}");

        if (string.IsNullOrWhiteSpace(userId))
            userId = Guid.NewGuid().ToString();

        // Check cache first
        var dynamoDb = HttpContext.RequestServices.GetRequiredService<DynamoDbService>();
        var cachedPlan = await dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (cachedPlan != null && cachedPlan.TargetRole == targetRole)
        {
            return RedirectToAction("Analysis", new { userId });
        }

        // Show loading page
        TempData["UserId"] = userId;
        TempData["TargetRole"] = targetRole;
        TempData["FileName"] = resume.FileName;
        
        // Process in background would be ideal, but for now process synchronously
        await using var stream = resume.OpenReadStream();
        var resumeText = await _textract.UploadAndParseResumeAsync(
        stream, resume.FileName, userId, ct);

        var plan = await _roadmap.GenerateRoadmapAsync(
        userId, resumeText, targetRole, ct);

        return RedirectToAction("Analysis", new { userId });
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
