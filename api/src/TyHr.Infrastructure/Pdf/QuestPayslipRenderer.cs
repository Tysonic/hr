using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TyHr.Application.Payroll;
using TyHr.Domain.Payroll;

namespace TyHr.Infrastructure.Pdf;

/// <summary>Payslip PDFs with QuestPDF: one A4 page with the company, the employee, earnings, deductions and net pay.</summary>
public class QuestPayslipRenderer : IPayslipRenderer
{
    static QuestPayslipRenderer()
    {
        // Community licence: free for organisations under USD 1M annual revenue. Check before selling TyHR beyond that.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(PayslipDocument d)
    {
        var p = d.Payslip;
        string Money(decimal amount) => amount.ToString(d.CurrencyMinorUnits == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);
        var period = new DateTime(p.Year, p.Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            page.Header().Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(d.CompanyName).FontSize(16).Bold();
                        if (d.CompanyAddress != null) c.Item().Text(d.CompanyAddress).FontColor(Colors.Grey.Darken1);
                        if (d.CompanyTaxNumber != null) c.Item().Text($"{d.TaxNumberLabel}: {d.CompanyTaxNumber}").FontColor(Colors.Grey.Darken1);
                    });
                    row.ConstantItem(180).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("PAYSLIP").FontSize(16).Bold().FontColor(Colors.Indigo.Darken2);
                        c.Item().AlignRight().Text(period);
                        c.Item().AlignRight().Text($"Paid {p.PayDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}").FontColor(Colors.Grey.Darken1);
                    });
                });
                col.Item().PaddingVertical(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            });

            page.Content().Column(col =>
            {
                col.Spacing(14);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                    void Pair(string label, string? value) => t.Cell().PaddingBottom(3).Text(x => { x.Span($"{label}: ").FontColor(Colors.Grey.Darken1); x.Span(value ?? "—"); });
                    Pair("Employee", p.EmployeeName);
                    Pair("Employee number", p.EmployeeNumber);
                    Pair("Position", p.PositionTitle);
                    Pair("Department", p.DepartmentName);
                    Pair(d.TaxNumberLabel, p.TaxNumber);
                    Pair(d.SocialSecurityLabel, p.SocialSecurityNumber);
                    Pair("Paid to", p.PaidTo);
                    Pair("Payroll", p.RunName);
                });

                void Section(string title, IEnumerable<PayLine> lines, string totalLabel, decimal total)
                {
                    var list = lines.ToList();
                    if (list.Count == 0) return;
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(120); });
                        t.Header(h =>
                        {
                            h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).Text(title).Bold();
                            h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(3).AlignRight().Text(p.CurrencyCode).Bold();
                        });
                        foreach (var l in list)
                        {
                            t.Cell().PaddingVertical(2).Text(x =>
                            {
                                x.Span(l.Name);
                                if (l.Note != null) x.Span($"  {l.Note}").FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                            t.Cell().PaddingVertical(2).AlignRight().Text(Money(l.Amount));
                        }
                        t.Cell().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(3).Text(totalLabel).Bold();
                        t.Cell().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(3).AlignRight().Text(Money(total)).Bold();
                    });
                }

                Section("Earnings", p.Lines.Where(l => l.Kind == PayLineKind.Earning), "Gross pay", p.Gross);
                Section("Deductions", p.Lines.Where(l => l.Kind is PayLineKind.Statutory or PayLineKind.Deduction), "Total deductions", p.TotalDeductions);
                col.Item().Background(Colors.Green.Lighten4).Padding(8).Row(row =>
                {
                    row.RelativeItem().Text("NET PAY").Bold().FontSize(12);
                    row.ConstantItem(180).AlignRight().Text($"{p.CurrencyCode} {Money(p.Net)}").Bold().FontSize(12);
                });
                Section("Employer contributions (not deducted from your pay)", p.Lines.Where(l => l.Kind == PayLineKind.Employer), "Total", p.EmployerContributions);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Confidential. Generated by TyHR. ").FontSize(8).FontColor(Colors.Grey.Darken1);
                t.CurrentPageNumber().FontSize(8);
            });
        })).GeneratePdf();
    }
}
