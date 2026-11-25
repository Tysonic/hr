using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Payroll;

/// <summary>What a payslip PDF shows: the company and the payslip.</summary>
public record PayslipDocument(string CompanyName, string? CompanyAddress, string? CompanyTaxNumber, string TaxNumberLabel, string SocialSecurityLabel,
    int CurrencyMinorUnits, PayslipDto Payslip);

/// <summary>Draws payslip PDFs.</summary>
public interface IPayslipRenderer
{
    byte[] Render(PayslipDocument document);
}

public record OutputFile(byte[] Content, string FileName, string ContentType);
/// <param name="Failed">Those of this batch that couldn't be sent.</param>
/// <param name="Remaining">Payslips with an address after this batch, not yet tried.</param>
/// <param name="Next">Pass this to send the next batch; null when there are no more.</param>
public record EmailPayslipsResult(int Sent, int AlreadySent, IReadOnlyList<string> NoEmail, IReadOnlyList<string> Failed, bool MailConfigured, int Remaining, Guid? Next);
public record MyPayslipDto(Guid Id, string RunName, int Year, int Month, DateOnly PayDate, decimal Gross, decimal Net, string CurrencyCode);

/// <summary>
/// What a payroll produces: payslip PDFs (emailed, and for employees to download), payment files for the bank and
/// mobile money, the month's statutory returns and the accounting journal.
/// </summary>
public class PayrollOutputService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IPayslipRenderer renderer,
    IEmailSender email, TimeProvider clock, ILogger<PayrollOutputService> logger)
{
    public const string Csv = "text/csv";
    public const string Pdf = "application/pdf";

    /// <summary>Payslips emailed per request (about half a minute's work), so a big payroll goes in several requests.</summary>
    public const int EmailBatch = 100;
    public int BatchSize { get; init; } = EmailBatch;

    // Payslips

    public async Task<OutputFile> PayslipPdfAsync(Guid payslipId, CancellationToken ct)
    {
        var (slip, run) = await PayslipAsync(payslipId, ct);
        return new OutputFile(await RenderAsync(slip, run, ct), FileName(slip, run), Pdf);
    }

    /// <summary>The signed-in employee's payslips from locked payrolls, newest first.</summary>
    public async Task<IReadOnlyList<MyPayslipDto>> MyPayslipsAsync(CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var runs = await db.PayrollRuns.AsNoTracking().Where(r => r.Status == PayrollStatus.Locked && r.Kind != PayrollRunKind.Reversal).ToDictionaryAsync(r => r.Id, ct);
        var runIds = runs.Keys.ToList();
        var slips = await db.Payslips.AsNoTracking().Where(p => p.EmployeeId == me && runIds.Contains(p.RunId)).ToListAsync(ct);
        return slips.Select(p => (p, r: runs[p.RunId]))
            .OrderByDescending(x => x.r.Year).ThenByDescending(x => x.r.Month).ThenByDescending(x => x.r.PayDate)
            .Select(x => new MyPayslipDto(x.p.Id, x.r.Name, x.r.Year, x.r.Month, x.r.PayDate, x.p.Gross, x.p.Net, country.CurrencyCode)).ToList();
    }

    public async Task<PayslipDto> MyPayslipAsync(Guid id, CancellationToken ct)
    {
        var (slip, run) = await MyAsync(id, ct);
        return PayrollService.ToDto(slip, run, (await db.CompanyCountryAsync(tenantContext, ct)).CurrencyCode);
    }

    public async Task<OutputFile> MyPayslipPdfAsync(Guid id, CancellationToken ct)
    {
        var (slip, run) = await MyAsync(id, ct);
        return new OutputFile(await RenderAsync(slip, run, ct), FileName(slip, run), Pdf);
    }

    /// <summary>
    /// Emails the payslips of a locked payroll not yet sent, each to the employee's work email (or personal, if none): up
    /// to <see cref="EmailBatch"/> a call, in order. Pass the result's Next as <paramref name="after"/> to carry on from
    /// there, so payslips that fail don't hold up the rest; starting again without it retries them.
    /// </summary>
    public async Task<EmailPayslipsResult> EmailPayslipsAsync(Guid runId, Guid? after, CancellationToken ct)
    {
        var run = await RunAsync(runId, ct);
        if (run.Status != PayrollStatus.Locked) throw new DomainException("Lock the payroll before sending payslips.");
        if (run.Kind == PayrollRunKind.Reversal) throw new DomainException("Reversals don't have payslips to send.");
        var slips = (await db.Payslips.Where(p => p.RunId == runId).ToListAsync(ct)).OrderBy(s => s.EmployeeName, StringComparer.Ordinal).ThenBy(s => s.Id).ToList();
        var start = after == null ? 0 : slips.FindIndex(s => s.Id == after) + 1;
        if (after != null && start == 0) throw new NotFoundException("There is no such payslip in this payroll.");
        var ids = slips.Select(p => p.EmployeeId).ToList();
        var addresses = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.WorkEmail ?? e.PersonalEmail, ct);
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var noEmail = slips.Where(s => s.EmailedAt == null && addresses.GetValueOrDefault(s.EmployeeId) == null).Select(s => s.EmployeeName).Order().ToList();
        var due = slips.Skip(start).Where(s => s.EmailedAt == null && addresses.GetValueOrDefault(s.EmployeeId) != null).ToList();
        var batch = due.Take(BatchSize).ToList();
        var failed = new List<string>();
        var messages = new List<EmailMessage>();
        foreach (var slip in batch)
        {
            var pdf = await RenderAsync(slip, run, ct);
            var name = WebUtility.HtmlEncode(slip.EmployeeName);
            messages.Add(new EmailMessage([addresses[slip.EmployeeId]!], $"Your payslip: {run.Name}",
                $"<p>Dear {name},</p><p>Your payslip for {run.PeriodStart:MMMM yyyy} is attached. You can also see it in TyHR under My payslips.</p>" +
                $"<p>Please keep it private. If anything looks wrong, contact HR.</p><p>{WebUtility.HtmlEncode(company.TradingName ?? company.LegalName)}</p>",
                $"Dear {slip.EmployeeName},\n\nYour payslip for {run.PeriodStart:MMMM yyyy} is attached. You can also see it in TyHR under My payslips.\n\n" +
                $"Please keep it private. If anything looks wrong, contact HR.\n\n{company.TradingName ?? company.LegalName}",
                [new EmailAttachment(FileName(slip, run), Pdf, pdf)]));
        }
        var already = slips.Count(s => s.EmailedAt != null);
        var sent = 0;
        bool configured;
        try
        {
            configured = await email.SendManyAsync(messages, async (i, error) =>
            {
                if (error != null)
                {
                    logger.LogWarning(error, "Payslip email to employee {EmployeeId} failed.", batch[i].EmployeeId);
                    failed.Add(batch[i].EmployeeName);
                    return;
                }
                batch[i].MarkEmailed(clock.GetUtcNow());
                sent++;
                if (sent % 20 == 0) await db.SaveChangesAsync(CancellationToken.None);
            }, ct);
        }
        catch (MailUnavailableException e)
        {
            logger.LogError(e, "Payslip emails stopped: the mail server is unavailable.");
            throw new DomainException("The mail server can't be reached, so the remaining payslips weren't emailed. Try again later: " +
                "those already sent won't be sent twice.");
        }
        finally
        {
            // Whatever stopped it (a closed browser, the server going away), what was sent stays marked as sent.
            await db.SaveChangesAsync(CancellationToken.None);
        }
        var left = due.Count - batch.Count;
        return new EmailPayslipsResult(sent, already, noEmail, failed, configured, left, configured && left > 0 ? batch[^1].Id : null);
    }

    // Payment files

    /// <summary>
    /// A CSV for the bank's or mobile-money provider's bulk payment upload: one line per person paid that way with
    /// net pay above zero. Banks' upload formats differ; this has the usual columns to map from.
    /// </summary>
    public async Task<OutputFile> PaymentFileAsync(Guid runId, PaymentMethod method, CancellationToken ct)
    {
        var run = await RunAsync(runId, ct);
        if (!run.IsFinal) throw new DomainException("Payment files are available once the payroll is approved.");
        if (run.Kind == PayrollRunKind.Reversal) throw new DomainException("A reversal pays nobody.");
        if (method == PaymentMethod.Cash) throw new DomainException("Cash payments have no payment file; use the payslip list.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        // People without account details are left out (their payslips say so) rather than sent to the bank with blanks.
        var slips = (await db.Payslips.AsNoTracking().Where(p => p.RunId == runId && p.PaymentMethod == method && p.Net > 0).OrderBy(p => p.EmployeeName).ToListAsync(ct))
            .Where(p => method == PaymentMethod.Bank ? p.BankAccountNumber != null : p.MobileMoneyNumber != null).ToList();
        var reference = $"SAL {run.PeriodStart:MMMyy}".ToUpperInvariant();
        var csv = new StringBuilder();
        if (method == PaymentMethod.Bank)
        {
            csv.AppendLine("Reference,Employee number,Account name,Bank,Branch,Account number,Amount,Currency");
            foreach (var p in slips)
                Line(csv, $"{reference} {p.EmployeeNumber}", p.EmployeeNumber, p.BankAccountName ?? p.EmployeeName, p.BankName, p.BankBranch, p.BankAccountNumber,
                    Amount(p.Net, country), country.CurrencyCode);
        }
        else
        {
            csv.AppendLine("Reference,Employee number,Name,Provider,Phone number,Amount,Currency");
            foreach (var p in slips)
                Line(csv, $"{reference} {p.EmployeeNumber}", p.EmployeeNumber, p.EmployeeName, p.MobileMoneyProvider, p.MobileMoneyNumber?.TrimStart('+'),
                    Amount(p.Net, country), country.CurrencyCode);
        }
        logger.LogInformation("{User} downloaded the {Method} payment file for payroll {RunId}.", user.UserName, method, runId);
        var kind = method == PaymentMethod.Bank ? "bank" : "mobile-money";
        return new OutputFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            $"{kind}-payments-{run.Year}-{run.Month:00}{(run.Kind == PayrollRunKind.OffCycle ? "-off-cycle" : "")}.csv", Csv);
    }

    // Statutory returns

    /// <summary>
    /// The month's statutory schedules, one sheet each, totalled per person over every approved and locked payroll in
    /// the month (off-cycle runs and reversals included). Columns follow what the authorities ask for; copy them into
    /// their templates (URA's PAYE return, NSSF's schedule, KRA's P10) or upload where accepted.
    /// </summary>
    public async Task<OutputFile> ReturnsAsync(int year, int month, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new DomainException("Choose a month between 2000 and 2100.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var runIds = await db.PayrollRuns.AsNoTracking().Where(r => r.Year == year && r.Month == month
            && (r.Status == PayrollStatus.Approved || r.Status == PayrollStatus.Locked)).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count == 0) throw new DomainException($"There's no approved payroll for {new DateOnly(year, month, 1):MMMM yyyy}.");
        var people = (await db.Payslips.AsNoTracking().Where(p => runIds.Contains(p.RunId)).ToListAsync(ct))
            .GroupBy(p => p.EmployeeId)
            .Select(g =>
            {
                var latest = g.Last();
                return new ReturnRow(latest.EmployeeNumber, latest.EmployeeName, g.Select(p => p.TaxNumber).LastOrDefault(t => t != null),
                    g.Select(p => p.SocialSecurityNumber).LastOrDefault(t => t != null), g.Sum(p => p.Gross), g.Sum(p => p.TaxableGross), g.Sum(p => p.PensionableGross),
                    g.SelectMany(p => p.Lines).Where(l => l.Kind is PayLineKind.Statutory or PayLineKind.Employer)
                        .GroupBy(l => l.Code).ToDictionary(c => c.Key, c => c.Sum(l => l.Amount)));
            })
            .Where(r => r.Gross != 0 || r.Amounts.Values.Any(v => v != 0))
            .OrderBy(r => r.Name).ToList();

        using var book = new XLWorkbook();
        foreach (var sheet in StatutoryReturns.For(country))
        {
            var rows = people.Where(sheet.Include).ToList();
            if (rows.Count == 0 && sheet.Optional) continue;
            var ws = book.AddWorksheet(sheet.Name);
            ws.Cell(1, 1).Value = $"{company.LegalName} · {sheet.Title} · {new DateOnly(year, month, 1):MMMM yyyy}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(2, 1).Value = $"Employer {country.TaxNumberLabel}: {company.TaxNumber ?? "(not set in Settings → Company)"}"
                                  + (sheet.Name.Contains("NSSF") ? $" · Employer NSSF number: {company.SocialSecurityNumber ?? "(not set)"}" : "");
            for (var c = 0; c < sheet.Columns.Count; c++)
            {
                ws.Cell(4, c + 1).Value = sheet.Columns[c].Heading;
                ws.Cell(4, c + 1).Style.Font.Bold = true;
            }
            var r = 5;
            foreach (var row in rows)
            {
                for (var c = 0; c < sheet.Columns.Count; c++) Set(ws.Cell(r, c + 1), sheet.Columns[c].Value(row));
                r++;
            }
            ws.Cell(r, 1).Value = "Total";
            ws.Cell(r, 1).Style.Font.Bold = true;
            for (var c = 0; c < sheet.Columns.Count; c++)
                if (sheet.Columns[c].Total)
                {
                    ws.Cell(r, c + 1).Value = rows.Sum(x => sheet.Columns[c].Value(x) as decimal? ?? 0);
                    ws.Cell(r, c + 1).Style.Font.Bold = true;
                }
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(4);
        }
        return new OutputFile(Save(book), $"statutory-returns-{year}-{month:00}.xlsx", Spreadsheet.ContentType);
    }

    // Journal

    /// <summary>
    /// The payroll's accounting entry: salary and employer costs by department on the debit side; what's owed to the
    /// tax authority, NSSF, other deductions' payees and employees (net pay) on the credit side. Debits equal credits.
    /// Account names are generic; map them to your chart of accounts when importing.
    /// </summary>
    public async Task<OutputFile> JournalAsync(Guid runId, CancellationToken ct)
    {
        var run = await RunAsync(runId, ct);
        if (!run.IsFinal) throw new DomainException("The journal is available once the payroll is approved.");
        var lines = await JournalLinesAsync(run, ct);
        using var book = new XLWorkbook();
        var ws = book.AddWorksheet("Journal");
        string[] headings = ["Date", "Account", "Department", "Description", "Debit", "Credit"];
        for (var c = 0; c < headings.Length; c++) ws.Cell(1, c + 1).Value = headings[c];
        ws.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var l in lines)
        {
            ws.Cell(r, 1).Value = run.PayDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd";
            ws.Cell(r, 2).Value = l.Account;
            ws.Cell(r, 3).Value = l.Department ?? "";
            ws.Cell(r, 4).Value = l.Description;
            if (l.Debit != 0) ws.Cell(r, 5).Value = l.Debit;
            if (l.Credit != 0) ws.Cell(r, 6).Value = l.Credit;
            r++;
        }
        ws.Cell(r, 4).Value = "Total";
        ws.Cell(r, 5).Value = lines.Sum(l => l.Debit);
        ws.Cell(r, 6).Value = lines.Sum(l => l.Credit);
        ws.Row(r).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return new OutputFile(Save(book), $"payroll-journal-{run.Year}-{run.Month:00}{(run.Kind == PayrollRunKind.Regular ? "" : "-" + run.Kind.ToString().ToLowerInvariant())}.xlsx",
            Spreadsheet.ContentType);
    }

    public async Task<IReadOnlyList<JournalLine>> JournalLinesAsync(PayrollRun run, CancellationToken ct)
    {
        var slips = await db.Payslips.AsNoTracking().Where(p => p.RunId == run.Id).ToListAsync(ct);
        var description = run.Name;
        var raw = new List<(string Account, string? Department, decimal Amount)>(); // positive = debit
        foreach (var dept in slips.GroupBy(p => p.DepartmentName))
        {
            raw.Add(("Salaries and wages", dept.Key, dept.Sum(p => p.Gross)));
            raw.Add(("Employer payroll contributions", dept.Key, dept.Sum(p => p.EmployerContributions)));
        }
        foreach (var code in slips.SelectMany(p => p.Lines).Where(l => l.Kind is PayLineKind.Statutory or PayLineKind.Employer or PayLineKind.Deduction)
                     .GroupBy(l => PayableAccount(l)))
            raw.Add((code.Key, null, -code.Sum(l => l.Amount)));
        raw.Add(("Net salaries payable", null, -slips.Sum(p => p.Net)));
        return raw.Where(x => x.Amount != 0)
            .Select(x => new JournalLine(x.Account, x.Department, description, x.Amount > 0 ? x.Amount : 0, x.Amount < 0 ? -x.Amount : 0)).ToList();

        static string PayableAccount(PayLine l) => l.Code switch
        {
            "PAYE" => "PAYE payable",
            "NSSF" or "NSSF_ER" => "NSSF payable",
            "AHL" or "AHL_ER" => "Housing levy payable",
            "SHIF" => "SHIF payable",
            "NITA" => "NITA payable",
            "LST" => "Local service tax payable",
            _ => $"{l.Name} payable",
        };
    }

    // Helpers

    private async Task<byte[]> RenderAsync(Payslip slip, PayrollRun run, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        return renderer.Render(new PayslipDocument(company.TradingName ?? company.LegalName, company.PhysicalAddress ?? company.PostalAddress,
            company.TaxNumber, country.TaxNumberLabel, country.SocialSecurityLabel, country.CurrencyMinorUnits, PayrollService.ToDto(slip, run, country.CurrencyCode)));
    }

    private static string FileName(Payslip slip, PayrollRun run) =>
        $"payslip-{slip.EmployeeNumber}-{run.Year}-{run.Month:00}{(run.Kind == PayrollRunKind.Regular ? "" : "-" + run.Kind.ToString().ToLowerInvariant())}.pdf";

    private async Task<(Payslip, PayrollRun)> PayslipAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var slip = await db.Payslips.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("There is no such payslip.");
        return (slip, await db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == slip.RunId, ct));
    }

    /// <summary>The employee's own payslip, only once its payroll is locked.</summary>
    private async Task<(Payslip, PayrollRun)> MyAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(ct);
        var (slip, run) = await PayslipAsync(id, ct);
        if (slip.EmployeeId != me || run.Status != PayrollStatus.Locked || run.Kind == PayrollRunKind.Reversal) throw new NotFoundException("There is no such payslip.");
        return (slip, run);
    }

    private async Task<Guid> MeAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await access.MeAsync(ct) ?? throw new DomainException("Your sign-in isn't linked to an employee record. Ask HR to set your work email on your record.");
    }

    private async Task<PayrollRun> RunAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.PayrollRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("There is no such payroll.");
    }

    private static string Amount(decimal value, CountryPack country) => value.ToString(country.CurrencyMinorUnits == 0 ? "0" : "0.00", CultureInfo.InvariantCulture);

    /// <summary>Writes a CSV line, quoting fields and defusing ones a spreadsheet would run as a formula.</summary>
    private static void Line(StringBuilder csv, params string?[] fields) =>
        csv.AppendLine(string.Join(",", fields.Select(f =>
        {
            var v = f ?? "";
            if (v.Length > 0 && "=+-@\t\r".Contains(v[0]) && !decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) v = "'" + v;
            return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 || v != v.Trim() ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        })));

    private static void Set(IXLCell cell, object? value)
    {
        switch (value)
        {
            case decimal d: cell.Value = d; break;
            case null: break;
            default: cell.SetValue(value.ToString()); break;
        }
    }

    private static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}

public record JournalLine(string Account, string? Department, string Description, decimal Debit, decimal Credit);

/// <summary>One person's month for the statutory returns.</summary>
public record ReturnRow(string EmployeeNumber, string Name, string? TaxNumber, string? SocialSecurityNumber, decimal Gross, decimal TaxableGross,
    decimal Pensionable, IReadOnlyDictionary<string, decimal> Amounts)
{
    public decimal this[string code] => Amounts.GetValueOrDefault(code);
}

public record ReturnColumn(string Heading, Func<ReturnRow, object?> Value, bool Total = false);

public record ReturnSheet(string Name, string Title, IReadOnlyList<ReturnColumn> Columns, Func<ReturnRow, bool> Include, bool Optional = false);

/// <summary>Each country's monthly schedules and their columns.</summary>
public static class StatutoryReturns
{
    public static IReadOnlyList<ReturnSheet> For(CountryPack country) => country.Code switch
    {
        "UG" =>
        [
            new("PAYE", "PAYE schedule", [Text("TIN", r => r.TaxNumber), Text("Employee number", r => r.EmployeeNumber), Text("Name", r => r.Name),
                Money("Gross pay", r => r.Gross), Money("Chargeable income", r => r.TaxableGross), Money("PAYE", r => r["PAYE"])], _ => true),
            new("NSSF", "NSSF contribution schedule", [Text("NSSF number", r => r.SocialSecurityNumber), Text("Employee number", r => r.EmployeeNumber),
                Text("Name", r => r.Name), Money("Gross pay", r => r.Pensionable), Money("Employee 5%", r => r["NSSF"]), Money("Employer 10%", r => r["NSSF_ER"]),
                Money("Total", r => r["NSSF"] + r["NSSF_ER"])], r => r["NSSF"] != 0 || r["NSSF_ER"] != 0),
            new("LST", "Local service tax schedule", [Text("Employee number", r => r.EmployeeNumber), Text("Name", r => r.Name), Text("TIN", r => r.TaxNumber),
                Money("Gross pay", r => r.Gross), Money("Local service tax", r => r["LST"])], r => r["LST"] != 0, Optional: true),
        ],
        "KE" =>
        [
            new("PAYE", "PAYE schedule (P10)", [Text("KRA PIN", r => r.TaxNumber), Text("Employee number", r => r.EmployeeNumber), Text("Name", r => r.Name),
                Money("Gross pay", r => r.Gross), Money("NSSF", r => r["NSSF"]), Money("SHIF", r => r["SHIF"]), Money("Housing levy", r => r["AHL"]),
                Money("Taxable pay", r => r.TaxableGross - r["NSSF"] - r["SHIF"] - r["AHL"]), Money("PAYE (after relief)", r => r["PAYE"])], _ => true),
            new("NSSF", "NSSF contribution schedule", [Text("NSSF number", r => r.SocialSecurityNumber), Text("Employee number", r => r.EmployeeNumber),
                Text("Name", r => r.Name), Money("Pensionable pay", r => r.Pensionable), Money("Employee", r => r["NSSF"]), Money("Employer", r => r["NSSF_ER"]),
                Money("Total", r => r["NSSF"] + r["NSSF_ER"])], r => r["NSSF"] != 0),
            new("SHIF", "SHIF schedule", [Text("Employee number", r => r.EmployeeNumber), Text("Name", r => r.Name), Text("KRA PIN", r => r.TaxNumber),
                Money("Gross pay", r => r.Gross), Money("SHIF", r => r["SHIF"])], r => r["SHIF"] != 0),
            new("Housing levy", "Affordable Housing Levy schedule", [Text("KRA PIN", r => r.TaxNumber), Text("Employee number", r => r.EmployeeNumber),
                Text("Name", r => r.Name), Money("Gross pay", r => r.Gross), Money("Employee 1.5%", r => r["AHL"]), Money("Employer 1.5%", r => r["AHL_ER"]),
                Money("Total", r => r["AHL"] + r["AHL_ER"])], r => r["AHL"] != 0),
            new("NITA", "NITA levy", [Text("Employee number", r => r.EmployeeNumber), Text("Name", r => r.Name), Money("NITA", r => r["NITA"])],
                r => r["NITA"] != 0, Optional: true),
        ],
        _ => throw new DomainException($"TyHR has no statutory returns for {country.Name}."),
    };

    private static ReturnColumn Text(string heading, Func<ReturnRow, string?> value) => new(heading, r => value(r));
    private static ReturnColumn Money(string heading, Func<ReturnRow, decimal> value) => new(heading, r => value(r), Total: true);
}
