using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
using ReRhythm.Core.Models;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ReRhythm.Web.Controllers;

public partial class RoadmapController
{
    [HttpGet]
    public async Task<IActionResult> DownloadEnhancedResume(string userId, CancellationToken ct)
    {
        try
        {
            var plan = await _dynamoDb.GetLatestRoadmapAsync(userId, ct);
            if (plan == null)
                return NotFound("Roadmap not found");

            var allLessons = await _dynamoDb.GetAllLessonsForUserAsync(userId, ct);
            var completedCount = allLessons.Count(l => l.IsCompleted);
            var totalLessons = plan.Modules?.Sum(m => m.DailySprints?.Count ?? 0) ?? 0;

            if (completedCount < totalLessons)
                return BadRequest("Complete all 28 lessons to unlock enhanced resume");

            var skillsLearned = plan.SkillsToAcquire ?? new List<string>();
            var completedProjects = plan.Modules?
                .Where(m => !string.IsNullOrEmpty(m.PortfolioProject))
                .Select(m => m.PortfolioProject!)
                .ToList() ?? new List<string>();

            // Use stored resume data or create fallback from plan data
            var resumeData = plan.ParsedResumeData;
            if (resumeData == null)
            {
                _logger.LogWarning("ParsedResumeData is null for user {UserId}, creating fallback from plan", userId);
                resumeData = new ResumeData
                {
                    ParsedText = plan.OriginalResumeText ?? "No original resume",
                    Name = plan.FullName ?? "User",
                    Email = plan.Email ?? "",
                    Phone = plan.PhoneNumber ?? "",
                    YearsExperience = plan.TotalYearsOfExperience,
                    TechnicalSkills = plan.SkillsToAcquire?.Take(10).ToList() ?? new List<string>(),
                    ProfessionalSummary = $"Experienced professional with {plan.TotalYearsOfExperience} years in {plan.Industry}"
                };
            }

            var resumeBuilder = HttpContext.RequestServices.GetRequiredService<ResumeBuilderService>();
            var enhancedResume = await resumeBuilder.GenerateEnhancedResumeAsync(
                resumeData,
                skillsLearned,
                plan.TargetRole ?? "Software Engineer",
                plan.Industry ?? "Technology",
                completedProjects
            );

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Calibri));

                    page.Content().Column(column =>
                    {
                        var lines = enhancedResume.Split('\n');
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                column.Item().PaddingVertical(3).Text("");
                            }
                            else if (line.Trim().All(c => c == '=' || c == '-'))
                            {
                                column.Item().PaddingVertical(2).LineHorizontal(0.5f);
                            }
                            else if (line == line.ToUpper() && line.Length < 50)
                            {
                                column.Item().PaddingTop(5).Text(line).Bold().FontSize(13);
                            }
                            else
                            {
                                column.Item().Text(line);
                            }
                        }
                    });
                });
            }).GeneratePdf();

            var userName = plan.FullName?.Split('\n')[0]?.Trim();
            if (string.IsNullOrWhiteSpace(userName) || userName.Length > 50)
                userName = userId;
            else
                userName = userName.Replace(" ", "_");

            return File(pdfBytes, "application/pdf", $"ReRhythm_Enhanced_Resume_{userName}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating enhanced resume for user {UserId}", userId);
            return StatusCode(500, "Error generating resume");
        }
    }
}
