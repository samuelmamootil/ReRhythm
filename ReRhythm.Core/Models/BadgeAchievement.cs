namespace ReRhythm.Core.Models;

public class BadgeAchievement
{
    public string UserId { get; set; } = string.Empty;
    public string BadgeId { get; set; } = string.Empty;
    public string BadgeName { get; set; } = string.Empty;
    public string BadgeIcon { get; set; } = string.Empty;
    public DateTime UnlockedAt { get; set; }
    public int LessonsCompletedAtUnlock { get; set; }
    public int ProgressPercentAtUnlock { get; set; }
}
