using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ReRhythm.Core.Services;

public class ResumeGeneratorService
{
    public byte[] GenerateFutureReadyResume(RoadmapPlan plan, string originalResumeText)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(50);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Calibri"));

                page.Header().Column(column =>
                {
                    column.Item().Text(plan.UserId.ToUpper()).FontSize(20).Bold();
                    column.Item().PaddingTop(5).Text(plan.TargetRole).FontSize(16).SemiBold().FontColor("#0ea5e9");
                    column.Item().PaddingTop(10).LineHorizontal(2).LineColor("#0ea5e9");
                });

                page.Content().PaddingTop(20).Column(column =>
                {
                    // Professional Summary
                    column.Item().PaddingBottom(15).Column(summaryCol =>
                    {
                        summaryCol.Item().Text("PROFESSIONAL SUMMARY").FontSize(13).Bold().FontColor("#1e293b");
                        summaryCol.Item().PaddingTop(5).Text($"Accomplished {plan.TargetRole} with comprehensive expertise in cloud architecture, DevOps practices, and infrastructure automation. Successfully completed intensive 28-day upskilling program mastering {plan.SkillsToAcquire.Count} advanced technical competencies. Proven track record of delivering {plan.Modules.Count} production-grade portfolio projects demonstrating hands-on proficiency in modern cloud technologies.");
                    });

                    // Technical Skills
                    column.Item().PaddingBottom(15).Column(skillCol =>
                    {
                        skillCol.Item().Text("TECHNICAL SKILLS").FontSize(13).Bold().FontColor("#1e293b");
                        skillCol.Item().PaddingTop(5).Column(skills =>
                        {
                            var allSkills = plan.SkillsIdentified.Concat(plan.SkillsToAcquire).ToList();
                            foreach (var skillGroup in allSkills.Select((s, i) => new { Skill = s, Index = i }).GroupBy(x => x.Index / 3))
                            {
                                skills.Item().Text(string.Join(" • ", skillGroup.Select(x => x.Skill)));
                            }
                        });
                    });

                    // Portfolio Projects
                    column.Item().PaddingBottom(15).Column(projectCol =>
                    {
                        projectCol.Item().Text("PORTFOLIO PROJECTS").FontSize(13).Bold().FontColor("#1e293b");
                        projectCol.Item().PaddingTop(5).Column(projects =>
                        {
                            foreach (var module in plan.Modules)
                            {
                                if (!string.IsNullOrEmpty(module.PortfolioProject))
                                {
                                    projects.Item().PaddingBottom(10).Column(proj =>
                                    {
                                        proj.Item().Text(module.Theme).SemiBold().FontSize(12);
                                        proj.Item().PaddingLeft(15).Text(module.PortfolioProject).FontSize(10);
                                        if (module.MilestonesUnlocked.Any())
                                        {
                                            proj.Item().PaddingLeft(15).PaddingTop(3).Text("Key Achievements:").FontSize(10).Italic();
                                            foreach (var milestone in module.MilestonesUnlocked.Take(2))
                                            {
                                                proj.Item().PaddingLeft(20).Text($"• {milestone}").FontSize(9);
                                            }
                                        }
                                    });
                                }
                            }
                        });
                    });

                    // Certifications
                    column.Item().PaddingBottom(15).Column(certCol =>
                    {
                        certCol.Item().Text("CERTIFICATIONS & TRAINING").FontSize(13).Bold().FontColor("#1e293b");
                        certCol.Item().PaddingTop(5).Column(certs =>
                        {
                            certs.Item().Text("• AWS Certified Solutions Architect - Professional (Exam Ready)");
                            certs.Item().Text("• AWS Certified DevOps Engineer - Professional (Exam Ready)");
                            certs.Item().Text("• Certified Kubernetes Administrator (Exam Ready)");
                            certs.Item().Text($"• Completed 28-Day Intensive {plan.TargetRole} Bootcamp ({DateTime.UtcNow:MMM yyyy})");
                        });
                    });

                    // Additional Info
                    column.Item().Column(infoCol =>
                    {
                        infoCol.Item().Text("ADDITIONAL INFORMATION").FontSize(13).Bold().FontColor("#1e293b");
                        infoCol.Item().PaddingTop(5).Text($"• Completed {plan.Modules.Sum(m => m.DailySprints.Count)} hands-on technical labs");
                        infoCol.Item().Text($"• Mastered {plan.SkillsToAcquire.Count} advanced technical skills in 28 days");
                        infoCol.Item().Text($"• Built {plan.Modules.Count} production-ready portfolio projects");
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span($"Generated: {DateTime.UtcNow:MMMM dd, yyyy} • ").FontSize(8).FontColor("#64748b");
                    text.Span("Enhanced by ReRhythm AI Career Platform").FontSize(8).FontColor("#64748b");
                });
            });
        });

        return document.GeneratePdf();
    }
}
