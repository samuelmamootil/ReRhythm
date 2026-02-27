using Microsoft.Extensions.Logging;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class BadgeService
{
    private readonly DynamoDbService _dynamoDb;
    private readonly ILogger<BadgeService> _logger;

    public BadgeService(DynamoDbService dynamoDb, ILogger<BadgeService> logger)
    {
        _dynamoDb = dynamoDb;
        _logger = logger;
    }

    private static readonly List<BadgeDefinition> BadgeDefinitions = new()
    {
        new BadgeDefinition { Id = "first-step", Name = "First Step", Icon = "🚀", RequiredLessons = 1 },
        new BadgeDefinition { Id = "week-warrior", Name = "Week Warrior", Icon = "⚡", RequiredLessons = 7 },
        new BadgeDefinition { Id = "halfway-hero", Name = "Halfway Hero", Icon = "🎯", RequiredPercent = 50 },
        new BadgeDefinition { Id = "skill-builder", Name = "Skill Builder", Icon = "🛠️", RequiredLessons = 14 },
        new BadgeDefinition { Id = "sprint-master", Name = "Sprint Master", Icon = "🏃", RequiredLessons = 21 },
        new BadgeDefinition { Id = "graduate", Name = "Graduate", Icon = "🎓", RequiredPercent = 100 }
    };

    public async Task<List<string>> CheckAndAwardBadgesAsync(
        string userId, 
        int completedCount, 
        int progressPercent,
        CancellationToken ct = default)
    {
        var newBadges = new List<string>();

        foreach (var def in BadgeDefinitions)
        {
            // Check if badge should be unlocked
            bool shouldUnlock = def.RequiredLessons.HasValue 
                ? completedCount >= def.RequiredLessons.Value
                : progressPercent >= def.RequiredPercent!.Value;

            if (!shouldUnlock) continue;

            // Check if user already has this badge
            var hasBadge = await _dynamoDb.HasBadgeAsync(userId, def.Id, ct);
            if (hasBadge) continue;

            // Award new badge
            var badge = new BadgeAchievement
            {
                UserId = userId,
                BadgeId = def.Id,
                BadgeName = def.Name,
                BadgeIcon = def.Icon,
                UnlockedAt = DateTime.UtcNow,
                LessonsCompletedAtUnlock = completedCount,
                ProgressPercentAtUnlock = progressPercent
            };

            await _dynamoDb.SaveBadgeAchievementAsync(badge, ct);
            newBadges.Add(def.Name);
            
            _logger.LogInformation("Badge unlocked: {BadgeName} for user {UserId}", def.Name, userId);
        }

        return newBadges;
    }

    public async Task<List<BadgeAchievement>> GetUserBadgesAsync(string userId, CancellationToken ct = default)
    {
        return await _dynamoDb.GetUserBadgesAsync(userId, ct);
    }

    private class BadgeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int? RequiredLessons { get; set; }
        public int? RequiredPercent { get; set; }
    }
}
