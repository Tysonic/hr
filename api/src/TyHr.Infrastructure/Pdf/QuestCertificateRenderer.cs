using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TyHr.Application.Exits;

namespace TyHr.Infrastructure.Pdf;

/// <summary>A one-page certificate of service on the company letterhead.</summary>
public class QuestCertificateRenderer : ICertificateRenderer
{
    static QuestCertificateRenderer()
    {
        // Community licence, as for payslips (see QuestPayslipRenderer).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static string D(DateOnly d) => d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    public byte[] Render(CertificateDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(50);
        page.DefaultTextStyle(t => t.FontSize(11).LineHeight(1.5f).FontColor(Colors.Grey.Darken4));

        page.Header().Column(col =>
        {
            col.Item().Text(d.CompanyName).FontSize(18).Bold();
            var contact = string.Join(" · ", new[] { d.CompanyAddress, d.CompanyPhone, d.CompanyEmail }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (contact.Length > 0) col.Item().Text(contact).FontSize(9).FontColor(Colors.Grey.Darken1);
            col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });

        page.Content().PaddingTop(20).Column(col =>
        {
            col.Spacing(14);
            col.Item().AlignCenter().Text("CERTIFICATE OF SERVICE").FontSize(16).Bold().LetterSpacing(0.05f);
            col.Item().PaddingTop(10).Text(D(d.Issued));
            col.Item().Text("To whom it may concern").Bold();
            col.Item().Text(t =>
            {
                t.Span("This is to certify that ");
                t.Span(d.EmployeeName).Bold();
                t.Span($" (employee number {d.EmployeeNumber}) was employed by {d.CompanyName} from ");
                t.Span(D(d.StartDate)).Bold();
                t.Span(" to ");
                t.Span(D(d.EndDate)).Bold();
                t.Span(".");
            });
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c => { c.ConstantColumn(150); c.RelativeColumn(); });
                void Row(string label, string? value)
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    table.Cell().PaddingVertical(3).Text(label).FontColor(Colors.Grey.Darken1);
                    table.Cell().PaddingVertical(3).Text(value);
                }
                Row("Position", d.PositionTitle);
                Row("Department", d.DepartmentName);
                Row("Place of work", d.Workplace);
                Row("Employment began", D(d.StartDate));
                Row("Employment ended", D(d.EndDate));
            });
            col.Item().Text("This certificate is issued for the employee's records.");
            col.Item().PaddingTop(30).Width(200).LineHorizontal(0.5f);
            col.Item().Text(d.IssuedBy);
            col.Item().Text($"For {d.CompanyName}").FontSize(9).FontColor(Colors.Grey.Darken1);
        });

        page.Footer().AlignCenter().Text(t => t.Span("Certificate of service").FontSize(8).FontColor(Colors.Grey.Darken1));
    })).GeneratePdf();
}
