using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Payroll;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;
using TyHr.Domain.Payroll;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Payroll runs end to end in a Ugandan company, November 2026. Paul (payroll officer) prepares; Grace and Henry
/// (HR admins) approve. Amina earns 1,000,000 plus a 200,000 housing allowance; Brian joined on 16 November on
/// 600,000; Mark has no salary yet.
/// </summary>
public class PayrollTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed record Services(Scope Scope, PayrollService Payroll, PayrollSetupService Setup, EmployeeService Employees) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        return new Services(scope, new PayrollService(scope.Db, scope.TenantContext, scope.User, fx.Clock),
            new PayrollSetupService(scope.Db, scope.TenantContext, fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock));
    }

    private Services Paul(string co) => As(co, "paul", Roles.PayrollOfficer);
    private Services Grace(string co) => As(co, "grace", Roles.HrAdmin);
    private Services Henry(string co) => As(co, "henry", Roles.HrAdmin);

    private sealed record Company(string Tenant, Guid Amina, Guid Brian, Guid Mark, Dictionary<string, Guid> Components);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = Grace(co);
        await PayrollDefaults.SeedAsync(hr.Scope.Db, default);
        var hired = new DateOnly(2025, 1, 6);
        var amina = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", salary: new AddSalaryRequest(hired, 1_000_000, "Starting salary")), default);
        await hr.Employees.UpdatePaymentAsync(amina.Id, new PaymentDetails(PaymentMethod.Bank, "Stanbic", "Kampala Road", "9030012345678", "Amina Nakato", null, null), default);
        var brian = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Brian", "Okello", salary: new AddSalaryRequest(new DateOnly(2026, 11, 16), 600_000, "Starting salary")), default);
        await hr.Employees.UpdateEmploymentAsync(brian.Id, new EmploymentDetails(new DateOnly(2026, 11, 16), null, null, null, null, null, null), default);
        var mark = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Manager"), default);
        var components = (await hr.Setup.ComponentsAsync(default)).ToDictionary(c => c.Code, c => c.Id);
        await hr.Setup.AddItemAsync(amina.Id, new SavePayItemRequest(components["HOUSING"], 200_000, hired, null, null), default);
        return new Company(co, amina.Id, brian.Id, mark.Id, components);
    }

    private async Task<PayrollRunDto> ApprovedAndLockedAsync(string co, Guid runId)
    {
        await using (var paul = Paul(co)) await paul.Payroll.SubmitAsync(runId, default);
        await using (var grace = Grace(co)) await grace.Payroll.ApproveAsync(runId, default);
        await using (var paul = Paul(co)) return await paul.Payroll.LockAsync(runId, default);
    }

    [Fact]
    public async Task A_regular_payroll_pays_salary_allowances_and_Ugandan_statutory_items_with_joiners_pro_rated()
    {
        var c = await CompanyAsync();
        await using var paul = Paul(c.Tenant);
        var run = await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default);
        Assert.Equal(("November 2026 payroll", new DateOnly(2026, 11, 30), PayrollStatus.Draft), (run.Name, run.PayDate, run.Status));

        run = await paul.Payroll.CalculateAsync(run.Id, default);
        Assert.Equal((PayrollStatus.Calculated, 2), (run.Status, run.EmployeeCount));
        Assert.Contains("Mark Manager", Assert.Single(run.Warnings, w => w.Contains("no salary")));

        var slips = (await paul.Payroll.PayslipsAsync(run.Id, default)).ToDictionary(p => p.EmployeeName);
        // Amina: 1,200,000 gross; PAYE 33,750 + 30% of 715,000; NSSF 5%.
        Assert.Equal((1_200_000m, 248_250m, 891_750m), (slips["Amina Nakato"].Gross, slips["Amina Nakato"].Paye, slips["Amina Nakato"].Net));
        Assert.Equal(120_000m, slips["Amina Nakato"].EmployerContributions);
        // Brian: 15 of 30 days of 600,000, under the tax-free band.
        Assert.Equal((300_000m, 0m, 285_000m), (slips["Brian Okello"].Gross, slips["Brian Okello"].Paye, slips["Brian Okello"].Net));
        Assert.Equal(1_176_750m, run.Net);

        var payslip = await paul.Payroll.PayslipAsync(slips["Brian Okello"].Id, default);
        Assert.Equal("From 16 Nov: 15 of 30 days", payslip.Lines.Single(l => l.Code == "BASIC").Note);
        Assert.Contains(payslip.Warnings, w => w.Contains("TIN"));
        Assert.Contains(payslip.Warnings, w => w.Contains("No bank account"));
        Assert.Equal("Stanbic ••••5678", (await paul.Payroll.PayslipAsync(slips["Amina Nakato"].Id, default)).PaidTo);
    }

    [Fact]
    public async Task The_preparer_cannot_approve_and_nothing_changes_once_approved()
    {
        var c = await CompanyAsync();
        Guid id;
        await using (var paul = Paul(c.Tenant))
        {
            id = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            Assert.Contains("Calculate", (await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.SubmitAsync(id, default))).Message);
        }
        await using (var grace = Grace(c.Tenant))
        {
            await grace.Payroll.CalculateAsync(id, default);
            var submitted = await grace.Payroll.SubmitAsync(id, default);
            Assert.False(submitted.CanApprove);
            Assert.Contains("Someone other", (await Assert.ThrowsAsync<DomainException>(() => grace.Payroll.ApproveAsync(id, default))).Message);
        }
        await using (var paul = Paul(c.Tenant))
            Assert.Contains("waiting for approval", (await Assert.ThrowsAsync<DomainException>(() =>
                paul.Payroll.AddInputAsync(id, new AddPayrollInputRequest(c.Amina, c.Components["BONUS"], 100_000, null), default))).Message);

        await using (var henry = Henry(c.Tenant))
        {
            Assert.Contains("needs changing", (await Assert.ThrowsAsync<DomainException>(() => henry.Payroll.SendBackAsync(id, " ", default))).Message);
            var back = await henry.Payroll.SendBackAsync(id, "Add Amina's overtime", default);
            Assert.Equal((PayrollStatus.Calculated, "Add Amina's overtime"), (back.Status, back.ReturnNote));
        }
        await using (var paul = Paul(c.Tenant))
        {
            await paul.Payroll.AddInputAsync(id, new AddPayrollInputRequest(c.Amina, c.Components["OVERTIME"], 50_000, "12 hours"), default);
            Assert.Equal(PayrollStatus.Draft, (await paul.Payroll.GetRunAsync(id, default)).Status);
            await paul.Payroll.CalculateAsync(id, default);
            await paul.Payroll.SubmitAsync(id, default);
        }
        await using (var henry = Henry(c.Tenant))
        {
            Assert.True((await henry.Payroll.GetRunAsync(id, default)).CanApprove);
            Assert.Equal((PayrollStatus.Approved, "henry"), ((await henry.Payroll.ApproveAsync(id, default)).Status, (await henry.Payroll.GetRunAsync(id, default)).ApprovedBy));
        }
        await using (var paul = Paul(c.Tenant))
        {
            await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.CalculateAsync(id, default));
            await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.DeleteRunAsync(id, default));
            Assert.Equal(PayrollStatus.Locked, (await paul.Payroll.LockAsync(id, default)).Status);
            Assert.Contains("already pays", (await Assert.ThrowsAsync<DomainException>(() =>
                paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default))).Message);
        }
    }

    [Fact]
    public async Task An_off_cycle_bonus_is_taxed_on_top_of_the_months_salary()
    {
        var c = await CompanyAsync();
        Guid regular, bonus;
        await using (var paul = Paul(c.Tenant))
        {
            regular = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(regular, default);
        }
        await ApprovedAndLockedAsync(c.Tenant, regular);

        await using (var paul = Paul(c.Tenant))
        {
            var run = await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.OffCycle, "Year-end bonus", new DateOnly(2026, 12, 15)), default);
            bonus = run.Id;
            await paul.Payroll.AddInputAsync(bonus, new AddPayrollInputRequest(c.Amina, c.Components["BONUS"], 500_000, null), default);
            run = await paul.Payroll.CalculateAsync(bonus, default);
            var slip = Assert.Single(await paul.Payroll.PayslipsAsync(bonus, default));
            // Already in the 30% band: all 500,000 taxed at 30%, NSSF 5%, no second LST or salary.
            Assert.Equal((500_000m, 150_000m, 325_000m), (slip.Gross, slip.Paye, slip.Net));
            Assert.Equal(1, run.EmployeeCount);
        }
    }

    [Fact]
    public async Task A_reversal_cancels_a_locked_payroll_line_for_line_and_frees_the_month_for_a_new_one()
    {
        var c = await CompanyAsync();
        Guid original;
        await using (var paul = Paul(c.Tenant))
        {
            original = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(original, default);
            Assert.Contains("Only a locked", (await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.ReverseAsync(original, default))).Message);
        }
        var locked = await ApprovedAndLockedAsync(c.Tenant, original);
        Assert.True(locked.CanReverse);

        PayrollRunDto reversal;
        await using (var paul = Paul(c.Tenant))
        {
            reversal = await paul.Payroll.ReverseAsync(original, default);
            Assert.Equal((PayrollRunKind.Reversal, PayrollStatus.Calculated, -locked.Net, -locked.EmployerCost), (reversal.Kind, reversal.Status, reversal.Net, reversal.EmployerCost));
            Assert.Contains("already being reversed", (await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.ReverseAsync(original, default))).Message);
            Assert.Equal(reversal.Id, (await paul.Payroll.GetRunAsync(original, default)).ReversedByRunId);
            await Assert.ThrowsAsync<DomainException>(() => paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default));
        }
        await ApprovedAndLockedAsync(c.Tenant, reversal.Id);

        await using (var paul = Paul(c.Tenant))
        {
            var again = await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, "November 2026 payroll (corrected)", null), default);
            again = await paul.Payroll.CalculateAsync(again.Id, default);
            Assert.Equal(locked.Net, again.Net); // the reversal and original cancel out, so statutory items start afresh
        }
    }

    [Fact]
    public async Task Approved_unpaid_leave_comes_off_basic_pay_by_working_day()
    {
        var c = await CompanyAsync();
        await using (var hr = Grace(c.Tenant))
        {
            var unpaid = new LeaveType("UNPAID", "Unpaid leave", LeaveAccrual.NoBalance, null, null, 0, false, false, null, 0, null, null);
            hr.Scope.Db.LeaveTypes.Add(unpaid);
            await hr.Scope.Db.SaveChangesAsync();
            var request = new LeaveRequest(c.Amina, unpaid.Id, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10), false, 2, null, null, "grace", fx.Clock.GetUtcNow());
            request.Approve("grace", fx.Clock.GetUtcNow(), null);
            hr.Scope.Db.LeaveRequests.Add(request);
            await hr.Scope.Db.SaveChangesAsync();
        }
        await using var paul = Paul(c.Tenant);
        var run = await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default);
        await paul.Payroll.CalculateAsync(run.Id, default);
        var amina = (await paul.Payroll.PayslipsAsync(run.Id, default)).Single(p => p.EmployeeName == "Amina Nakato");
        var line = (await paul.Payroll.PayslipAsync(amina.Id, default)).Lines.Single(l => l.Code == "UNPAID");
        Assert.Equal((-95_238m, "2 of 21 working days"), (line.Amount, line.Note)); // 1,000,000 / 21 × 2
        Assert.Equal(1_200_000m - 95_238m, amina.Gross);
    }

    [Fact]
    public async Task The_variance_report_shows_who_is_new_and_whose_pay_changed_since_last_month()
    {
        var c = await CompanyAsync();
        Guid november;
        await using (var paul = Paul(c.Tenant))
        {
            november = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(november, default);
        }
        await ApprovedAndLockedAsync(c.Tenant, november);
        await using var hr = Grace(c.Tenant);
        await hr.Employees.AddSalaryAsync(c.Mark, new AddSalaryRequest(new DateOnly(2026, 12, 1), 800_000, "Starting salary"), default);

        await using var p = Paul(c.Tenant);
        var december = await p.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 12, PayrollRunKind.Regular, null, null), default);
        await p.Payroll.CalculateAsync(december.Id, default);
        var variance = await p.Payroll.VarianceAsync(december.Id, default);
        var rows = variance.Rows.ToDictionary(r => r.EmployeeName);
        Assert.Equal("New this month", rows["Mark Manager"].Change);
        Assert.Equal(("Pay changed", 285_000m), (rows["Brian Okello"].Change, rows["Brian Okello"].PreviousNet!.Value));
        Assert.False(rows.ContainsKey("Amina Nakato"));
        Assert.Equal((2026, 11), (variance.PreviousYear, variance.PreviousMonth));
    }

    [Fact]
    public async Task Two_people_changing_a_payroll_at_once_cannot_overwrite_each_other()
    {
        var c = await CompanyAsync();
        Guid id;
        await using (var paul = Paul(c.Tenant))
        {
            id = (await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 11, PayrollRunKind.Regular, null, null), default)).Id;
            await paul.Payroll.CalculateAsync(id, default);
        }
        await using var first = Paul(c.Tenant);
        await using var second = Grace(c.Tenant);
        _ = first.Scope.Db.PayrollRuns.Single(r => r.Id == id); // first has the run open
        await second.Payroll.SubmitAsync(id, default);
        Assert.Contains("Someone else changed", (await Assert.ThrowsAsync<DomainException>(() => first.Payroll.UpdateRunAsync(id,
            new UpdatePayrollRunRequest("Renamed", new DateOnly(2026, 11, 28)), default))).Message);
    }

    [Fact]
    public async Task Pay_items_cannot_overlap_and_codes_used_by_TyHR_are_reserved()
    {
        var c = await CompanyAsync();
        await using var hr = Grace(c.Tenant);
        Assert.Contains("already has Housing allowance", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Setup.AddItemAsync(c.Amina, new SavePayItemRequest(c.Components["HOUSING"], 250_000, new DateOnly(2026, 12, 1), null, null), default))).Message);
        Assert.Contains("used by TyHR", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Setup.CreateComponentAsync(new SavePayComponentRequest("PAYE", "Tax", PayComponentKind.Deduction, false, false, null), default))).Message);
        Assert.Contains("decimal places", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Setup.AddItemAsync(c.Brian, new SavePayItemRequest(c.Components["TRANSPORT"], 100.50m, new DateOnly(2026, 12, 1), null, null), default))).Message);
        var pay = await hr.Setup.SaveTaxProfileAsync(c.Brian, new SaveTaxProfileRequest(false, false), default);
        Assert.False(pay.Resident);
    }
}
