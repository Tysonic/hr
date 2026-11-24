using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Exits;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;
using TyHr.Application.Leave;
using TyHr.Application.Payroll;
using TyHr.Domain.Payroll;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Exits. Grace runs HR (with her own record), Mark is a line manager, Ivan looks after IT, Paul does payroll, and Nora
/// (hired 1 Jun 2023) reports to Mark. Today is 23 Nov 2026 (fixture clock).
/// </summary>
public class ExitTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly FakeEmail _email = new();

    private sealed record Services(Scope Scope, ExitService Exits, EmployeeService Employees, FinalDuesService Dues, PayrollService Payroll) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        var payroll = new PayrollService(scope.Db, scope.TenantContext, scope.User, fx.Clock);
        return new Services(scope, new ExitService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<ExitService>.Instance),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock),
            new FinalDuesService(scope.Db, scope.TenantContext, scope.User, payroll, fx.Clock), payroll);
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Ivan, Guid Nora);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace", Roles.HrAdmin);
        var grace = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Atim", email: "grace@co.test"), default)).Id;
        var mark = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Opio", email: "mark@co.test",
            contract: new SaveContractRequest(ContractType.Permanent, EmployeeTests.NewEmployee("x", "y").Employment.HireDate, null, null, null)), default)).Id;
        var ivan = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Ivan", "Ssali", email: "ivan@co.test"), default)).Id;
        var start = new DateOnly(2023, 6, 1);
        var nora = EmployeeTests.NewEmployee("Nora", "Achieng", managerId: mark, email: "nora@co.test",
            contract: new SaveContractRequest(ContractType.Permanent, start, null, null, null));
        var noraId = (await hr.Employees.CreateAsync(nora with { Employment = nora.Employment with { HireDate = start } }, default)).Id;
        return new Company(co, grace, mark, ivan, noraId);
    }

    private static readonly InterviewAnswers Answers = new(LeavingReason.CareerGrowth, 4, 3, 2, 2, 4, true, true, "The team", "Clearer promotion paths", null);

    [Theory]
    [InlineData("2026-01-01", "2026-05-31", 0)]
    [InlineData("2026-01-01", "2026-07-01", 14)]
    [InlineData("2023-06-01", "2026-11-23", 30)]
    [InlineData("2020-06-01", "2026-11-23", 60)]
    [InlineData("2015-06-01", "2026-11-23", 90)]
    public void Ugandan_statutory_notice_grows_with_service(string hired, string given, int days) =>
        Assert.Equal(days, CountryPacks.Uganda.Exits.Notice(DateOnly.Parse(hired), DateOnly.Parse(given)).Days);

    [Fact]
    public void Kenyan_statutory_notice_is_28_days_for_monthly_pay() =>
        Assert.Equal(28, CountryPacks.Kenya.Exits.Notice(new DateOnly(2010, 1, 1), new DateOnly(2026, 11, 23)).Days);

    [Fact]
    public async Task A_resignation_goes_to_HR_who_accept_it_and_start_clearance_with_a_confidential_interview()
    {
        var c = await CompanyAsync();
        MyExitDto mine;
        await using (var nora = As(c.Tenant, "nora", Roles.Employee))
        {
            mine = await nora.Exits.ResignAsync(new ResignRequest(new DateOnly(2026, 12, 31), "A role closer to home"), default);
            Assert.Equal((ExitStatus.Requested, true), (mine.Status, mine.CanWithdraw));
            Assert.Contains("already has an exit", (await Assert.ThrowsAsync<DomainException>(() =>
                nora.Exits.ResignAsync(new ResignRequest(new DateOnly(2026, 12, 31), null), default))).Message);
            Assert.Contains("opens once HR", (await Assert.ThrowsAsync<DomainException>(() => nora.Exits.MyInterviewAsync(Answers, default))).Message);
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["mark@co.test"]) && m.Subject == "Nora Achieng has resigned");

        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.Equal(1, await hr.Exits.WaitingCountAsync(default));
            var accepted = await hr.Exits.AcceptAsync(mine.Id, new AcceptResignationRequest(new DateOnly(2026, 12, 31), false, c.Ivan), default);
            Assert.Empty(accepted.Warnings);
            var exit = accepted.Exit;
            Assert.Equal((ExitStatus.InProgress, ClearanceStep.Standard.Length, 30, false), (exit.Status, exit.Clearance.Count, exit.Notice.Days, exit.ShortNotice));
            Assert.Equal(c.Mark, exit.Clearance.Single(i => i.Title.StartsWith("Collect the laptop")).AssigneeId);
            Assert.Contains("on or after their last working day", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Exits.CompleteAsync(exit.Id, new CompleteExitRequest(true, null), default))).Message);
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["ivan@co.test"]) && m.Subject == "Exit clearance for Nora Achieng");
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["nora@co.test"]) && m.Subject.Contains("accepted"));

        await using (var ivan = As(c.Tenant, "ivan", Roles.Employee))
        {
            var item = Assert.Single(await ivan.Exits.MyItemsAsync(default));
            Assert.True((await ivan.Exits.ClearAsync(item.Id, "Accounts disabled", default)).IsDone);
        }
        await using (var nora = As(c.Tenant, "nora", Roles.Employee))
        {
            mine = (await nora.Exits.MineAsync(default))!;
            var own = Assert.Single(mine.Clearance);
            Assert.Equal(ClearanceOwner.Employee, own.Owner);
            await nora.Exits.ClearAsync(own.Id, null, default);
            mine = await nora.Exits.MyInterviewAsync(Answers, default);
            Assert.Equal(LeavingReason.CareerGrowth, mine.Interview!.Answers.MainReason);
            Assert.False(mine.CanWithdraw);
            await Assert.ThrowsAsync<DomainException>(() => nora.Exits.WithdrawAsync(mine.Id, null, default));
        }
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => mark.Exits.GetAsync(mine.Id, default)); // the interview is HR's only
            Assert.Equal(2, (await mark.Exits.MyItemsAsync(default)).Count);
        }
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.Contains("themselves", (await Assert.ThrowsAsync<DomainException>(() => hr.Exits.InterviewAsync(mine.Id, Answers, default))).Message);
            var exit = await hr.Exits.UpdateAsync(mine.Id, new UpdateExitRequest(ExitType.Resignation, "A role closer to home", new DateOnly(2026, 11, 23),
                new DateOnly(2026, 12, 15), false), default);
            Assert.True(exit.ShortNotice); // 22 days against a statutory 30
            Assert.Equal(new DateOnly(2026, 12, 15), exit.Clearance.Single(i => i.Title.StartsWith("Collect the ID")).DueOn);
        }
    }

    [Fact]
    public async Task Completing_an_exit_needs_clearance_done_and_then_marks_the_employee_as_exited()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        Assert.Contains("reason", (await Assert.ThrowsAsync<DomainException>(() => hr.Exits.CreateAsync(new CreateExitRequest(c.Mark, ExitType.Redundancy,
            null, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 20), true, null), default))).Message);
        Assert.Contains("your own", (await Assert.ThrowsAsync<DomainException>(() => hr.Exits.CreateAsync(new CreateExitRequest(c.Grace, ExitType.Resignation,
            null, new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 20), false, null), default))).Message);

        var created = await hr.Exits.CreateAsync(new CreateExitRequest(c.Mark, ExitType.Redundancy, "Branch closed", new DateOnly(2026, 10, 1),
            new DateOnly(2026, 11, 20), true, null), default);
        Assert.Contains(created.Warnings, w => w.Contains("no line manager"));
        Assert.Contains(created.Warnings, w => w.Contains("No IT contact"));
        var exit = created.Exit;
        Assert.False(exit.ShortNotice); // paid in lieu
        Assert.Contains("still open", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Exits.CompleteAsync(exit.Id, new CompleteExitRequest(true, null), default))).Message);

        var finance = exit.Clearance.Single(i => i.Owner == ClearanceOwner.Finance);
        await using (var paul = As(c.Tenant, "paul", Roles.PayrollOfficer))
        {
            Assert.Contains(await paul.Exits.MyItemsAsync(default), i => i.Id == finance.Id);
            await paul.Exits.ClearAsync(finance.Id, "No advances outstanding", default);
            await Assert.ThrowsAsync<NotFoundException>(() => paul.Exits.ClearAsync(exit.Clearance.First(i => i.Owner == ClearanceOwner.Hr).Id, null, default));
        }
        foreach (var item in exit.Clearance.Where(i => i.Id != finance.Id)) await hr.Exits.ClearAsync(item.Id, null, default);
        await hr.Exits.InterviewAsync(exit.Id, Answers, default);

        var done = await hr.Exits.CompleteAsync(exit.Id, new CompleteExitRequest(true, "Would rehire"), default);
        Assert.Equal(ExitStatus.Completed, done.Exit.Status);
        Assert.Contains(done.Warnings, w => w.Contains("Nora Achieng still reports to Mark Opio"));
        var mark = await hr.Scope.Db.Employees.AsNoTracking().SingleAsync(e => e.Id == c.Mark);
        Assert.Equal(EmployeeStatus.Exited, mark.Status);
        Assert.Equal(new DateOnly(2026, 11, 20), (await hr.Scope.Db.EmploymentContracts.AsNoTracking().SingleAsync(x => x.EmployeeId == c.Mark)).EndDate);
        Assert.Contains("already left", (await Assert.ThrowsAsync<DomainException>(() => hr.Exits.CreateAsync(new CreateExitRequest(c.Mark, ExitType.Resignation,
            null, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 22), false, null), default))).Message);
    }

    [Fact]
    public void Kenyan_redundancy_pays_15_days_for_each_completed_year()
    {
        var s = CountryPacks.Kenya.Exits.Severance(ExitType.Redundancy, new DateOnly(2020, 1, 1), new DateOnly(2026, 11, 20), 90_000, CountryPacks.Kenya)!;
        Assert.Equal(270_000m, s.Amount); // 90,000 ÷ 30 × 15 × 6 years
        Assert.Null(CountryPacks.Kenya.Exits.Severance(ExitType.Resignation, new DateOnly(2020, 1, 1), new DateOnly(2026, 11, 20), 90_000, CountryPacks.Kenya));
    }

    [Fact]
    public void A_leavers_last_month_is_paid_up_to_their_last_day() =>
        Assert.Equal(20m / 31, PayCalculator.EmployedShare(new DateOnly(2025, 1, 6), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 20)));

    [Fact]
    public async Task Final_dues_pay_unused_leave_notice_in_lieu_and_severance_through_an_off_cycle_payroll()
    {
        var c = await CompanyAsync();
        Guid exitId;
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            await LeaveDefaults.SeedAsync(hr.Scope.Db, CountryPacks.Uganda, 2026, default);
            await hr.Employees.AddSalaryAsync(c.Mark, new AddSalaryRequest(new DateOnly(2025, 1, 6), 1_000_000, "Starting salary"), default);
            exitId = (await hr.Exits.CreateAsync(new CreateExitRequest(c.Mark, ExitType.Redundancy, "Branch closed", new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 20), true, c.Ivan), default)).Exit.Id;
        }
        await using (var henry = As(c.Tenant, "henry", Roles.HrOfficer))
            await Assert.ThrowsAsync<NotFoundException>(() => henry.Dues.GetAsync(exitId, default));

        await using var paul = As(c.Tenant, "paul", Roles.PayrollOfficer);
        var dues = await paul.Dues.GetAsync(exitId, default);
        Assert.False(dues.Saved);
        Assert.Equal(20.5m, dues.LeaveDays); // 15.5 earned to the end of September + 5 carried from 2025
        Assert.Equal([("Unused annual leave", 946_154m), ("Pay in lieu of notice", 366_667m), ("Severance pay", 1_000_000m)],
            dues.Lines.Select(l => (l.Label, l.Amount)));
        Assert.Contains("11 of 30 days", dues.Lines[1].Basis);

        var advance = new PayComponent("ADVANCE", "Salary advance", PayComponentKind.Deduction, false, false);
        paul.Scope.Db.PayComponents.Add(advance);
        await paul.Scope.Db.SaveChangesAsync();
        var lines = dues.Lines.Select(l => new SettlementLine(l.ComponentId, l.Label, l.Amount, l.Basis)).Append(new(advance.Id, "Advance not repaid", 200_000, null)).ToList();
        dues = await paul.Dues.SaveAsync(exitId, lines, default);
        Assert.Equal((2_312_821m, 200_000m, true), (dues.Earnings, dues.Deductions, dues.CanSend));

        // October's regular payroll pays Mark to his last day.
        var october = await paul.Payroll.CreateRunAsync(new CreatePayrollRunRequest(2026, 10, PayrollRunKind.Regular, null, null), default);
        await paul.Payroll.CalculateAsync(october.Id, default);
        var slip = await paul.Scope.Db.Payslips.AsNoTracking().SingleAsync(p => p.RunId == october.Id && p.EmployeeId == c.Mark);
        var basic = slip.Lines.Single(l => l.Code == "BASIC");
        Assert.Equal(645_161m, basic.Amount);
        Assert.Contains("To 20 Oct", basic.Note);

        dues = await paul.Dues.SendToPayrollAsync(exitId, default);
        Assert.Equal((SettlementStatus.InPayroll, "Final dues: Mark Opio"), (dues.Status, dues.RunName));
        Assert.Contains("already in a payroll", (await Assert.ThrowsAsync<DomainException>(() => paul.Dues.SaveAsync(exitId, lines, default))).Message);
        var run = await paul.Payroll.CalculateAsync(dues.RunId!.Value, default);
        var final = await paul.Scope.Db.Payslips.AsNoTracking().SingleAsync(p => p.RunId == run.Id);
        Assert.Equal(2_312_821m, final.Gross);

        await paul.Payroll.DeleteRunAsync(run.Id, default);
        Assert.Equal(SettlementStatus.Draft, (await paul.Dues.GetAsync(exitId, default)).Status);
    }

    [Fact]
    public async Task Former_employees_get_a_certificate_and_their_personal_data_is_removed_once_the_retention_period_passes()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        var request = EmployeeTests.NewEmployee("Olivia", "Kato", email: "olivia@co.test");
        var olivia = (await hr.Employees.CreateAsync(request with { Employment = request.Employment with { HireDate = new DateOnly(2012, 3, 1) } }, default)).Id;
        var exit = (await hr.Exits.CreateAsync(new CreateExitRequest(olivia, ExitType.Resignation, "Moved abroad", new DateOnly(2018, 5, 31),
            new DateOnly(2018, 6, 30), false, null), default)).Exit;
        var certificates = new CertificateService(hr.Scope.Db, hr.Scope.TenantContext, hr.Scope.User, new TyHr.Infrastructure.Pdf.QuestCertificateRenderer(), fx.Clock);
        Assert.Contains("once the exit is complete", (await Assert.ThrowsAsync<DomainException>(() => certificates.RenderAsync(exit.Id, default))).Message);
        foreach (var item in exit.Clearance) await hr.Exits.ClearAsync(item.Id, null, default);
        await hr.Exits.InterviewAsync(exit.Id, Answers, default);
        await hr.Exits.CompleteAsync(exit.Id, new CompleteExitRequest(true, null), default);
        var pdf = await certificates.RenderAsync(exit.Id, default);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.Content[..4]));
        Assert.Equal("Certificate of service - OliviaKato.pdf", pdf.FileName);

        var archive = new ArchiveService(hr.Scope.Db, hr.Scope.TenantContext, hr.Scope.User, new FakeFiles(), fx.Clock, NullLogger<ArchiveService>.Instance);
        await using (var henry = As(c.Tenant, "henry", Roles.HrOfficer))
        {
            var officer = new ArchiveService(henry.Scope.Db, henry.Scope.TenantContext, henry.Scope.User, new FakeFiles(), fx.Clock, NullLogger<ArchiveService>.Instance);
            Assert.Contains("HR admin", (await Assert.ThrowsAsync<DomainException>(() => officer.AnonymiseAsync(olivia, default))).Message);
        }
        var list = await archive.SetRetentionAsync(10, default);
        Assert.Equal((new DateOnly(2028, 6, 30), false), (list.People.Single().KeepUntil!.Value, list.People.Single().Due));
        Assert.Contains("kept until 30 June 2028", (await Assert.ThrowsAsync<DomainException>(() => archive.AnonymiseAsync(olivia, default))).Message);
        await archive.SetRetentionAsync(7, default);

        var row = (await archive.AnonymiseAsync(olivia, default)).People.Single();
        Assert.Equal("Former employee " + row.EmployeeNumber, row.Name);
        var record = await hr.Scope.Db.Employees.AsNoTracking().SingleAsync(e => e.Id == olivia);
        Assert.Equal((null, null, (DateOnly?)null), (record.WorkEmail, record.IdNumber, record.DateOfBirth));
        Assert.Empty(await hr.Scope.Db.ExitInterviews.AsNoTracking().Where(i => i.EmployeeId == olivia).ToListAsync());
        Assert.Null((await hr.Scope.Db.EmployeeExits.AsNoTracking().SingleAsync(x => x.Id == exit.Id)).Reason);
        var audit = await hr.Scope.Db.AuditEntries.AsNoTracking().Where(a => a.EntityId == olivia.ToString()).ToListAsync();
        Assert.NotEmpty(audit);
        Assert.DoesNotContain(audit, a => a.Changes != null && a.Changes.Contains("Olivia"));
        Assert.Contains("already been removed", (await Assert.ThrowsAsync<DomainException>(() => archive.AnonymiseAsync(olivia, default))).Message);
    }

    private sealed class FakeFiles : IFileStore
    {
        public Task SaveAsync(string tenant, string key, Stream content, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string tenant, string key, CancellationToken cancellationToken = default) => throw new NotFoundException("gone");
        public Task DeleteAsync(string tenant, string key, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
