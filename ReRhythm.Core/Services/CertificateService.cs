using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ReRhythm.Core.Models;

namespace ReRhythm.Core.Services;

public class CertificateService
{
    public byte[] GenerateCompletionCertificate(RoadmapPlan plan, List<LessonPlan> completedLessons)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var completionDate = completedLessons.Any() ? completedLessons.Max(l => l.CreatedAt) : DateTime.UtcNow;
        var verifyUrl = $"https://rerhythm.com/Roadmap/Verify/{plan.UserId}";

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(50);
                page.PageColor(Colors.White);

                page.Content().Border(6).BorderColor("#0ea5e9").Padding(35).Column(column =>
                {
                    column.Spacing(12);

                    // Logo + Header
                    column.Item().AlignCenter().Text("ReRhythm")
                        .FontSize(32).Bold().FontColor("#0ea5e9");
                    
                    column.Item().AlignCenter().Text("Catch the Industry Beat")
                        .FontSize(9).Italic().FontColor("#64748b");

                    // Title
                    column.Item().PaddingTop(20).AlignCenter()
                        .Text("CERTIFICATE OF COMPLETION")
                        .FontSize(24).Bold().FontColor("#0f172a");

                    // Line
                    column.Item().AlignCenter().Width(180).Height(2).Background("#0ea5e9");

                    // Main text
                    column.Item().PaddingTop(20).AlignCenter()
                        .Text("This certifies that").FontSize(12).FontColor("#475569");

                    column.Item().PaddingTop(5).AlignCenter()
                        .Text(plan.UserId.Replace("-", " ").ToUpper())
                        .FontSize(22).Bold().FontColor("#0f172a");

                    column.Item().AlignCenter().PaddingHorizontal(70).PaddingTop(12)
                        .Text($"has successfully completed the 28-day intensive career development program for {plan.TargetRole} in {plan.Industry}, demonstrating commitment to professional growth and skill mastery.")
                        .FontSize(11).FontColor("#475569").LineHeight(1.3f);

                    // Footer
                    column.Item().PaddingTop(25).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().AlignCenter().Text(completionDate.ToString("MMMM dd, yyyy"))
                                .FontSize(10).FontColor("#475569");
                            col.Item().PaddingTop(4).AlignCenter().Width(130).Height(1).Background("#cbd5e1");
                            col.Item().PaddingTop(3).AlignCenter().Text("Date")
                                .FontSize(8).FontColor("#94a3b8");
                        });

                        row.RelativeItem().Column(col =>
                        {
                            col.Item().AlignCenter().Text("ReRhythm Platform")
                                .FontSize(10).FontColor("#475569");
                            col.Item().PaddingTop(4).AlignCenter().Width(130).Height(1).Background("#cbd5e1");
                            col.Item().PaddingTop(3).AlignCenter().Text("Authorized Signature")
                                .FontSize(8).FontColor("#94a3b8");
                        });
                    });

                    // Verification - Clickable
                    column.Item().PaddingTop(18).AlignCenter().Hyperlink(verifyUrl)
                        .Text($"Verify at: {verifyUrl}")
                        .FontSize(8).FontColor("#0ea5e9").Underline();
                });
            });
        }).GeneratePdf();
    }
}
