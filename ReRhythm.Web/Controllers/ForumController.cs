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
            if (fullName.Length > 50)
                fullName = "User";
            
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
            
            // Moderate content in background - delete if abuse detected
            _ = Task.Run(async () =>
            {
                try
                {
                    // Run text and image moderation in parallel for speed
                    var textTask = Task.Run(async () =>
                    {
                        var (titleOk, titleReason) = await _forumService.ModerateTextAsync(title, CancellationToken.None);
                        if (!titleOk) return (false, titleReason);
                        
                        var (contentOk, contentReason) = await _forumService.ModerateTextAsync(content, CancellationToken.None);
                        return contentOk ? (true, string.Empty) : (false, contentReason);
                    });

                    var imageTask = Task.Run(async () =>
                    {
                        if (!imageUrls.Any()) return (true, string.Empty);
                        
                        using var httpClient = new HttpClient();
                        foreach (var imageUrl in imageUrls)
                        {
                            var imageStream = await httpClient.GetStreamAsync(imageUrl);
                            var (isAppropriate, reason) = await _forumService.ModerateImageAsync(imageStream, CancellationToken.None);
                            if (!isAppropriate)
                                return (false, reason);
                        }
                        return (true, string.Empty);
                    });

                    // Wait for both to complete
                    await Task.WhenAll(textTask, imageTask);
                    
                    var textResult = await textTask;
                    var imageResult = await imageTask;
                    
                    // Delete if either failed
                    if (!textResult.Item1)
                    {
                        await _forumService.DeleteQuestionAsync(questionId, CancellationToken.None);
                        await _forumService.NotifyViolationAsync(userId, "question", textResult.Item2, CancellationToken.None);
                        _logger.LogWarning("Question {QuestionId} deleted - Text: {Reason}", questionId, textResult.Item2);
                    }
                    else if (!imageResult.Item1)
                    {
                        await _forumService.DeleteQuestionAsync(questionId, CancellationToken.None);
                        await _forumService.NotifyViolationAsync(userId, "question", imageResult.Item2, CancellationToken.None);
                        _logger.LogWarning("Question {QuestionId} deleted - Image: {Reason}", questionId, imageResult.Item2);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error moderating question {QuestionId}", questionId);
                }
            });
            
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
        
        // Moderate answer in background - delete if abuse detected
        _ = Task.Run(async () =>
        {
            try
            {
                var (isOk, reason) = await _forumService.ModerateTextAsync(content, CancellationToken.None);
                if (!isOk)
                {
                    await _forumService.DeleteAnswerAsync(answerId, CancellationToken.None);
                    await _forumService.NotifyViolationAsync(userId, "answer", reason, CancellationToken.None);
                    _logger.LogWarning("Answer {AnswerId} deleted - Moderation failed: {Reason}", answerId, reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moderating answer {AnswerId}", answerId);
            }
        });
        
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

    [HttpGet]
    public async Task<IActionResult> GetNotifications(string userId, CancellationToken ct)
    {
        try
        {
            var notifications = await _forumService.GetUserNotificationsAsync(userId, ct);
            return Json(notifications);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notifications for user {UserId}", userId);
            return Json(new List<object>());
        }
    }

    [HttpPost]
    public async Task<IActionResult> MarkNotificationRead(string notificationId, CancellationToken ct)
    {
        await _forumService.MarkNotificationReadAsync(notificationId, ct);
        return Json(new { success = true });
    }
}
