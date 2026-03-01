using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public class ForumController : Controller
{
    private readonly ForumService _forumService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<ForumController> _logger;

    public ForumController(ForumService forumService, DynamoDbService dynamoDb, ILogger<ForumController> logger)
    {
        _forumService = forumService;
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? userId, CancellationToken ct)
    {
        RoadmapPlan? plan = null;
        if (!string.IsNullOrEmpty(userId))
            plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);

        // Guests see all questions, logged-in users see their industry
        var industry = plan?.Industry ?? "All";
        var questions = string.IsNullOrEmpty(userId) 
            ? await _forumService.GetAllQuestionsAsync(ct)
            : await _forumService.GetQuestionsByIndustryAsync(industry, ct);
        
        var userName = plan?.FullName?.Split('\n')[0]?.Trim() ?? "Guest";
        if (userName.Length > 50)
        {
            userName = "Guest";
        }
        
        ViewBag.UserId = userId ?? "";
        ViewBag.UserName = userName;
        ViewBag.Industry = industry;
        ViewBag.IsGuest = string.IsNullOrEmpty(userId) || plan is null;
        ViewBag.SubscriptionTier = plan?.SubscriptionTier ?? "Basic";
        return View(questions);
    }

    [HttpGet]
    public async Task<IActionResult> Question(string id, string? userId, CancellationToken ct)
    {
        var question = await _forumService.GetQuestionAsync(id, ct);
        if (question is null)
            return NotFound();

        await _forumService.IncrementViewCountAsync(id, ct);
        
        var answers = await _forumService.GetAnswersAsync(id, ct);
        
        RoadmapPlan? plan = null;
        if (!string.IsNullOrEmpty(userId))
            plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        
        var userName = plan?.FullName?.Split('\n')[0]?.Trim() ?? "Guest";
        if (userName.Length > 50)
        {
            userName = "Guest";
        }
        
        ViewBag.UserId = userId ?? "";
        ViewBag.UserName = userName;
        ViewBag.Answers = answers;
        ViewBag.IsGuest = string.IsNullOrEmpty(userId) || plan is null;
        ViewBag.SubscriptionTier = plan?.SubscriptionTier ?? "Basic";
        return View(question);
    }

    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> PostQuestion(
        string userId,
        string title,
        string content,
        List<IFormFile>? images,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("PostQuestion - UserId: {UserId}, Title: {Title}, Content length: {Length}", 
                userId, title, content?.Length ?? 0);
            _logger.LogInformation("Content preview: {Content}", content?.Substring(0, Math.Min(100, content?.Length ?? 0)));

            var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (plan is null)
                return Json(new { success = false, error = "User not found" });

            if (images != null && images.Count > 2)
                return Json(new { success = false, error = "Maximum 2 images allowed" });

            var imageUrls = new List<string>();
            if (images != null)
            {
                foreach (var image in images)
                {
                    using var stream = image.OpenReadStream();
                    var url = await _forumService.UploadImageAsync(stream, image.FileName, ct);
                    imageUrls.Add(url);
                }
            }

            var fullName = plan.FullName?.Split('\n')[0]?.Trim() ?? "Anonymous";
            
            // Extra safeguard: if name is too long, it's likely the entire resume
            if (fullName.Length > 50)
            {
                fullName = "User";
            }
            
            var question = new ForumQuestion
            {
                UserId = userId,
                UserName = fullName,
                Industry = plan.Industry,
                Title = title,
                Content = content,
                ImageUrls = imageUrls
            };

            var questionId = await _forumService.PostQuestionAsync(question, ct);
            
            // Moderate images in background
            if (imageUrls.Any())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var imageUrl in imageUrls)
                        {
                            using var httpClient = new HttpClient();
                            var imageStream = await httpClient.GetStreamAsync(imageUrl);
                            var (isAppropriate, _) = await _forumService.ModerateImageAsync(imageStream, CancellationToken.None);
                            
                            if (!isAppropriate)
                            {
                                await _forumService.DeleteQuestionAsync(questionId, CancellationToken.None);
                                _logger.LogWarning($"Question {questionId} deleted due to inappropriate image");
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error moderating images for question {questionId}");
                    }
                });
            }
            
            return Json(new { success = true, questionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error posting question");
            return Json(new { success = false, error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> PostAnswer(
        string questionId,
        string userId,
        string userName,
        string content,
        CancellationToken ct)
    {
        var answer = new ForumAnswer
        {
            QuestionId = questionId,
            UserId = userId,
            UserName = userName,
            Content = content
        };

        var answerId = await _forumService.PostAnswerAsync(answer, ct);
        return Json(new { success = true, answerId });
    }

    [HttpPost]
    public async Task<IActionResult> Vote(
        string answerId,
        string userId,
        bool isUpvote,
        CancellationToken ct)
    {
        await _forumService.VoteAnswerAsync(answerId, userId, isUpvote, ct);
        return Json(new { success = true });
    }

    [HttpPost]
    public async Task<IActionResult> AcceptAnswer(
        string questionId,
        string answerId,
        CancellationToken ct)
    {
        await _forumService.AcceptAnswerAsync(questionId, answerId, ct);
        return Json(new { success = true });
    }
}
