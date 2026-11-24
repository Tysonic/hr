using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;
using TyHr.Domain.Leave;
using TyHr.Domain.Payroll;

namespace TyHr.Application.Reports;

public record Count(string Label, decimal Value);
public record MonthValue(int Year, int Month, decimal Value);

public record HeadcountReport(DateOnly AsOf, int Total, int Suspended, IReadOnlyList<Count> ByDepartment, IReadOnlyList<Count> ByBranch,
    IReadOnlyList<Count> ByContract, IReadOnlyList<Count> ByGender, IReadOnlyList<Count> ByAge, IReadOnlyList<Count> ByService, IReadOnlyList<MonthValue> Trend);

public record TurnoverMonth(int Year, int Month, int Hires, int Leavers, int Voluntary, int Headcount);

/// <param name="Rate">Leavers in the 12 months as a percentage of the average month-end headcount.</param>
/// <param name="ByReason">The main reason from exit interviews; only for those who handle exits.</param>
public record TurnoverReport(DateOnly From, DateOnly To, int Hires, int Leavers, int Voluntary, decimal AverageHeadcount, decimal? Rate, decimal? VoluntaryRate,
    IReadOnlyList<TurnoverMonth> Months, IReadOnlyList<Count> ByType, IReadOnlyList<Count> ByDepartment, IReadOnlyList<Count>? ByReason,
    IReadOnlyList<Count> ByService);

public record LiabilityRow(Guid EmployeeId, string EmployeeNumber, string Name, string? Department, decimal Days, decimal? DailyRate, decimal? Amount);

/// <param name="Amount">Null when the user may not see pay.</param>
public record LeaveLiabilityReport(DateOnly AsOf, string? LeaveType, decimal Days, decimal? Amount, IReadOnlyList<Count> DaysByDepartment,
    IReadOnlyList<Count>? AmountByDepartment, IReadOnlyList<LiabilityRow> People, string CurrencyCode, IReadOnlyList<string> Notes);

public record PayrollCostRow(string Department, int People, decimal Gross, decimal EmployerContributions, decimal Total);
public record PayrollCostMonth(int Year, int Month, decimal Gross, decimal EmployerContributions, decimal Net);

/// <param name="Year">The month shown: the one asked for, else the latest with an approved or locked payroll; null when there's none.</param>
public record PayrollCostReport(int? Year, int? Month, IReadOnlyList<PayrollCostRow> ByDepartment, decimal Gross, decimal EmployerContributions,
    decimal Total, IReadOnlyList<PayrollCostMonth> Trend, string CurrencyCode);

/// <summary>
/// Company reports for HR and payroll: headcount, turnover, leave liability and payroll cost, on screen and as one Excel
/// workbook. Pay figures need <see cref="Permissions.EmployeesPay"/> (leave liability) or <see cref="Permissions.PayrollView"/>
/// (payroll cost); leaving reasons need <see cref="Permissions.ExitsManage"/>. Everything is worked out in memory from
/// the company's records, which stays quick at a few thousand employees.
/// </summary>
public class ReportsService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, TimeProvider clock)
{
    private static readonly ExitType[] VoluntaryTypes = [ExitType.Resignation, ExitType.Retirement];
    private const string NoDepartment = "No department";

    public async Task<HeadcountReport> HeadcountAsync(CancellationToken ct)
    {
        var data = await LoadAsync(ct);
        var today = data.Today;
        var current = data.People.Where(p => p.InPost(today)).ToList();
        var contracts = (await db.EmploymentContracts.AsNoTracking().ToListAsync(ct)).Where(c => c.StartDate <= today)
            .GroupBy(c => c.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(c => c.StartDate)!.Type);
        return new HeadcountReport(today, current.Count, current.Count(p => p.Employee.Status == EmployeeStatus.Suspended),
            Group(current, p => p.Department ?? NoDepartment),
            Group(current, p => p.Branch ?? "No branch"),
            Group(current, p => contracts.TryGetValue(p.Employee.Id, out var t) ? ContractLabel(t) : "No contract"),
            Group(current, p => p.Employee.Gender.ToString()),
            Ordered(current, p => AgeBand(p.Employee.DateOfBirth, today), ["Under 25", "25–34", "35–44", "45–54", "55 and over", "Not recorded"]),
            Ordered(current, p => ServiceBand(p.Employee.HireDate, today), ServiceBands),
            Months(today).Select(m => new MonthValue(m.Year, m.Month, data.People.Count(p => p.InPost(MonthEnd(m, today))))).ToList());
    }

    public async Task<TurnoverReport> TurnoverAsync(CancellationToken ct)
    {
        var data = await LoadAsync(ct);
        var today = data.Today;
        var months = Months(today);
        var from = months[0];
        var leavers = data.People.Where(p => p.LeftOn is { } d && d >= from && d <= today).ToList();
        var rows = months.Select(m =>
        {
            var end = MonthEnd(m, today);
            bool In(DateOnly d) => d >= m && d <= end;
            var left = leavers.Where(p => In(p.LeftOn!.Value)).ToList();
            return new TurnoverMonth(m.Year, m.Month, data.People.Count(p => In(p.Employee.HireDate)), left.Count,
                left.Count(p => VoluntaryTypes.Contains(p.Exit!.Type)), data.People.Count(p => p.InPost(end)));
        }).ToList();
        var average = Math.Round((decimal)rows.Average(r => r.Headcount), 1);
        decimal? Rate(int n) => average == 0 ? null : Math.Round(n * 100m / average, 1);
        IReadOnlyList<Count>? reasons = null;
        if (user.Can(Permissions.ExitsManage))
        {
            var ids = leavers.Select(p => p.Exit!.Id).ToList();
            var interviews = await db.ExitInterviews.AsNoTracking().Where(i => ids.Contains(i.ExitId) && i.MainReason != null).Select(i => i.MainReason!.Value).ToListAsync(ct);
            reasons = interviews.GroupBy(r => r).Select(g => new Count(ReasonLabel(g.Key), g.Count())).OrderByDescending(c => c.Value).ToList();
        }
        var voluntary = leavers.Count(p => VoluntaryTypes.Contains(p.Exit!.Type));
        return new TurnoverReport(from, today, rows.Sum(r => r.Hires), leavers.Count, voluntary, average, Rate(leavers.Count), Rate(voluntary), rows,
            Group(leavers, p => ExitLabel(p.Exit!.Type)), Group(leavers, p => p.Department ?? NoDepartment), reasons,
            Ordered(leavers, p => ServiceBand(p.Employee.HireDate, p.LeftOn!.Value), ServiceBands));
    }

    public async Task<LeaveLiabilityReport> LeaveLiabilityAsync(CancellationToken ct)
    {
        var data = await LoadAsync(ct);
        var today = data.Today;
        var showPay = user.Can(Permissions.EmployeesPay);
        var notes = new List<string>();
        var annual = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Code == "ANNUAL", ct);
        if (annual is not { HasBalance: true })
            return new LeaveLiabilityReport(today, null, 0, showPay ? 0 : null, [], showPay ? [] : null, [], data.Country.CurrencyCode,
                ["There's no leave type with the code ANNUAL and a balance, so there's no leave liability to work out."]);

        var current = data.People.Where(p => p.InPost(today)).ToList();
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var holidays = await db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct);
        var calendar = new LeaveCalendar(profile.WorkWeek, holidays, profile.FinancialYearStartMonth);
        var year = calendar.LeaveYearOf(today);
        var requests = (await db.LeaveRequests.AsNoTracking().Where(r => r.LeaveTypeId == annual.Id).ToListAsync(ct)).ToLookup(r => r.EmployeeId);
        var adjustments = (await db.LeaveAdjustments.AsNoTracking().Where(a => a.LeaveTypeId == annual.Id).ToListAsync(ct)).ToLookup(a => a.EmployeeId);
        var salaries = showPay
            ? (await db.SalaryChanges.AsNoTracking().Where(s => s.EffectiveDate <= today).ToListAsync(ct))
                .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.EffectiveDate)!.MonthlyBasic)
            : [];
        var workingDays = profile.WorkWeek.Days.Count();
        var rows = current.Select(p =>
        {
            var days = new LeaveBalances(calendar, annual, p.Employee.HireDate, requests[p.Employee.Id].ToList(), adjustments[p.Employee.Id].ToList())
                .For(year, today).Available;
            decimal? rate = showPay && salaries.TryGetValue(p.Employee.Id, out var basic) ? data.Country.Round(FinalDues.DailyRate(basic, workingDays)) : null;
            return new LiabilityRow(p.Employee.Id, p.Employee.EmployeeNumber, p.Employee.FullName, p.Department, days, rate,
                rate == null || days <= 0 ? (showPay ? 0 : null) : data.Country.Round(days * rate.Value));
        }).OrderByDescending(r => r.Amount ?? 0).ThenByDescending(r => r.Days).ToList();
        if (current.Count(p => !salaries.ContainsKey(p.Employee.Id)) is var unpaid && showPay && unpaid > 0)
            notes.Add(unpaid == 1 ? "1 person has no salary on record, so their days aren't valued."
                : $"{unpaid} people have no salary on record, so their days aren't valued.");
        notes.Add("Unused days earned so far this leave year, including any carried over, less leave approved or waiting for approval. "
                  + "Days taken in advance count as nothing owed. Valued at basic × 12 ÷ 52 ÷ working days a week, as on leaving.");
        var owed = rows.Where(r => r.Days > 0).ToList();
        return new LeaveLiabilityReport(today, annual.Name, owed.Sum(r => r.Days), showPay ? owed.Sum(r => r.Amount ?? 0) : null,
            owed.GroupBy(r => r.Department ?? NoDepartment).Select(g => new Count(g.Key, g.Sum(r => r.Days))).OrderByDescending(c => c.Value).ToList(),
            showPay ? owed.GroupBy(r => r.Department ?? NoDepartment).Select(g => new Count(g.Key, g.Sum(r => r.Amount ?? 0))).OrderByDescending(c => c.Value).ToList() : null,
            rows, data.Country.CurrencyCode, notes);
    }

    public async Task<PayrollCostReport> PayrollCostAsync(int? year, int? month, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!user.Can(Permissions.PayrollView)) throw new DomainException("Only payroll and HR admins can see payroll costs.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var runs = await db.PayrollRuns.AsNoTracking().Where(r => r.Status == PayrollStatus.Approved || r.Status == PayrollStatus.Locked)
            .Select(r => new { r.Id, r.Year, r.Month }).ToListAsync(ct);
        if (year == null || month == null)
        {
            var latest = runs.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).FirstOrDefault();
            (year, month) = (latest?.Year, latest?.Month);
        }
        if (year == null || month == null) return new PayrollCostReport(null, null, [], 0, 0, 0, [], country.CurrencyCode);
        if (month is < 1 or > 12 || year is < 2000 or > 2100) throw new DomainException("Choose a month between 2000 and 2100.");

        var shown = new DateOnly(year.Value, month.Value, 1);
        var window = Enumerable.Range(0, 12).Select(i => shown.AddMonths(i - 11)).ToList();
        var ids = runs.Where(r => window.Contains(new DateOnly(r.Year, r.Month, 1))).Select(r => r.Id).ToList();
        var monthOf = runs.ToDictionary(r => r.Id, r => new DateOnly(r.Year, r.Month, 1));
        var slips = (await db.Payslips.AsNoTracking().Where(p => ids.Contains(p.RunId))
            .Select(p => new { p.RunId, p.EmployeeId, p.DepartmentName, p.Gross, p.EmployerContributions, p.Net }).ToListAsync(ct))
            .Select(p => (Month: monthOf[p.RunId], p.EmployeeId, Department: p.DepartmentName ?? NoDepartment, p.Gross, p.EmployerContributions, p.Net)).ToList();
        var inMonth = slips.Where(s => s.Month == shown).ToList();
        var rows = inMonth.GroupBy(s => s.Department).Select(g => new PayrollCostRow(g.Key,
                g.GroupBy(s => s.EmployeeId).Count(e => e.Sum(s => s.Gross) != 0), g.Sum(s => s.Gross), g.Sum(s => s.EmployerContributions),
                g.Sum(s => s.Gross + s.EmployerContributions)))
            .OrderByDescending(r => r.Total).ToList();
        var trend = window.Select(m => slips.Where(s => s.Month == m).ToList())
            .Select((s, i) => new PayrollCostMonth(window[i].Year, window[i].Month, s.Sum(x => x.Gross), s.Sum(x => x.EmployerContributions), s.Sum(x => x.Net)))
            .ToList();
        return new PayrollCostReport(year, month, rows, rows.Sum(r => r.Gross), rows.Sum(r => r.EmployerContributions), rows.Sum(r => r.Total), trend,
            country.CurrencyCode);
    }

    /// <summary>All the reports the user may see, one sheet each, as plain values (text is never read as a formula).</summary>
    public async Task<Spreadsheet> ExportAsync(CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var money = country.CurrencyMinorUnits == 0 ? "#,##0" : "#,##0.00";
        using var book = new XLWorkbook();

        var h = await HeadcountAsync(ct);
        var sheet = book.AddWorksheet("Headcount");
        var row = Title(sheet, $"Headcount on {h.AsOf:d MMMM yyyy}: {h.Total}" + (h.Suspended > 0 ? $" ({h.Suspended} suspended)" : ""));
        foreach (var (name, counts) in new[] { ("Department", h.ByDepartment), ("Branch", h.ByBranch), ("Contract", h.ByContract), ("Gender", h.ByGender),
                     ("Age", h.ByAge), ("Length of service", h.ByService) })
            row = Counts(sheet, row, name, "People", counts);
        row = Header(sheet, row, "Month end", "Headcount");
        foreach (var m in h.Trend) row = Line(sheet, row, MonthLabel(m.Year, m.Month), m.Value);
        Fit(sheet);

        var t = await TurnoverAsync(ct);
        sheet = book.AddWorksheet("Turnover");
        row = Title(sheet, $"Turnover, {t.From:d MMM yyyy} to {t.To:d MMM yyyy}: {t.Leavers} leavers ({t.Voluntary} voluntary), {t.Hires} hires, "
                           + $"average headcount {t.AverageHeadcount}, turnover {(t.Rate is { } r ? $"{r}%" : "n/a")}");
        row = Header(sheet, row, "Month", "Hires", "Leavers", "Voluntary", "Headcount at month end");
        foreach (var m in t.Months) row = Line(sheet, row, MonthLabel(m.Year, m.Month), m.Hires, m.Leavers, m.Voluntary, m.Headcount);
        row++;
        row = Counts(sheet, row, "Left as", "Leavers", t.ByType);
        row = Counts(sheet, row, "Department", "Leavers", t.ByDepartment);
        row = Counts(sheet, row, "Length of service", "Leavers", t.ByService);
        if (t.ByReason != null) Counts(sheet, row, "Main reason (exit interviews)", "Leavers", t.ByReason);
        Fit(sheet);

        var l = await LeaveLiabilityAsync(ct);
        sheet = book.AddWorksheet("Leave liability");
        row = Title(sheet, $"Unused {l.LeaveType ?? "annual leave"} on {l.AsOf:d MMMM yyyy}: {l.Days} days" + (l.Amount is { } a ? $", {country.CurrencyCode} {a.ToString(money == "#,##0" ? "N0" : "N2")}" : ""));
        foreach (var note in l.Notes) sheet.Cell(row++, 1).SetValue(note);
        row++;
        string[] cols = l.Amount == null ? ["Employee number", "Name", "Department", "Days"] : ["Employee number", "Name", "Department", "Days", "Daily rate", "Value"];
        row = Header(sheet, row, cols);
        foreach (var p in l.People)
            row = l.Amount == null ? Line(sheet, row, p.EmployeeNumber, p.Name, p.Department, p.Days)
                : Line(sheet, row, p.EmployeeNumber, p.Name, p.Department, p.Days, (p.DailyRate), (p.Amount));
        if (l.Amount != null) sheet.Columns(5, 6).Style.NumberFormat.Format = money;
        Fit(sheet);

        if (user.Can(Permissions.PayrollView))
        {
            var c = await PayrollCostAsync(null, null, ct);
            sheet = book.AddWorksheet("Payroll cost");
            row = Title(sheet, c.Year == null ? "No approved or locked payroll yet." : $"Payroll cost, {MonthLabel(c.Year.Value, c.Month!.Value)} (approved and locked payrolls), {country.CurrencyCode}");
            row = Header(sheet, row, "Department", "People paid", "Gross pay", "Employer contributions", "Total cost");
            foreach (var d in c.ByDepartment) row = Line(sheet, row, d.Department, d.People, (d.Gross), (d.EmployerContributions), (d.Total));
            row = Line(sheet, row, "Total", null, (c.Gross), (c.EmployerContributions), (c.Total));
            sheet.Row(row - 1).Style.Font.Bold = true;
            row++;
            row = Header(sheet, row, "Month", "", "Gross pay", "Employer contributions", "Net pay");
            foreach (var m in c.Trend) row = Line(sheet, row, MonthLabel(m.Year, m.Month), null, (m.Gross), (m.EmployerContributions), (m.Net));
            sheet.Columns(3, 5).Style.NumberFormat.Format = money;
            Fit(sheet);
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return new Spreadsheet(stream.ToArray(), $"tyhr-reports-{tenant}-{h.AsOf:yyyy-MM-dd}.xlsx");
    }

    // Data

    private sealed record Person(Employee Employee, string? Department, string? Branch, EmployeeExit? Exit, DateOnly? LeftOn)
    {
        /// <summary>Employed on that day: hired by then and not yet gone (an exited person with no completed exit counts as gone).</summary>
        public bool InPost(DateOnly day) => Employee.HireDate <= day
            && (Employee.Status != EmployeeStatus.Exited ? LeftOn == null || LeftOn >= day : LeftOn != null && LeftOn >= day);
    }

    private sealed record Data(DateOnly Today, CountryPack Country, List<Person> People);

    private async Task<Data> LoadAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!user.Can(Permissions.ReportsView)) throw new DomainException("You can't see company reports.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var today = DateOnly.FromDateTime(country.ToLocal(clock.GetUtcNow()).DateTime);
        var employees = await db.Employees.AsNoTracking().ToListAsync(ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var exits = (await db.EmployeeExits.AsNoTracking().Where(x => x.Status == ExitStatus.Completed).ToListAsync(ct))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.LastWorkingDay)!);
        var people = employees.Select(e =>
        {
            // A completed exit belongs to an earlier spell of employment if the person was rehired after it.
            var exit = exits.GetValueOrDefault(e.Id) is { } x && (e.Status == EmployeeStatus.Exited || x.LastWorkingDay >= e.HireDate) ? x : null;
            return new Person(e, e.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null, e.BranchId is { } b ? branches.GetValueOrDefault(b) : null,
                exit, exit?.LastWorkingDay);
        }).ToList();
        return new Data(today, country, people);
    }

        private static List<DateOnly> Months(DateOnly today)
    {
        var first = new DateOnly(today.Year, today.Month, 1);
        return Enumerable.Range(0, 12).Select(i => first.AddMonths(i - 11)).ToList();
    }

    /// <summary>The month's last day, or today for the current month.</summary>
    private static DateOnly MonthEnd(DateOnly month, DateOnly today) => month.AddMonths(1).AddDays(-1) is var end && end > today ? today : end;

    private static List<Count> Group<T>(IEnumerable<T> items, Func<T, string> key) =>
        items.GroupBy(key).Select(g => new Count(g.Key, g.Count())).OrderByDescending(c => c.Value).ThenBy(c => c.Label, StringComparer.Ordinal).ToList();

    private static List<Count> Ordered<T>(IEnumerable<T> items, Func<T, string> key, string[] order)
    {
        var counts = items.GroupBy(key).ToDictionary(g => g.Key, g => g.Count());
        return order.Where(counts.ContainsKey).Select(k => new Count(k, counts[k])).ToList();
    }

    private static readonly string[] ServiceBands = ["Under 1 year", "1–2 years", "3–5 years", "6–10 years", "Over 10 years"];

    private static int Years(DateOnly from, DateOnly to) => to.Year - from.Year - (to < from.AddYears(to.Year - from.Year) ? 1 : 0);

    private static string AgeBand(DateOnly? born, DateOnly today) => born is not { } b ? "Not recorded" : Years(b, today) switch
    {
        < 25 => "Under 25", < 35 => "25–34", < 45 => "35–44", < 55 => "45–54", _ => "55 and over",
    };

    private static string ServiceBand(DateOnly hired, DateOnly on) => Years(hired, on) switch
    {
        < 1 => ServiceBands[0], < 3 => ServiceBands[1], < 6 => ServiceBands[2], <= 10 => ServiceBands[3], _ => ServiceBands[4],
    };

    private static string ContractLabel(ContractType t) => t switch { ContractType.FixedTerm => "Fixed term", _ => t.ToString() };

    private static string ExitLabel(ExitType t) => t switch { ExitType.EndOfContract => "End of contract", _ => t.ToString() };

    private static string ReasonLabel(LeavingReason r) => r switch
    {
        LeavingReason.CareerGrowth => "Career growth", LeavingReason.WorkEnvironment => "Work environment", LeavingReason.WorkLifeBalance => "Work–life balance",
        _ => r.ToString(),
    };

    private static string MonthLabel(int year, int month) => new DateOnly(year, month, 1).ToString("MMM yyyy");

    // Sheets

    private static int Title(IXLWorksheet sheet, string text)
    {
        sheet.Cell(1, 1).SetValue(text);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 13;
        return 3;
    }

    private static int Header(IXLWorksheet sheet, int row, params string[] headings)
    {
        for (var c = 0; c < headings.Length; c++) sheet.Cell(row, c + 1).SetValue(headings[c]);
        sheet.Row(row).Style.Font.Bold = true;
        return row + 1;
    }

    private static int Line(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var c = 0; c < values.Length; c++)
        {
            var cell = sheet.Cell(row, c + 1);
            switch (values[c])
            {
                case string s: cell.SetValue(s); break;
                case decimal d: cell.Value = d; break;
                case int i: cell.Value = i; break;
            }
        }
        return row + 1;
    }

    private static int Counts(IXLWorksheet sheet, int row, string label, string valueLabel, IReadOnlyList<Count> counts)
    {
        row = Header(sheet, row, label, valueLabel);
        foreach (var c in counts) row = Line(sheet, row, c.Label, c.Value);
        return row + 1;
    }

    private static void Fit(IXLWorksheet sheet)
    {
        sheet.Columns().AdjustToContents(2, 500, 8, 45);
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 22);
    }
}
