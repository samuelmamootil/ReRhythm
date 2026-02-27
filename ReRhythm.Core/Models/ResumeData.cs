namespace ReRhythm.Core.Models;

public class ResumeData
{
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public string ParsedText { get; set; } = string.Empty;
    public List<string> ExtractedSkills { get; set; } = [];
    public List<string> ExtractedRoles { get; set; } = [];
    public List<string> ExtractedCertifications { get; set; } = [];
    public string Education { get; set; } = string.Empty;
    public int YearsExperience { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public string ContactInfo { get; set; } = string.Empty;
    public List<WorkExperience> WorkHistory { get; set; } = [];
}

public class WorkExperience
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public List<string> Responsibilities { get; set; } = [];
}
