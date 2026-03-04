namespace ReRhythm.Core.Models;

public class ResumeData
{
    public string UserId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public string ParsedText { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    
    // Contact Information
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    
    // Professional Information
    public string ProfessionalSummary { get; set; } = string.Empty;
    public List<string> TechnicalSkills { get; set; } = [];
    public List<WorkExperience> WorkHistory { get; set; } = [];
    public int YearsExperience { get; set; }
    
    // Education & Projects
    public List<EducationEntry> Education { get; set; } = [];
    public List<PersonalProject> PersonalProjects { get; set; } = [];
    
    // Additional Sections
    public List<string> Certifications { get; set; } = [];
    public List<string> ExtracurricularActivities { get; set; } = [];
    public List<string> StudentOrganizations { get; set; } = [];
    public Dictionary<string, string> ExtraInfo { get; set; } = new();
}

public class ParsedResumeData
{
    public string Name { get; set; } = string.Empty;
    public string ContactInfo { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> Skills { get; set; } = new();
    public List<WorkExperience> WorkExperiences { get; set; } = new();
    public List<PersonalProject> PersonalProjects { get; set; } = new();
    public List<EducationEntry> EducationEntries { get; set; } = new();
    public List<string> Certifications { get; set; } = new();
}

public class WorkExperience
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public List<string> Responsibilities { get; set; } = [];
}

public class EducationEntry
{
    public string Institution { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string GPA { get; set; } = string.Empty;
    public string Coursework { get; set; } = string.Empty;
}

public class PersonalProject
{
    public string Name { get; set; } = string.Empty;
    public List<string> Description { get; set; } = [];
    public string TechStack { get; set; } = string.Empty;
}
