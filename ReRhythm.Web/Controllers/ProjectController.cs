using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public class ProjectController : Controller
{
    private readonly ProjectService _projectService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(ProjectService projectService, DynamoDbService dynamoDb, ILogger<ProjectController> logger)
    {
        _projectService = projectService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan == null) return RedirectToAction("Upload", "Resume");

        var projects = await _projectService.GetProjectsByIndustryAsync(plan.Industry, ct);
        var reputation = await _projectService.GetUserReputationAsync(userId, ct);

        ViewBag.UserId = userId;
        ViewBag.UserName = plan.FullName?.Split('\n')[0]?.Trim() ?? "User";
        ViewBag.Industry = plan.Industry;
        ViewBag.Reputation = reputation;
        
        return View(projects);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id, string userId, CancellationToken ct)
    {
        var project = await _projectService.GetProjectAsync(id, ct);
        if (project == null) return NotFound();

        await _projectService.IncrementViewCountAsync(id, ct);
        var reviews = await _projectService.GetReviewsAsync(id, ct);

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        ViewBag.UserId = userId;
        ViewBag.UserName = plan?.FullName?.Split('\n')[0]?.Trim() ?? "Guest";
        ViewBag.Reviews = reviews;
        ViewBag.IsOwner = project.UserId == userId;
        
        return View(project);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Create(
        string userId,
        string title,
        string description,
        string techStack,
        string githubUrl,
        string liveUrl,
        List<IFormFile>? images,
        CancellationToken ct)
    {
        try
        {
            var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (plan == null) return Json(new { success = false, error = "User not found" });

            if (images != null && images.Count > 3)
                return Json(new { success = false, error = "Maximum 3 images allowed" });

            var imageUrls = new List<string>();
            if (images != null)
            {
                foreach (var image in images)
                {
                    using var stream = image.OpenReadStream();
                    var url = await _projectService.UploadImageAsync(stream, image.FileName, ct);
                    imageUrls.Add(url);
                }
            }

            var project = new Project
            {
                UserId = userId,
                UserName = plan.FullName?.Split('\n')[0]?.Trim() ?? "User",
                Title = title,
                Description = description,
                TechStack = techStack,
                GithubUrl = githubUrl ?? "",
                LiveUrl = liveUrl ?? "",
                ImageUrls = imageUrls,
                Industry = plan.Industry
            };

            var projectId = await _projectService.CreateProjectAsync(project, ct);
            return Json(new { success = true, projectId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> AddReview(
        string projectId,
        string reviewerId,
        string reviewerName,
        int rating,
        string comment,
        CancellationToken ct)
    {
        try
        {
            var review = new ProjectReview
            {
                ProjectId = projectId,
                ReviewerId = reviewerId,
                ReviewerName = reviewerName,
                Rating = rating,
                Comment = comment
            };

            await _projectService.AddReviewAsync(review, ct);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding review");
            return Json(new { success = false, error = ex.Message });
        }
    }
}
