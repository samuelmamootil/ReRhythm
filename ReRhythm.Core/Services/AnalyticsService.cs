using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class AnalyticsService
{
    private readonly DynamoDbService _dynamoDb;

    public AnalyticsService(DynamoDbService dynamoDb)
    {
        _dynamoDb = dynamoDb;
    }

    public async Task<ResumeQualityScore> CalculateResumeScoreAsync(RoadmapPlan plan, CancellationToken ct = default)
    {
        if (plan == null)
        {
            return new ResumeQualityScore
            {
                Score = 0,
                Strengths = new List<string>(),
                Improvements = new List<string> { "No roadmap found" },
                SkillCount = 0,
                ExperienceYears = 0,
                ProgressPercent = 0
            };
        }

        var score = 0;
        var feedback = new List<string>();
        var strengths = new List<string>();

        // Skills count (max 20 points)
        var skillCount = plan.SkillsIdentified?.Count ?? 0;
        if (skillCount >= 15) { score += 20; strengths.Add("Strong skill portfolio"); }
        else if (skillCount >= 10) { score += 15; strengths.Add("Good skill variety"); }
        else if (skillCount >= 5) { score += 10; }
        else feedback.Add("Add more skills to your resume");

        // Experience (max 20 points)
        if (plan.TotalYearsOfExperience >= 5) { score += 20; strengths.Add("Solid experience level"); }
        else if (plan.TotalYearsOfExperience >= 2) { score += 15; }
        else if (plan.TotalYearsOfExperience >= 1) { score += 10; }
        else score += 5;

        // Industry alignment (max 15 points)
        if (plan.YearsInTargetIndustry >= 3) { score += 15; strengths.Add("Industry-specific experience"); }
        else if (plan.YearsInTargetIndustry >= 1) { score += 10; }
        else { score += 5; feedback.Add("Gain more industry-specific experience"); }

        // Resume completeness (max 15 points)
        if (!string.IsNullOrEmpty(plan.FullName)) score += 5;
        else feedback.Add("Add your full name");
        if (!string.IsNullOrEmpty(plan.ContactInfo)) score += 5;
        else feedback.Add("Add contact information");
        if (!string.IsNullOrEmpty(plan.OriginalResumeText) && plan.OriginalResumeText.Length > 500) { score += 5; strengths.Add("Detailed resume content"); }
        else feedback.Add("Expand resume with more details");

        // Roadmap progress (max 30 points)
        var completedLessons = await _dynamoDb.GetCompletedLessonsCountAsync(plan.UserId, ct);
        var totalLessons = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;
        var progressPercent = totalLessons > 0 ? (completedLessons * 100 / totalLessons) : 0;
        
        if (progressPercent >= 75) { score += 30; strengths.Add("Excellent learning progress"); }
        else if (progressPercent >= 50) { score += 20; strengths.Add("Good learning momentum"); }
        else if (progressPercent >= 25) { score += 10; }
        else feedback.Add("Complete more lessons to boost your score");

        return new ResumeQualityScore
        {
            Score = score,
            Strengths = strengths,
            Improvements = feedback,
            SkillCount = skillCount,
            ExperienceYears = plan.TotalYearsOfExperience,
            ProgressPercent = progressPercent
        };
    }

    public async Task<IndustryInsights> GetIndustryInsightsAsync(string industry, CancellationToken ct = default)
    {
        var allPlans = await _dynamoDb.GetAllRoadmapsAsync(ct);
        var industryPlans = allPlans.Where(p => p.Industry == industry).ToList();

        var topSkills = industryPlans
            .SelectMany(p => p.SkillsIdentified)
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new SkillFrequency { Skill = g.Key, Percentage = (g.Count() * 100 / industryPlans.Count) })
            .ToList();

        var roleDistribution = industryPlans
            .GroupBy(p => p.TargetRole)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new RoleCount { Role = g.Key, Count = g.Count() })
            .ToList();

        return new IndustryInsights
        {
            Industry = industry,
            TotalUsers = industryPlans.Count,
            TopSkills = topSkills,
            PopularRoles = roleDistribution,
            AverageExperience = industryPlans.Average(p => p.TotalYearsOfExperience)
        };
    }

    public async Task<SkillGapAnalysis> GetSkillGapAnalysisAsync(string userId, CancellationToken ct = default)
    {
        var userPlan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
        if (userPlan == null) return null;

        var allPlans = await _dynamoDb.GetAllRoadmapsAsync(ct);
        var similarUsers = allPlans.Where(p => p.TargetRole == userPlan.TargetRole && p.UserId != userId).ToList();

        var commonSkills = similarUsers
            .SelectMany(p => p.SkillsIdentified)
            .GroupBy(s => s)
            .Where(g => g.Count() >= similarUsers.Count * 0.6) // 60% threshold
            .Select(g => new SkillFrequency { Skill = g.Key, Percentage = (g.Count() * 100 / similarUsers.Count) })
            .ToList();

        var missingSkills = commonSkills
            .Where(cs => !userPlan.SkillsIdentified.Contains(cs.Skill))
            .OrderByDescending(cs => cs.Percentage)
            .ToList();

        var uniqueSkills = userPlan.SkillsIdentified
            .Where(s => !commonSkills.Any(cs => cs.Skill == s))
            .ToList();

        return new SkillGapAnalysis
        {
            MissingSkills = missingSkills,
            UniqueSkills = uniqueSkills,
            ComparisonCount = similarUsers.Count
        };
    }

    public SalaryInsights GetSalaryInsights(string targetRole, string industry, int yearsOfExperience)
    {
        var baseSalary = GetBaseSalary(targetRole);
        var experienceMultiplier = 1 + (yearsOfExperience * 0.05);
        var averageSalary = (int)(baseSalary * experienceMultiplier);
        
        return new SalaryInsights
        {
            AverageSalary = averageSalary,
            MinSalary = (int)(averageSalary * 0.8),
            MaxSalary = (int)(averageSalary * 1.3),
            TopCompanies = new() { "Amazon", "Microsoft", "Google", "Meta", "Apple" },
            LocationSalaries = new()
            {
                ["San Francisco"] = (int)(averageSalary * 1.4),
                ["Seattle"] = (int)(averageSalary * 1.3),
                ["New York"] = (int)(averageSalary * 1.35),
                ["Austin"] = (int)(averageSalary * 1.1),
                ["Remote"] = averageSalary
            }
        };
    }

    private int GetBaseSalary(string role)
    {
        var lower = role.ToLower();
        if (lower.Contains("senior") || lower.Contains("lead")) return 140000;
        if (lower.Contains("engineer") || lower.Contains("developer")) return 110000;
        if (lower.Contains("manager")) return 120000;
        return 95000;
    }
}
public class ResumeQualityScore
{
    public int Score { get; set; }
    public List<string> Strengths { get; set; } = new();
    public List<string> Improvements { get; set; } = new();
    public int SkillCount { get; set; }
    public int ExperienceYears { get; set; }
    public int ProgressPercent { get; set; }
}

public class IndustryInsights
{
    public string Industry { get; set; }
    public int TotalUsers { get; set; }
    public List<SkillFrequency> TopSkills { get; set; } = new();
    public List<RoleCount> PopularRoles { get; set; } = new();
    public double AverageExperience { get; set; }
}

public class SkillFrequency
{
    public string Skill { get; set; }
    public int Percentage { get; set; }
}

public class RoleCount
{
    public string Role { get; set; }
    public int Count { get; set; }
}

public class SkillGapAnalysis
{
    public List<SkillFrequency> MissingSkills { get; set; } = new();
    public List<string> UniqueSkills { get; set; } = new();
    public int ComparisonCount { get; set; }
}

public class SalaryInsights
{
    public int AverageSalary { get; set; }
    public int MinSalary { get; set; }
    public int MaxSalary { get; set; }
    public List<string> TopCompanies { get; set; } = new();
    public Dictionary<string, int> LocationSalaries { get; set; } = new();
}
