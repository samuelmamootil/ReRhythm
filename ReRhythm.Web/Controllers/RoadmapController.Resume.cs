using Microsoft.AspNetCore.Mvc;
using ReRhythm.Core.Services;
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
            var totalLessons = plan.Modules.Sum(m => m.DailySprints.Count);

            if (completedCount < totalLessons)
                return BadRequest("Complete all 28 lessons to unlock enhanced resume");

            var skillsLearned = plan.SkillsToAcquire ?? new List<string>();
            var completedProjects = plan.Modules?
                .Where(m => !string.IsNullOrEmpty(m.PortfolioProject))
                .Select(m => m.PortfolioProject!)
                .ToList() ?? new List<string>();

            var resumeBuilder = HttpContext.RequestServices.GetRequiredService<ResumeBuilderService>();
            var enhancedResume = await resumeBuilder.GenerateEnhancedResumeAsync(
                plan.OriginalResumeText ?? "No original resume",
                skillsLearned,
                plan.TargetRole ?? "Software Engineer",
                plan.Industry ?? "Technology",
                plan.TotalYearsOfExperience,
                completedProjects
            );

            // AI already includes name and contact in the resume, don't prepend again

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

            return File(pdfBytes, "application/pdf", $"ReRhythm_Enhanced_Resume_{userId}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating enhanced resume for user {UserId}", userId);
            return StatusCode(500, "Error generating resume");
        }
    }
}
