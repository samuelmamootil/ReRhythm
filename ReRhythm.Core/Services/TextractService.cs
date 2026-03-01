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

    public async Task<(string ResumeText, string FullName, string ContactInfo)> UploadAndParseResumeAsync(
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
        
        // Handle DOCX directly
        if (ext == ".docx")
        {
            _logger.LogInformation("Processing DOCX file directly");
            var text = ExtractTextFromDocx(fileBytes);
            
            _logger.LogInformation(
                "DOCX parsed {CharCount} chars, {LineCount} lines",
                text.Length, text.Split('\n').Length);
            
            if (text.Length < 100)
            {
                _logger.LogWarning("Resume text too short ({Length} chars), may be parsing error", text.Length);
            }
            
            var (name, contact) = ExtractNameAndContact(text);
            return (text, name, contact);
        }
        
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

            var resumeText = ExtractTextFromBlocks(response.Blocks);
            
            _logger.LogInformation(
                "Textract parsed {CharCount} chars, {LineCount} lines for user {UserId}",
                resumeText.Length, resumeText.Split('\n').Length, userId);
            
            if (resumeText.Length < 100)
            {
                _logger.LogWarning("Resume text too short ({Length} chars), may be parsing error", resumeText.Length);
            }
            
            var (fullName, contactInfo) = ExtractNameAndContact(resumeText);

            return (resumeText, fullName, contactInfo);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Textract failed, falling back to PdfPig");
            var text = ExtractTextWithPdfPig(fileBytes);
            
            _logger.LogInformation(
                "PdfPig parsed {CharCount} chars, {LineCount} lines",
                text.Length, text.Split('\n').Length);
            
            if (text.Length < 100)
            {
                _logger.LogWarning("Resume text too short ({Length} chars), may be parsing error", text.Length);
            }
            
            var (name, contact) = ExtractNameAndContact(text);
            return (text, name, contact);
        }
    }

    private (string FullName, string ContactInfo) ExtractNameAndContact(string resumeText)
    {
        var lines = resumeText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var fullName = "";
        var contactParts = new List<string>();

        _logger.LogInformation("Extracting name from {LineCount} lines", lines.Length);
        for (int i = 0; i < Math.Min(10, lines.Length); i++)
        {
            _logger.LogInformation("Line {Index}: '{Line}'", i, lines[i]);
        }

        // Extract name
        foreach (var line in lines.Take(10))
        {
            if (line.Length < 2 || line.Length > 50) continue;
            if (line.Contains('@') || line.Contains("http", StringComparison.OrdinalIgnoreCase) || line.Contains("www.", StringComparison.OrdinalIgnoreCase)) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\d{3,}")) continue;
            
            var keywords = new[] { "RESUME", "CV", "CURRICULUM", "SUMMARY", "EXPERIENCE", "EDUCATION", "SKILLS", "OBJECTIVE", "PROFILE", "PROFESSIONAL" };
            if (keywords.Any(k => line.ToUpper().Contains(k))) continue;
            if (line.Contains('|') || line.Contains('/') || line.Contains('\\')) continue;
            
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z][A-Za-z\s\.'-]+$"))
            {
                fullName = line;
                _logger.LogInformation("Found name: '{Name}'", fullName);
                break;
            }
        }
        
        // Fallback: if no name found, default to "User" - never return resume content
        if (string.IsNullOrEmpty(fullName))
        {
            fullName = "User";
            _logger.LogWarning("No valid name found, defaulting to: '{Name}'", fullName);
        }

        // Extract contact info (email, phone, location, LinkedIn)
        foreach (var line in lines.Take(15))
        {
            // Email
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"))
            {
                var email = System.Text.RegularExpressions.Regex.Match(line, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}").Value;
                if (!contactParts.Contains(email))
                    contactParts.Add(email);
                continue;
            }
            // Phone
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\+?\d[\d\s\-\(\)]{7,}"))
            {
                var phone = System.Text.RegularExpressions.Regex.Match(line, @"\+?\d[\d\s\-\(\)]{7,}").Value.Trim();
                if (!contactParts.Contains(phone))
                    contactParts.Add(phone);
                continue;
            }
            // LinkedIn
            if (line.Contains("linkedin.com"))
            {
                if (!contactParts.Contains(line))
                    contactParts.Add(line);
                continue;
            }
            // Location (city, state) - only if short and has comma
            if (line.Contains(",") && line.Length < 50 && line.Length > 5 && 
                !line.Contains(":") && !line.ToUpper().Contains("EXPERIENCE") && !line.ToUpper().Contains("EDUCATION"))
            {
                if (!contactParts.Contains(line))
                    contactParts.Add(line);
            }
        }

        _logger.LogInformation("Final name: '{Name}', Contact parts: {Count}", fullName, contactParts.Count);
        return (fullName, string.Join("  •  ", contactParts.Take(4)));
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
}
