using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;

namespace ReRhythm.Web.Controllers;

public class CommunityController : Controller
{
    private readonly ForumService _forumService;
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<CommunityController> _logger;

    public CommunityController(ForumService forumService, DynamoDbService dynamoDb, ILogger<CommunityController> logger)
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

        // Get community stats if user completed 28 lessons
        int? totalUsers = null;
        int? completedUsers = null;
        int? referralCount = null;
        int? discountPercent = null;
        bool? isFree = null;
        string? referralLink = null;

        if (plan != null)
        {
            var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId!, ct);
            var completedCount = allLessons.Count(l => l.IsCompleted);
            var totalLessons = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;
            
            if (completedCount >= totalLessons)
            {
                totalUsers = await _dynamoDb.GetTotalUsersCountAsync(ct);
                completedUsers = await _dynamoDb.GetCompletedUsersCountAsync(ct);
                referralCount = await _dynamoDb.GetReferralCountAsync(userId!, ct);
                discountPercent = Math.Min(referralCount.Value * 10, 100);
                isFree = referralCount >= 5;
                referralLink = $"https://rerhythm.com/Resume/Upload?ref={userId}";
            }
        }
        
        ViewBag.UserId = userId ?? "";
        ViewBag.UserName = userName;
        ViewBag.Industry = industry;
        ViewBag.TargetRole = plan?.TargetRole ?? "";
        ViewBag.IsGuest = string.IsNullOrEmpty(userId) || plan is null;
        ViewBag.SubscriptionTier = plan?.SubscriptionTier ?? "Basic";
        ViewBag.TotalUsers = totalUsers;
        ViewBag.CompletedUsers = completedUsers;
        ViewBag.ReferralCount = referralCount;
        ViewBag.DiscountPercent = discountPercent;
        ViewBag.IsFree = isFree;
        ViewBag.ReferralLink = referralLink;
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
            
            // Moderate in background - delete if inappropriate
            _ = Task.Run(async () =>
            {
                try
                {
                    var (titleOk, titleReason) = await _forumService.ModerateTextAsync(title, CancellationToken.None);
                    if (!titleOk)
                    {
                        await _forumService.DeleteQuestionAsync(questionId, CancellationToken.None);
                        await _forumService.NotifyViolationAsync(userId, "question", titleReason, CancellationToken.None);
                        _logger.LogWarning("Question {QuestionId} deleted - {Reason}", questionId, titleReason);
                        return;
                    }
                    
                    var (contentOk, contentReason) = await _forumService.ModerateTextAsync(content, CancellationToken.None);
                    if (!contentOk)
                    {
                        await _forumService.DeleteQuestionAsync(questionId, CancellationToken.None);
                        await _forumService.NotifyViolationAsync(userId, "question", contentReason, CancellationToken.None);
                        _logger.LogWarning("Question {QuestionId} deleted - {Reason}", questionId, contentReason);
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
        
        // Moderate in background - delete if inappropriate
        _ = Task.Run(async () =>
        {
            try
            {
                var (isOk, reason) = await _forumService.ModerateTextAsync(content, CancellationToken.None);
                if (!isOk)
                {
                    await _forumService.DeleteAnswerAsync(answerId, CancellationToken.None);
                    await _forumService.NotifyViolationAsync(userId, "answer", reason, CancellationToken.None);
                    _logger.LogWarning("Answer {AnswerId} deleted - {Reason}", answerId, reason);
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

    [HttpPost]
    public async Task<IActionResult> CleanupInappropriatePosts(CancellationToken ct)
    {
        var deletedCount = await _forumService.CleanupInappropriatePostsAsync(ct);
        return Json(new { success = true, deletedCount });
    }
}
