using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Exits;
using TyHr.Application.Leave;
using TyHr.Application.Payroll;
using TyHr.Application.Reports;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;
using TyHr.Domain.Payroll;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Reports. Grace, Mark and Ivan were hired 6 Jan 2025, Nora 1 Jun 2023, Zoe 2 Mar 2026; Olivia (hired 2012) resigned and
/// left 30 Jun 2026. Today is 23 Nov 2026 (fixture clock), so the 12 months run from December 2025.
/// </summary>
public class ReportsTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Services(Scope Scope, ReportsService Reports, EmployeeService Employees, ExitService Exits, PayrollService Payroll) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        return new Services(scope, new ReportsService(scope.Db, scope.TenantContext, scope.User, fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock),
            new ExitService(scope.Db, scope.TenantContext, scope.User, access, new FakeEmail(), fx.Clock, NullLogger<ExitService>.Instance),
            new PayrollService(scope.Db, scope.TenantContext, scope.User, fx.Clock));
    }

    private async Task<(string Tenant, Guid Mark)> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace", Roles.HrAdmin);
        await LeaveDefaults.SeedAsync(hr.Scope.Db, CountryPacks.Uganda, 2026, default);
        async Task<Guid> Hire(string first, string last, DateOnly? on = null, Gender gender = Gender.Female)
        {
            var r = EmployeeTests.NewEmployee(first, last, email: $"{first.ToLowerInvariant()}@co.test");
            r = r with { Personal = r.Personal with { Gender = gender } };
            if (on is { } d) r = r with { Employment = r.Employment with { HireDate = d } };
            return (await hr.Employees.CreateAsync(r, default)).Id;
        }
        await Hire("Grace", "Atim");
        var mark = await Hire("Mark", "Opio", gender: Gender.Male);
        await Hire("Ivan", "Ssali", gender: Gender.Male);
        await Hire("Nora", "Achieng", new DateOnly(2023, 6, 1));
        await Hire("Zoe", "Nabirye", new DateOnly(2026, 3, 2));
        var olivia = await Hire("Olivia", "Kato", new DateOnly(2012, 3, 1));
        await hr.Employees.AddSalaryAsync(mark, new AddSalaryRequest(new DateOnly(2025, 1, 6), 1_000_000, "Starting salary"), default);

        var exit = (await hr.Exits.CreateAsync(new CreateExitRequest(olivia, ExitType.Resignation, "Moved abroad", new DateOnly(2026, 5, 31),
            new DateOnly(2026, 6, 30), false, null), default)).Exit;
        foreach (var item in exit.Clearance) await hr.Exits.ClearAsync(item.Id, null, default);
        await hr.Exits.InterviewAsync(exit.Id, new InterviewAnswers(LeavingReason.CareerGrowth, 4, 3, 2, 2, 4, true, true, null, null, null), default);
        await hr.Exits.CompleteAsync(exit.Id, new CompleteExitRequest(true, null), default);
        return (co, mark);
    }

    [Fact]
    public async Task Headcount_and_turnover_count_people_in_post_month_by_month()
    {
        var (co, _) = await CompanyAsync();
        await using var hr = As(co, "henry", Roles.HrOfficer);

        var h = await hr.Reports.HeadcountAsync(default);
        Assert.Equal(5, h.Total);
        Assert.Equal([("Female", 3m), ("Male", 2m)], h.ByGender.Select(c => (c.Label, c.Value)));
        Assert.Equal([("Under 1 year", 1m), ("1–2 years", 3m), ("3–5 years", 1m)], h.ByService.Select(c => (c.Label, c.Value)));
        Assert.Equal([5m, 5, 5, 6, 6, 6, 6, 5, 5, 5, 5, 5], h.Trend.Select(m => m.Value));
        Assert.Equal((2025, 12), (h.Trend[0].Year, h.Trend[0].Month));

        var t = await hr.Reports.TurnoverAsync(default);
        Assert.Equal((1, 1, 1), (t.Hires, t.Leavers, t.Voluntary));
        Assert.Equal((5.3m, 18.9m), (t.AverageHeadcount, t.Rate!.Value));
        Assert.Equal(1, t.Months.Single(m => m.Month == 6).Leavers);
        Assert.Equal([("Resignation", 1m)], t.ByType.Select(c => (c.Label, c.Value)));
        Assert.Equal([("Career growth", 1m)], t.ByReason!.Select(c => (c.Label, c.Value)));
        Assert.Equal([("Over 10 years", 1m)], t.ByService.Select(c => (c.Label, c.Value)));

        await using var paul = As(co, "paul", Roles.PayrollOfficer);
        Assert.Null((await paul.Reports.TurnoverAsync(default)).ByReason);
        await using var mark = As(co, "mark", Roles.LineManager);
        Assert.Contains("can't see company reports", (await Assert.ThrowsAsync<DomainException>(() => mark.Reports.HeadcountAsync(default))).Message);
    }

    [Fact]
    public async Task Leave_liability_values_unused_annual_leave_only_for_those_who_may_see_pay()
    {
        var (co, mark) = await CompanyAsync();
        await using var paul = As(co, "paul", Roles.PayrollOfficer);
        var l = await paul.Reports.LeaveLiabilityAsync(default);
        var row = l.People.Single(p => p.EmployeeId == mark);
        Assert.True(row.Days > 0);
        Assert.Equal(46_154m, row.DailyRate); // 1,000,000 × 12 ÷ 52 ÷ 5
        Assert.Equal(Math.Round(row.Days * 46_154m), row.Amount);
        Assert.Equal(row.Amount, l.Amount); // nobody else has a salary
        Assert.Equal(l.People.Where(p => p.Days > 0).Sum(p => p.Days), l.Days);
        Assert.Contains(l.Notes, n => n.Contains("4 people have no salary"));

        await using var henry = As(co, "henry", Roles.HrOfficer);
        var hidden = await henry.Reports.LeaveLiabilityAsync(default);
        Assert.Equal((l.Days, (decimal?)null, (decimal?)null), (hidden.Days, hidden.Amount, hidden.People.Single(p => p.EmployeeId == mark).Amount));
        Assert.Null(hidden.AmountByDepartment);
    }

    [Fact]
    public async Task Payroll_cost_comes_from_approved_payrolls_and_everything_exports_to_one_workbook()
    {
        var (co, _) = await CompanyAsync();
        Guid runId;
        await using (var paul = As(co, "paul", Roles.PayrollOfficer))
        {
            Assert.Null((await paul.Reports.PayrollCostAsync(null, null, default)).Year);
            runId = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 10, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(runId, default);
            await paul.Payroll.SubmitAsync(runId, default);
            Assert.Null((await paul.Reports.PayrollCostAsync(null, null, default)).Year); // submitted isn't approved
        }
        await using (var grace = As(co, "grace", Roles.HrAdmin)) await grace.Payroll.ApproveAsync(runId, default);

        await using var payroll = As(co, "paul", Roles.PayrollOfficer);
        var c = await payroll.Reports.PayrollCostAsync(null, null, default);
        Assert.Equal((2026, 10), (c.Year, c.Month));
        var row = Assert.Single(c.ByDepartment);
        Assert.Equal((1, 1_000_000m), (row.People, row.Gross));
        Assert.True(row.EmployerContributions > 0); // NSSF 10%
        Assert.Equal(row.Gross + row.EmployerContributions, c.Total);
        Assert.Equal(1_000_000m, c.Trend.Single(m => m.Month == 10 && m.Year == 2026).Gross);
        Assert.Equal(12, c.Trend.Count);

        var file = await payroll.Reports.ExportAsync(default);
        using var book = new XLWorkbook(new MemoryStream(file.Content));
        Assert.Equal(["Headcount", "Turnover", "Leave liability", "Payroll cost"], book.Worksheets.Select(w => w.Name));
        Assert.Contains("Headcount on 23 November 2026: 5", book.Worksheet("Headcount").Cell(1, 1).GetString());

        await using var henry = As(co, "henry", Roles.HrOfficer);
        Assert.Contains("payroll costs", (await Assert.ThrowsAsync<DomainException>(() => henry.Reports.PayrollCostAsync(null, null, default))).Message);
        using var officer = new XLWorkbook(new MemoryStream((await henry.Reports.ExportAsync(default)).Content));
        Assert.DoesNotContain(officer.Worksheets, w => w.Name == "Payroll cost");
        Assert.Equal(4, officer.Worksheet("Leave liability").LastColumnUsed()!.ColumnNumber()); // no pay columns
    }
}
