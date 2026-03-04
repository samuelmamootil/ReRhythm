namespace ReRhythm.Core.Models;

public class ConnectionRequest
{
    public string ConnectionId { get; set; } = Guid.NewGuid().ToString();
    public string FromUserId { get; set; } = string.Empty;
    public string ToUserId { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Accepted, Rejected
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TargetRole { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public int CompletedLessons { get; set; }
    public int TotalLessons { get; set; }
    public DateTime JoinedAt { get; set; }
}
