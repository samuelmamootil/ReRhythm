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
    public async Task<IActionResult> Index(string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId))
            return RedirectToAction("Upload", "Resume");

        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return RedirectToAction("Upload", "Resume");

        var questions = await _forumService.GetQuestionsByIndustryAsync(plan.Industry, ct);
        
        ViewBag.UserId = userId;
        ViewBag.UserName = plan.FullName;
        ViewBag.Industry = plan.Industry;
        return View(questions);
    }

    [HttpGet]
    public async Task<IActionResult> Question(string id, string userId, CancellationToken ct)
    {
        var question = await _forumService.GetQuestionAsync(id, ct);
        if (question is null)
            return NotFound();

        var answers = await _forumService.GetAnswersAsync(id, ct);
        
        ViewBag.UserId = userId;
        ViewBag.Answers = answers;
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
                    var (isAppropriate, reason) = await _forumService.ModerateImageAsync(stream, ct);
                    
                    if (!isAppropriate)
                        return Json(new { success = false, error = reason });

                    stream.Position = 0;
                    var url = await _forumService.UploadImageAsync(stream, image.FileName, ct);
                    imageUrls.Add(url);
                }
            }

            var question = new ForumQuestion
            {
                UserId = userId,
                UserName = plan.FullName,
                Industry = plan.Industry,
                Title = title,
                Content = content,
                ImageUrls = imageUrls
            };

            var questionId = await _forumService.PostQuestionAsync(question, ct);
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
        string content,
        CancellationToken ct)
    {
        var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (plan is null)
            return Json(new { success = false, error = "User not found" });

        var answer = new ForumAnswer
        {
            QuestionId = questionId,
            UserId = userId,
            UserName = plan.FullName,
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
