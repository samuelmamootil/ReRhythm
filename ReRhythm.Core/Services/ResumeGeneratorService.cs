using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.RegularExpressions;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class ResumeGeneratorService
{
    public byte[] GenerateFutureReadyResume(RoadmapPlan plan, string originalResumeText, List<LessonPlan> completedLessons)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var completedCount = completedLessons.Count(l => l.IsCompleted);
        var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);
        var completionRate = totalLessons > 0 ? (completedCount / (double)totalLessons) * 100 : 0;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial").LineHeight(1.2f));

                page.Header().ShowOnce().Column(column =>
                {
                    column.Item().AlignCenter().Text(plan.UserId.Replace("-", " ").ToUpper())
                        .FontSize(20).Bold();
                    column.Item().AlignCenter().PaddingTop(5)
                        .Text($"Target Role: {plan.TargetRole}")
                        .FontSize(12).FontColor("#0ea5e9");
                });

                page.Content().PaddingTop(20).Column(column =>
                {
                    // Skills Section
                    column.Item().PaddingBottom(15).Column(skillCol =>
                    {
                        skillCol.Item().BorderBottom(2).BorderColor("#0ea5e9").PaddingBottom(3)
                            .Text("TECHNICAL SKILLS").FontSize(14).Bold();
                        skillCol.Item().PaddingTop(8).Text(string.Join(" • ", plan.SkillsToAcquire.Take(15)))
                            .FontSize(10);
                    });

                    // Training Progress
                    column.Item().PaddingBottom(15).Column(progressCol =>
                    {
                        progressCol.Item().BorderBottom(2).BorderColor("#0ea5e9").PaddingBottom(3)
                            .Text("RERHYTHM TRAINING PROGRESS").FontSize(14).Bold();
                        progressCol.Item().PaddingTop(8).Text($"Completed {completedCount} of {totalLessons} lessons ({completionRate:F0}%)")
                            .FontSize(10).Bold().FontColor("#0ea5e9");
                        progressCol.Item().PaddingTop(5).Text($"Program: 28-Day {plan.TargetRole} Career Development")
                            .FontSize(10);
                        progressCol.Item().PaddingTop(3).Text($"Industry Focus: {plan.Industry}")
                            .FontSize(10);
                    });

                    // Portfolio Projects
                    if (plan.Modules.Any(m => !string.IsNullOrEmpty(m.PortfolioProject)))
                    {
                        column.Item().PaddingBottom(15).Column(projectCol =>
                        {
                            projectCol.Item().BorderBottom(2).BorderColor("#0ea5e9").PaddingBottom(3)
                                .Text("PORTFOLIO PROJECTS").FontSize(14).Bold();
                            projectCol.Item().PaddingTop(8).Column(projects =>
                            {
                                foreach (var module in plan.Modules.Where(m => !string.IsNullOrEmpty(m.PortfolioProject)).Take(4))
                                {
                                    projects.Item().PaddingBottom(8).Column(p =>
                                    {
                                        p.Item().Text($"Week {module.WeekNumber}: {module.Theme}")
                                            .FontSize(11).Bold();
                                        p.Item().PaddingTop(2).PaddingLeft(15)
                                            .Text($"• {module.PortfolioProject}")
                                            .FontSize(10);
                                    });
                                }
                            });
                        });
                    }

                    // Certifications & Achievements
                    column.Item().Column(certCol =>
                    {
                        certCol.Item().BorderBottom(2).BorderColor("#0ea5e9").PaddingBottom(3)
                            .Text("CERTIFICATIONS & ACHIEVEMENTS").FontSize(14).Bold();
                        certCol.Item().PaddingTop(8).Column(certs =>
                        {
                            certs.Item().PaddingBottom(3).Text($"• ReRhythm {plan.TargetRole} Program - {completionRate:F0}% Complete")
                                .FontSize(10);
                            certs.Item().PaddingBottom(3).Text($"• AWS-Powered Career Development Training")
                                .FontSize(10);
                            if (completionRate >= 100)
                            {
                                certs.Item().PaddingBottom(3).Text($"• Certificate of Completion - {DateTime.UtcNow:MMMM yyyy}")
                                    .FontSize(10).Bold().FontColor("#0ea5e9");
                            }
                        });
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by ReRhythm • ").FontSize(8).FontColor("#94a3b8");
                    text.Span($"{DateTime.UtcNow:MMMM dd, yyyy}").FontSize(8).FontColor("#94a3b8");
                });
            });
        });

        return document.GeneratePdf();
    }

    private ParsedResumeData ParseOriginalResume(string resumeText)
    {
        var data = new ParsedResumeData();
        if (string.IsNullOrWhiteSpace(resumeText))
            return data;

        // Decode HTML entities first
        resumeText = System.Net.WebUtility.HtmlDecode(resumeText);
        
        // Fix common spacing issues from PDF extraction
        resumeText = Regex.Replace(resumeText, @"([a-z])([A-Z])", "$1 $2"); // Add space between camelCase
        resumeText = Regex.Replace(resumeText, @"([.,;])([A-Za-z])", "$1 $2"); // Add space after punctuation
        
        var lines = resumeText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        // Extract name (first substantial line)
        data.Name = lines.FirstOrDefault(l => l.Length > 2 && !l.Contains("@")) ?? "Professional";

        // Extract contact info (email, phone)
        var emailPattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
        var phonePattern = @"\+?\d[\d\s\-\(\)]{7,}";
        var emails = Regex.Matches(resumeText, emailPattern).Select(m => m.Value).ToList();
        var phones = Regex.Matches(resumeText, phonePattern).Select(m => m.Value).ToList();
        if (emails.Any() || phones.Any())
            data.ContactInfo = string.Join(" | ", emails.Concat(phones).Take(2));

        // Extract career objective/summary
        var summaryIndex = lines.FindIndex(l => 
            l.Equals("CAREER OBJECTIVE", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("OBJECTIVE", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("PROFESSIONAL SUMMARY", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("SUMMARY", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("PROFILE", StringComparison.OrdinalIgnoreCase));
        
        if (summaryIndex >= 0)
        {
            var summaryLines = new List<string>();
            for (int i = summaryIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Equals("TECHNICAL SKILLS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("SKILLS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("PROFESSIONAL EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("WORK EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EDUCATION", StringComparison.OrdinalIgnoreCase))
                    break;
                if (lines[i].Length > 10)
                    summaryLines.Add(lines[i]);
            }
            data.Summary = string.Join(" ", summaryLines);
        }

        // Extract skills - stop at EXPERIENCE section
        var skillsIndex = lines.FindIndex(l => 
            l.Equals("TECHNICAL SKILLS", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("SKILLS", StringComparison.OrdinalIgnoreCase));
        
        if (skillsIndex >= 0)
        {
            for (int i = skillsIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Equals("PROFESSIONAL EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("WORK EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EDUCATION", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("PROJECTS", StringComparison.OrdinalIgnoreCase))
                    break;
                
                // Parse skills from lines with bullets or colons
                if (lines[i].StartsWith("•") || lines[i].Contains(":"))
                {
                    var skillLine = System.Net.WebUtility.HtmlDecode(lines[i])
                        .Replace("•", "")
                        .Replace("&amp;", "&");
                    
                    var skills = skillLine
                        .Split(new[] { ':', ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Skip(skillLine.Contains(":") ? 1 : 0)
                        .SelectMany(s => s.Split(','))
                        .Select(s => s.Trim())
                        .Where(s => s.Length > 2 && s.Length < 40);
                    data.Skills.AddRange(skills);
                }
            }
            data.Skills = data.Skills.Distinct().ToList();
        }

        // Extract work experience - must have PROFESSIONAL EXPERIENCE or WORK EXPERIENCE header
        var expIndex = lines.FindIndex(l => 
            l.Equals("PROFESSIONAL EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("WORK EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase));
        
        if (expIndex >= 0)
        {
            var currentExp = new WorkExperience();
            var inExperienceSection = true;
            
            for (int i = expIndex + 1; i < lines.Count && inExperienceSection; i++)
            {
                var line = lines[i];
                
                // Stop at next major section
                if (line.Equals("EDUCATION", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("PROJECTS", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("PERSONAL PROJECTS", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("CERTIFICATIONS", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("TECHNICAL SKILLS", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentExp.Role))
                        data.WorkExperiences.Add(currentExp);
                    break;
                }

                // Date pattern for work experience
                var datePattern = @"(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\d{4}).*?[-–—].*?(Present|Current|Now|\d{4})";
                if (Regex.IsMatch(line, datePattern, RegexOptions.IgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentExp.Role))
                        data.WorkExperiences.Add(currentExp);
                    currentExp = new WorkExperience { Duration = line };
                }
                else if (string.IsNullOrEmpty(currentExp.Role) && line.Length > 5 && 
                         !line.StartsWith("•") && !line.Contains("Languages") && !line.Contains("Tools"))
                {
                    currentExp.Role = line;
                }
                else if (!string.IsNullOrEmpty(currentExp.Role) && string.IsNullOrEmpty(currentExp.Company) && 
                         line.Length > 2 && !line.StartsWith("•") && !line.Contains("|"))
                {
                    currentExp.Company = line;
                }
                else if (line.StartsWith("•") && line.Length > 10)
                {
                    var responsibility = line.TrimStart('•', ' ').Trim();
                    currentExp.Responsibilities.Add(responsibility);
                }
            }
            
            if (!string.IsNullOrEmpty(currentExp.Role))
                data.WorkExperiences.Add(currentExp);
        }

        // Extract personal projects
        var projectIndex = lines.FindIndex(l => 
            l.Equals("PROJECTS", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("PERSONAL PROJECTS", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("ACADEMIC PROJECTS", StringComparison.OrdinalIgnoreCase));
        
        if (projectIndex >= 0)
        {
            var currentProject = new PersonalProject();
            for (int i = projectIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Equals("EDUCATION", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("CERTIFICATIONS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("PROFESSIONAL EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("SKILLS", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentProject.Name))
                        data.PersonalProjects.Add(currentProject);
                    break;
                }

                // Project name line (contains | separator with tech stack)
                if (lines[i].Contains("|") && string.IsNullOrEmpty(currentProject.Name))
                {
                    if (!string.IsNullOrEmpty(currentProject.Name))
                        data.PersonalProjects.Add(currentProject);
                    currentProject = new PersonalProject { Name = lines[i] };
                }
                // Project description bullets
                else if (lines[i].StartsWith("•") && !string.IsNullOrEmpty(currentProject.Name))
                {
                    currentProject.Description.Add(lines[i].TrimStart('•', ' '));
                }
                // New project without saving previous (handles consecutive project names)
                else if (lines[i].Contains("|") && !string.IsNullOrEmpty(currentProject.Name))
                {
                    data.PersonalProjects.Add(currentProject);
                    currentProject = new PersonalProject { Name = lines[i] };
                }
            }
            if (!string.IsNullOrEmpty(currentProject.Name))
                data.PersonalProjects.Add(currentProject);
            
            // Deduplicate projects by name
            data.PersonalProjects = data.PersonalProjects
                .GroupBy(p => p.Name)
                .Select(g => g.First())
                .ToList();
        }

        // Extract education with proper structure
        var eduIndex = lines.FindIndex(l => l.Equals("EDUCATION", StringComparison.OrdinalIgnoreCase));
        if (eduIndex >= 0)
        {
            var currentEdu = new EducationEntry();
            for (int i = eduIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Equals("CERTIFICATIONS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("PROFESSIONAL EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("SKILLS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("PROJECTS", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentEdu.Institution))
                        data.EducationEntries.Add(currentEdu);
                    break;
                }

                // University/Institution line with dates and degree
                if (lines[i].Contains("|") && lines[i].Contains("University", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentEdu.Institution))
                        data.EducationEntries.Add(currentEdu);
                    
                    currentEdu = new EducationEntry();
                    var parts = lines[i].Split('|').Select(p => p.Trim()).ToArray();
                    
                    currentEdu.Institution = parts[0];
                    if (parts.Length > 1) currentEdu.Duration = parts[1];
                    if (parts.Length > 2) currentEdu.Degree = parts[2];
                    
                    // Extract GPA if present
                    for (int j = 3; j < parts.Length; j++)
                    {
                        if (parts[j].Contains("GPA", StringComparison.OrdinalIgnoreCase))
                        {
                            var gpaMatch = Regex.Match(parts[j], @"(\d\.\d+)\s*/\s*(\d)");
                            if (gpaMatch.Success)
                                currentEdu.GPA = $"{gpaMatch.Groups[1].Value}/{gpaMatch.Groups[2].Value}";
                        }
                    }
                }
                // Coursework line
                else if (lines[i].Contains("Coursework", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentEdu.Institution))
                {
                    var coursework = lines[i].Replace("Coursework:", "").Replace("Relevant Coursework:", "").Trim();
                    currentEdu.Coursework = coursework.Length > 150 ? coursework.Substring(0, 150) + "..." : coursework;
                }
            }
            
            if (!string.IsNullOrEmpty(currentEdu.Institution))
                data.EducationEntries.Add(currentEdu);
        }

        // Extract certifications
        var certIndex = lines.FindIndex(l => 
            l.Equals("CERTIFICATIONS", StringComparison.OrdinalIgnoreCase) ||
            l.Equals("CERTIFICATES", StringComparison.OrdinalIgnoreCase));
        
        if (certIndex >= 0)
        {
            var currentCert = "";
            for (int i = certIndex + 1; i < lines.Count; i++)
            {
                if (lines[i].Equals("SKILLS", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EXPERIENCE", StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Equals("EDUCATION", StringComparison.OrdinalIgnoreCase))
                    break;
                
                var line = lines[i].TrimStart('•', '-', '*', ' ');
                
                // Check if line is a date (month + year or just year)
                var isDate = Regex.IsMatch(line, @"^(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\s+\d{4}$") ||
                             Regex.IsMatch(line, @"^\d{4}$");
                
                if (isDate && !string.IsNullOrEmpty(currentCert))
                {
                    // Append date to current cert
                    data.Certifications.Add($"{currentCert} - {line}");
                    currentCert = "";
                }
                else if (!isDate && line.Length > 5)
                {
                    // Save previous cert if exists
                    if (!string.IsNullOrEmpty(currentCert))
                        data.Certifications.Add(currentCert);
                    currentCert = line;
                }
                
                if (data.Certifications.Count >= 10) break;
            }
            
            // Add last cert if exists
            if (!string.IsNullOrEmpty(currentCert))
                data.Certifications.Add(currentCert);
        }

        return data;
    }

    private List<string> GetNewSkillsFromLessons(List<LessonPlan> completedLessons, RoadmapPlan plan)
    {
        var newSkills = new List<string>();
        
        if (!completedLessons.Any())
            return newSkills;
        
        var completedWeeks = completedLessons.Select(l => l.WeekNumber).Distinct().ToList();
        
        // Add skills from completed modules
        foreach (var week in completedWeeks)
        {
            var module = plan.Modules.FirstOrDefault(m => m.WeekNumber == week);
            if (module != null)
            {
                // Add milestones as skills
                newSkills.AddRange(module.MilestonesUnlocked);
            }
        }
        
        // Add skills to acquire based on completion percentage
        var completionRate = completedLessons.Count / (double)plan.Modules.Sum(m => m.DailySprints.Count);
        if (completionRate > 0.25) // If 25%+ complete, add target skills
        {
            newSkills.AddRange(plan.SkillsToAcquire.Take((int)(plan.SkillsToAcquire.Count * completionRate)));
        }
        
        return newSkills.Distinct().Take(15).ToList(); // Limit to 15 new skills
    }
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
    public List<string> Description { get; set; } = new();
}

public class WorkExperience
{
    public string Company { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public List<string> Responsibilities { get; set; } = new();
}
