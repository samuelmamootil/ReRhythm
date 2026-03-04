namespace ReRhythm.Core.Models;

public class ForumQuestion
{
    public string QuestionId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? AcceptedAnswerId { get; set; }
    public int ViewCount { get; set; }
}

public class ForumAnswer
{
    public string AnswerId { get; set; } = Guid.NewGuid().ToString();
    public string QuestionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public List<string> UpvotedBy { get; set; } = new();
    public List<string> DownvotedBy { get; set; } = new();
}

public class ForumNotification
{
    public string NotificationId { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "answer", "vote", "accepted", "violation"
    public string Message { get; set; } = string.Empty;
    public string? QuestionId { get; set; }
    public string? AnswerId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; } = false;
}
