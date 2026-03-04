using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class TextractService
{
    private readonly IAmazonTextract _textract;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<TextractService> _logger;

    private string ResumeBucket => _config["S3:ResumeBucket"]!;

    public TextractService(
        IAmazonTextract textract,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<TextractService> logger)
    {
        _textract = textract;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task<ResumeData> UploadAndParseResumeAsync(
        Stream fileStream,
        string fileName,
        string userId,
        CancellationToken ct = default)
    {
        var s3Key = $"resumes/{userId}/{Guid.NewGuid()}/{fileName}";
        
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream, ct);
        var fileBytes = memoryStream.ToArray();
        
        _logger.LogInformation("Processing file {FileName}, size: {Size} bytes", fileName, fileBytes.Length);
        
        if (fileBytes.Length == 0)
            throw new InvalidOperationException("File is empty");
        
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        string resumeText;
        
        // Handle DOCX directly
        if (ext == ".docx")
        {
            _logger.LogInformation("Processing DOCX file directly");
            resumeText = ExtractTextFromDocx(fileBytes);
            _logger.LogInformation("DOCX parsed {CharCount} chars", resumeText.Length);
        }
        else
        {
            // Upload to S3 for Textract
            memoryStream.Position = 0;
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = ResumeBucket,
                Key = s3Key,
                InputStream = memoryStream,
                ContentType = GetContentType(fileName)
            }, ct);

            _logger.LogInformation("Resume uploaded to S3: {Key}", s3Key);
            await Task.Delay(2000, ct);

            try
            {
                var detectRequest = new DetectDocumentTextRequest
                {
                    Document = new Amazon.Textract.Model.Document
                    {
                        S3Object = new Amazon.Textract.Model.S3Object
                        {
                            Bucket = ResumeBucket,
                            Name = s3Key
                        }
                    }
                };

                var response = await _textract.DetectDocumentTextAsync(detectRequest, ct);
                resumeText = ExtractTextFromBlocks(response.Blocks);
                _logger.LogInformation("Textract parsed {CharCount} chars", resumeText.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Textract failed, falling back to PdfPig");
                resumeText = ExtractTextWithPdfPig(fileBytes);
                _logger.LogInformation("PdfPig parsed {CharCount} chars", resumeText.Length);
            }
        }
        
        if (resumeText.Length < 100)
            _logger.LogWarning("Resume text too short ({Length} chars)", resumeText.Length);
        
        var resumeData = ParseResumeData(resumeText);
        resumeData.UserId = userId;
        resumeData.FileName = fileName;
        resumeData.S3Key = s3Key;
        resumeData.ParsedText = resumeText;
        
        // Validate and log parsing results
        LogParsingResults(resumeData);
        
        return resumeData;
    }

    private ResumeData ParseResumeData(string resumeText)
    {
        var data = new ResumeData();
        var lines = resumeText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        
        // Extract Name (first non-header line)
        foreach (var line in lines.Take(10))
        {
            if (line.Length < 2 || line.Length > 50) continue;
            if (line.Contains('@') || line.Contains("http")) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\d{3,}")) continue;
            var keywords = new[] { "RESUME", "CV", "CURRICULUM", "SUMMARY", "EXPERIENCE", "EDUCATION", "SKILLS" };
            if (keywords.Any(k => line.ToUpper().Contains(k))) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z][A-Za-z\s\.'-]+$"))
            {
                data.Name = line;
                break;
            }
        }
        if (string.IsNullOrEmpty(data.Name)) data.Name = "User";
        
        // Extract Contact Information
        foreach (var line in lines.Take(20))
        {
            var emailMatch = System.Text.RegularExpressions.Regex.Match(line, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            if (emailMatch.Success && string.IsNullOrEmpty(data.Email))
                data.Email = emailMatch.Value;
            
            var phoneMatch = System.Text.RegularExpressions.Regex.Match(line, @"\+?[1-9]\d{0,3}[\s\-]?\(?\d{1,4}\)?[\s\-]?\d{1,4}[\s\-]?\d{1,9}");
            if (phoneMatch.Success && string.IsNullOrEmpty(data.Phone))
                data.Phone = phoneMatch.Value.Trim();
            
            if (line.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(data.LinkedIn))
                data.LinkedIn = line.Trim();
            
            if (line.Contains("github.com", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(data.GitHub))
                data.GitHub = line.Trim();
            
            if ((line.Contains("http://") || line.Contains("https://")) && 
                !line.Contains("linkedin") && !line.Contains("github") && string.IsNullOrEmpty(data.Website))
                data.Website = line.Trim();
        }
        
        // Extract Location (more flexible - after contact info extraction)
        foreach (var line in lines.Take(20))
        {
            if (string.IsNullOrEmpty(data.Location) && 
                !line.Contains('@') && !line.Contains("http") && 
                !line.Contains(data.Name) && !line.Contains(data.Phone ?? "") &&
                line.Length >= 3 && line.Length <= 50 &&
                !line.Contains(':') && !IsSectionHeader(line) &&
                System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z][A-Za-z\s,.-]+$"))
            {
                // Could be location if it's a reasonable city/state format
                var locationKeywords = new[] { "SUMMARY", "OBJECTIVE", "EXPERIENCE", "EDUCATION", "SKILLS", "PHONE", "EMAIL" };
                if (!locationKeywords.Any(k => line.ToUpper().Contains(k)))
                {
                    data.Location = line.Trim();
                }
            }
        }
        
        // Extract Professional Summary
        var summaryIndex = FindSectionIndex(lines, "SUMMARY", "OBJECTIVE", "PROFILE", "PROFESSIONAL SUMMARY");
        if (summaryIndex >= 0)
        {
            var summaryLines = new List<string>();
            for (int i = summaryIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i])) break;
                if (lines[i].Length > 10) summaryLines.Add(lines[i]);
            }
            data.ProfessionalSummary = string.Join(" ", summaryLines);
        }
        
        // Extract Technical Skills
        var skillsIndex = FindSectionIndex(lines, "SKILLS", "TECHNICAL SKILLS", "CORE COMPETENCIES");
        if (skillsIndex >= 0)
        {
            for (int i = skillsIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i])) break;
                var skillLine = lines[i].Replace("•", "").Replace("&amp;", "&");
                var skills = skillLine.Split(new[] { ':', ',', '|', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Skip(skillLine.Contains(":") ? 1 : 0)
                    .SelectMany(s => s.Split(','))
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 2 && s.Length < 40);
                data.TechnicalSkills.AddRange(skills);
            }
            data.TechnicalSkills = data.TechnicalSkills.Distinct().ToList();
        }
        
        // Extract Work Experience
        var expIndex = FindSectionIndex(lines, "EXPERIENCE", "WORK EXPERIENCE", "PROFESSIONAL EXPERIENCE");
        if (expIndex >= 0)
        {
            var currentExp = new WorkExperience();
            for (int i = expIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i]))
                {
                    if (!string.IsNullOrEmpty(currentExp.Role) || !string.IsNullOrEmpty(currentExp.Company)) 
                        data.WorkHistory.Add(currentExp);
                    break;
                }
                
                var datePattern = @"(Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\d{4}).*?[-–—].*?(Present|Current|\d{4})";
                if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], datePattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentExp.Role) || !string.IsNullOrEmpty(currentExp.Company)) 
                        data.WorkHistory.Add(currentExp);
                    currentExp = new WorkExperience { Duration = lines[i] };
                }
                else if (string.IsNullOrEmpty(currentExp.Role) && lines[i].Length > 5 && !lines[i].StartsWith("•"))
                {
                    // Could be role or company - try to determine
                    if (lines[i].Contains("at ") || lines[i].Contains(" - "))
                    {
                        var parts = lines[i].Split(new[] { " at ", " - " }, StringSplitOptions.RemoveEmptyEntries);
                        currentExp.Role = parts[0].Trim();
                        if (parts.Length > 1) currentExp.Company = parts[1].Trim();
                    }
                    else
                    {
                        currentExp.Role = lines[i];
                    }
                }
                else if (!string.IsNullOrEmpty(currentExp.Role) && string.IsNullOrEmpty(currentExp.Company) && !lines[i].StartsWith("•"))
                    currentExp.Company = lines[i];
                else if (lines[i].StartsWith("•") && lines[i].Length > 10)
                    currentExp.Responsibilities.Add(lines[i].TrimStart('•', ' '));
            }
            if (!string.IsNullOrEmpty(currentExp.Role) || !string.IsNullOrEmpty(currentExp.Company)) 
                data.WorkHistory.Add(currentExp);
        }
        
        // Extract Education
        var eduIndex = FindSectionIndex(lines, "EDUCATION");
        if (eduIndex >= 0)
        {
            var currentEdu = new EducationEntry();
            for (int i = eduIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i]))
                {
                    if (!string.IsNullOrEmpty(currentEdu.Institution) || !string.IsNullOrEmpty(currentEdu.Degree)) 
                        data.Education.Add(currentEdu);
                    break;
                }
                
                // Handle pipe-separated format
                if (lines[i].Contains("|"))
                {
                    if (!string.IsNullOrEmpty(currentEdu.Institution) || !string.IsNullOrEmpty(currentEdu.Degree)) 
                        data.Education.Add(currentEdu);
                    currentEdu = new EducationEntry();
                    var parts = lines[i].Split('|').Select(p => p.Trim()).ToArray();
                    currentEdu.Institution = parts[0];
                    if (parts.Length > 1) currentEdu.Duration = parts[1];
                    if (parts.Length > 2) currentEdu.Degree = parts[2];
                }
                // Handle standard format (degree, institution, dates on separate lines)
                else if (string.IsNullOrEmpty(currentEdu.Degree) && lines[i].Length > 5 && 
                        (lines[i].Contains("Bachelor") || lines[i].Contains("Master") || lines[i].Contains("PhD") || 
                         lines[i].Contains("Associate") || lines[i].Contains("Certificate")))
                {
                    currentEdu.Degree = lines[i];
                }
                else if (string.IsNullOrEmpty(currentEdu.Institution) && lines[i].Length > 5 && 
                        !lines[i].Contains("Coursework") && !lines[i].Contains("GPA"))
                {
                    currentEdu.Institution = lines[i];
                }
                else if (lines[i].Contains("Coursework", StringComparison.OrdinalIgnoreCase))
                    currentEdu.Coursework = lines[i].Replace("Coursework:", "").Replace("Relevant Coursework:", "").Trim();
                
                // Extract GPA
                var gpaMatch = System.Text.RegularExpressions.Regex.Match(lines[i], @"(\d\.\d+)\s*/\s*(\d)");
                if (gpaMatch.Success) currentEdu.GPA = $"{gpaMatch.Groups[1].Value}/{gpaMatch.Groups[2].Value}";
            }
            if (!string.IsNullOrEmpty(currentEdu.Institution) || !string.IsNullOrEmpty(currentEdu.Degree)) 
                data.Education.Add(currentEdu);
        }
        
        // Extract Personal Projects
        var projectIndex = FindSectionIndex(lines, "PROJECTS", "PERSONAL PROJECTS", "ACADEMIC PROJECTS");
        if (projectIndex >= 0)
        {
            var currentProject = new PersonalProject();
            for (int i = projectIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i]))
                {
                    if (!string.IsNullOrEmpty(currentProject.Name)) data.PersonalProjects.Add(currentProject);
                    break;
                }
                if (lines[i].Contains("|") && string.IsNullOrEmpty(currentProject.Name))
                {
                    if (!string.IsNullOrEmpty(currentProject.Name)) data.PersonalProjects.Add(currentProject);
                    var parts = lines[i].Split('|');
                    currentProject = new PersonalProject { Name = parts[0].Trim(), TechStack = parts.Length > 1 ? parts[1].Trim() : "" };
                }
                else if (lines[i].StartsWith("•") && !string.IsNullOrEmpty(currentProject.Name))
                    currentProject.Description.Add(lines[i].TrimStart('•', ' '));
            }
            if (!string.IsNullOrEmpty(currentProject.Name)) data.PersonalProjects.Add(currentProject);
        }
        
        // Extract Certifications
        var certIndex = FindSectionIndex(lines, "CERTIFICATIONS", "CERTIFICATES");
        if (certIndex >= 0)
        {
            for (int i = certIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i])) break;
                var line = lines[i].TrimStart('•', '-', '*', ' ');
                if (line.Length > 5) data.Certifications.Add(line);
            }
        }
        
        // Extract Extracurricular Activities
        var extraIndex = FindSectionIndex(lines, "EXTRACURRICULAR", "ACTIVITIES", "LEADERSHIP");
        if (extraIndex >= 0)
        {
            for (int i = extraIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i])) break;
                var line = lines[i].TrimStart('•', '-', '*', ' ');
                if (line.Length > 5) data.ExtracurricularActivities.Add(line);
            }
        }
        
        // Extract Student Organizations
        var orgIndex = FindSectionIndex(lines, "ORGANIZATIONS", "STUDENT ORGANIZATIONS", "MEMBERSHIPS");
        if (orgIndex >= 0)
        {
            for (int i = orgIndex + 1; i < lines.Length; i++)
            {
                if (IsSectionHeader(lines[i])) break;
                var line = lines[i].TrimStart('•', '-', '*', ' ');
                if (line.Length > 5) data.StudentOrganizations.Add(line);
            }
        }
        
        return data;
    }
    
    private int FindSectionIndex(string[] lines, params string[] sectionNames)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (var name in sectionNames)
            {
                if (lines[i].Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    lines[i].Contains(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }
    
    private bool IsSectionHeader(string line)
    {
        var headers = new[] { "SUMMARY", "SKILLS", "EXPERIENCE", "EDUCATION", "PROJECTS", 
                              "CERTIFICATIONS", "EXTRACURRICULAR", "ORGANIZATIONS", "AWARDS" };
        return headers.Any(h => line.Equals(h, StringComparison.OrdinalIgnoreCase) || 
                               line.Contains(h, StringComparison.OrdinalIgnoreCase));
    }

    private string ExtractTextFromDocx(byte[] docxBytes)
    {
        using var stream = new MemoryStream(docxBytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return string.Empty;
        
        var paragraphs = body.Descendants<Paragraph>();
        var lines = paragraphs.Select(p => p.InnerText.Trim()).Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join("\n", lines);
    }

    private string ExtractTextWithPdfPig(byte[] pdfBytes)
    {
        using var document = PdfDocument.Open(pdfBytes);
        var text = new System.Text.StringBuilder();
        
        foreach (var page in document.GetPages())
        {
            text.AppendLine(page.Text);
        }
        
        return text.ToString();
    }

    private string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".tiff" or ".tif" => "image/tiff",
            _ => "application/octet-stream"
        };
    }

    private string ExtractTextFromBlocks(List<Block> blocks)
    {
        var lines = blocks
            .Where(b => b.BlockType == BlockType.LINE)
            .Select(b => b.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return string.Join("\n", lines);
    }
    
    private void LogParsingResults(ResumeData data)
    {
        var missingFields = new List<string>();
        
        if (string.IsNullOrEmpty(data.Name)) missingFields.Add("Name");
        if (string.IsNullOrEmpty(data.Email)) missingFields.Add("Email");
        if (string.IsNullOrEmpty(data.Phone)) missingFields.Add("Phone");
        if (!data.TechnicalSkills.Any()) missingFields.Add("TechnicalSkills");
        if (!data.WorkHistory.Any()) missingFields.Add("WorkHistory");
        if (!data.Education.Any()) missingFields.Add("Education");
        
        if (missingFields.Any())
        {
            _logger.LogWarning("Resume parsing incomplete. Missing fields: {MissingFields}", string.Join(", ", missingFields));
        }
        
        _logger.LogInformation("Resume parsing completed. Name: {Name}, Skills: {SkillCount}, Experience: {ExpCount}, Education: {EduCount}", 
            data.Name, data.TechnicalSkills.Count, data.WorkHistory.Count, data.Education.Count);
    }
}
