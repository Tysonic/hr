using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Leave;
using TyHr.Application.Payroll;
using TyHr.Application.Reports;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Organization;
using TyHr.Domain.Payroll;
using TyHr.Infrastructure.Pdf;
using TyHr.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace TyHr.Tests;

/// <summary>Runs only when TYHR_LOAD is set (e.g. TYHR_LOAD=5000 dotnet test --filter LoadTests): it takes a few minutes.</summary>
public sealed class LoadFactAttribute : FactAttribute
{
    public LoadFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TYHR_LOAD"))) Skip = "Load test: set TYHR_LOAD to the number of employees to run it.";
    }
}

/// <summary>
/// A company of thousands (5,000 by default) through a month's payroll and the reports, timed. The limits are generous
/// so a slow machine passes; the timings printed are what to watch.
/// </summary>
public class LoadTests(PostgresFixture fx, ITestOutputHelper output) : IClassFixture<PostgresFixture>
{
    [LoadFact]
    public async Task A_payroll_of_thousands_calculates_approves_and_reports_in_reasonable_time()
    {
        var count = int.TryParse(Environment.GetEnvironmentVariable("TYHR_LOAD"), out var n) && n > 0 ? n : 5000;
        var tenant = await fx.CreateTenantAsync();
        var timings = new List<(string Step, TimeSpan Took, TimeSpan Limit)>();
        async Task<T> Time<T>(string step, int limitSeconds, Func<Task<T>> work)
        {
            var watch = Stopwatch.StartNew();
            var result = await work();
            timings.Add((step, watch.Elapsed, TimeSpan.FromSeconds(limitSeconds)));
            output.WriteLine($"{step}: {watch.Elapsed.TotalSeconds:0.0} s");
            return result;
        }

        await Time($"Add {count:N0} employees with salaries", 600, async () =>
        {
            List<Guid> departments;
            await using (var scope = fx.NewScope(tenant))
            {
                await LeaveDefaults.SeedAsync(scope.Db, CountryPacks.Uganda, 2026, default);
                await PayrollDefaults.SeedAsync(scope.Db, default);
                var depts = Enumerable.Range(1, 12).Select(i => new Department($"Department {i}", $"D{i}", null)).ToList();
                scope.Db.Departments.AddRange(depts);
                await scope.Db.SaveChangesAsync();
                departments = depts.Select(d => d.Id).ToList();
            }
            var random = new Random(42);
            for (var start = 0; start < count; start += 500)
            {
                await using var scope = fx.NewScope(tenant);
                foreach (var i in Enumerable.Range(start, Math.Min(500, count - start)))
                {
                    var e = new Employee($"EMP-{i + 1:00000}", new PersonalDetails($"Person{i}", null, "Load", i % 2 == 0 ? Gender.Female : Gender.Male,
                            new DateOnly(1970 + i % 35, 1 + i % 12, 1), null, "UG", null, null, null, null, null, null, null),
                        CountryPacks.Uganda, new EmploymentDetails(new DateOnly(2015 + i % 11, 1 + i % 12, 1), $"person{i}@load.test", null, null,
                            departments[i % departments.Count], null, null));
                    e.UpdatePayment(new PaymentDetails(PaymentMethod.Bank, "Stanbic", "Kampala", $"90{i:0000000000}", $"Person{i} Load", null, null), CountryPacks.Uganda);
                    scope.Db.Employees.Add(e);
                    scope.Db.SalaryChanges.Add(new SalaryChange(e.Id, e.HireDate, 600_000 + random.Next(0, 90) * 50_000, CountryPacks.Uganda, "Starting salary",
                        "load", fx.Clock.GetUtcNow()));
                }
                await scope.Db.SaveChangesAsync();
            }
            return 0;
        });

        await using var payroll = fx.NewScope(tenant, user: "paul", roles: [Roles.PayrollOfficer]);
        var runs = new PayrollService(payroll.Db, payroll.TenantContext, payroll.User, fx.Clock);
        var run = await Time("Create the October payroll", 30, () => runs.CreateRunAsync(new CreatePayrollRunRequest(2026, 10, PayrollRunKind.Regular, null, null), default));
        var calculated = await Time("Calculate it", 120, () => runs.CalculateAsync(run.Id, default));
        Assert.Equal(count, await payroll.Db.Payslips.CountAsync(p => p.RunId == run.Id));
        await Time("Calculate it again (after a change)", 120, () => runs.CalculateAsync(run.Id, default));
        await Time("Submit it", 30, () => runs.SubmitAsync(run.Id, default));
        await using (var approver = fx.NewScope(tenant, user: "grace", roles: [Roles.HrAdmin]))
            await Time("Approve it", 30, () => new PayrollService(approver.Db, approver.TenantContext, approver.User, fx.Clock).ApproveAsync(run.Id, default));

        await using var outputsScope = fx.NewScope(tenant, user: "paul", roles: [Roles.PayrollOfficer]);
        var outputs = new PayrollOutputService(outputsScope.Db, outputsScope.TenantContext, outputsScope.User, new EmployeeAccess(outputsScope.Db, outputsScope.User),
            new QuestPayslipRenderer(), new FakeEmail(), fx.Clock, NullLogger<PayrollOutputService>.Instance);
        await Time("Bank payment file", 30, () => outputs.PaymentFileAsync(run.Id, PaymentMethod.Bank, default));
        await Time("Statutory returns", 30, () => outputs.ReturnsAsync(2026, 10, default));
        await Time("Journal", 30, () => outputs.JournalAsync(run.Id, default));
        await Time("Lock it", 30, () => new PayrollService(outputsScope.Db, outputsScope.TenantContext, outputsScope.User, fx.Clock).LockAsync(run.Id, default));
        var emailed = await Time("Email every payslip (PDFs; the mail server is a fake)", 600, async () =>
        {
            var total = 0;
            for (Guid? after = null; ;)
            {
                var r = await outputs.EmailPayslipsAsync(run.Id, after, default);
                total += r.Sent;
                Assert.True(r.Sent <= PayrollOutputService.EmailBatch);
                if (r.Next == null) return total;
                after = r.Next;
            }
        });
        Assert.Equal(count, emailed);
        var slips = await outputsScope.Db.Payslips.AsNoTracking().Where(p => p.RunId == run.Id).Select(p => p.Id).Take(20).ToListAsync();
        await Time("20 payslip PDFs", 30, async () => { foreach (var id in slips) await outputs.PayslipPdfAsync(id, default); return 0; });

        await using var hr = fx.NewScope(tenant, user: "grace", roles: [Roles.HrAdmin]);
        var reports = new ReportsService(hr.Db, hr.TenantContext, hr.User, fx.Clock);
        await Time("Headcount report", 20, () => reports.HeadcountAsync(default));
        await Time("Turnover report", 20, () => reports.TurnoverAsync(default));
        var liability = await Time("Leave liability report", 30, () => reports.LeaveLiabilityAsync(default));
        Assert.Equal(count, liability.People.Count);
        var cost = await Time("Payroll cost report", 20, () => reports.PayrollCostAsync(null, null, default));
        Assert.Equal(calculated.Gross, cost.Gross);
        await Time("Reports as Excel", 60, () => reports.ExportAsync(default));
        var access = new EmployeeAccess(hr.Db, hr.User);
        var employees = new EmployeeService(hr.Db, hr.TenantContext, new PostgresDocumentNumbers(hr.Db), access, hr.User, fx.Clock);
        await Time("Employee list, first page", 5, () => employees.ListAsync(new EmployeeQuery(), default));
        await Time("Employee list, searching", 5, () => employees.ListAsync(new EmployeeQuery(Search: "person4999"), default));
        await Time("Employees as Excel", 60, () => new EmployeeSpreadsheets(hr.Db, hr.TenantContext, employees, access, fx.Clock).ExportAsync(new EmployeeQuery(), default));

        Assert.Empty(timings.Where(t => t.Took > t.Limit).Select(t => $"{t.Step} took {t.Took.TotalSeconds:0.0} s (limit {t.Limit.TotalSeconds} s)"));
    }
}
