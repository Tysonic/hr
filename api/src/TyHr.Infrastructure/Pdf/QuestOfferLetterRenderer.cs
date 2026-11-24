using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TyHr.Application.Recruitment;

namespace TyHr.Infrastructure.Pdf;

/// <summary>Offer letters: the company letterhead, the candidate's address block, the filled-in template and a signature space.</summary>
public class QuestOfferLetterRenderer : IOfferLetterRenderer
{
    static QuestOfferLetterRenderer()
    {
        // Community licence, as for payslips (see QuestPayslipRenderer).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(OfferLetterDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(50);
        page.DefaultTextStyle(t => t.FontSize(11).LineHeight(1.4f).FontColor(Colors.Grey.Darken4));

        page.Header().Column(col =>
        {
            col.Item().Text(d.CompanyName).FontSize(18).Bold();
            var contact = string.Join(" · ", new[] { d.CompanyAddress, d.CompanyPhone, d.CompanyEmail }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (contact.Length > 0) col.Item().Text(contact).FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });

        page.Content().Column(col =>
        {
            col.Spacing(12);
            col.Item().Text(d.Date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture));
            col.Item().Column(to =>
            {
                to.Item().Text(d.CandidateName).Bold();
                to.Item().Text(d.CandidateEmail);
                if (d.CandidatePhone != null) to.Item().Text(d.CandidatePhone);
            });
            col.Item().Text($"Offer of employment: {d.JobTitle}").Bold().FontSize(13);
            foreach (var paragraph in d.Body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
                col.Item().Text(paragraph.Trim());
            col.Item().PaddingTop(10).Text("Yours sincerely,");
            col.Item().PaddingTop(30).Width(200).LineHorizontal(0.5f);
            col.Item().Text(d.SignedBy);
            col.Item().PaddingTop(30).Text("Acceptance").Bold();
            col.Item().Text($"I, {d.CandidateName}, accept this offer on the terms above.");
            col.Item().PaddingTop(25).Row(row =>
            {
                row.RelativeItem().Column(c => { c.Item().Width(180).LineHorizontal(0.5f); c.Item().Text("Signature").FontSize(9); });
                row.RelativeItem().Column(c => { c.Item().Width(140).LineHorizontal(0.5f); c.Item().Text("Date").FontSize(9); });
            });
        });

        page.Footer().AlignCenter().Text(t => t.Span("Private and confidential").FontSize(8).FontColor(Colors.Grey.Darken1));
    })).GeneratePdf();
}
