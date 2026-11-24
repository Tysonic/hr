using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Application.Leave;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Leave requests end to end. Today is Monday 23 Nov 2026 (the fixture clock). The company: Grace (HR admin), Mark
/// (line manager) with Amina reporting to him, and Brian in Amina's department who reports to Grace.
/// </summary>
public class LeaveTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TempFolder _files = new();
    private readonly FakeEmail _email = new();
    public void Dispose() => _files.Dispose();

    private sealed record Services(Scope Scope, LeaveService Leave, LeaveSetupService Setup, EmployeeService Employees) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string email, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: email.Split('@')[0], roles: roles, email: email);
        var access = new EmployeeAccess(scope.Db, scope.User);
        var store = new LocalFileStore(Options.Create(new StorageOptions { Path = _files.Path }));
        var documents = new DocumentService(scope.Db, scope.TenantContext, store, scope.User, fx.Clock);
        return new Services(scope,
            new LeaveService(scope.Db, scope.TenantContext, access, scope.User, new LeaveNotifier(scope.Db, _email, NullLogger<LeaveNotifier>.Instance), documents, fx.Clock),
            new LeaveSetupService(scope.Db, scope.TenantContext, fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock));
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Amina, Guid Brian, Guid Annual, Guid Maternity, Guid Paternity);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace@co.test", Roles.HrAdmin);
        await LeaveDefaults.SeedAsync(hr.Scope.Db, CountryPacks.Uganda, 2026, default);
        var finance = Guid.Empty;
        var hired = new DateOnly(2025, 1, 6);
        EmployeeDtoId(await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Admin", email: "grace@co.test"), default), out var grace);
        EmployeeDtoId(await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Manager", email: "mark@co.test"), default), out var mark);
        var dept = await new Application.Organization.OrgService(hr.Scope.Db, hr.Scope.TenantContext).CreateDepartmentAsync(new("Finance", null, null, null), default);
        finance = dept.Id;
        var amina = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", managerId: mark, email: "amina@co.test"), default);
        await hr.Employees.UpdateEmploymentAsync(amina.Id, new EmploymentDetails(hired, "amina@co.test", null, null, finance, null, mark), default);
        var brianRequest = EmployeeTests.NewEmployee("Brian", "Okello", managerId: grace, email: "brian@co.test");
        var brian = await hr.Employees.CreateAsync(brianRequest with { Personal = brianRequest.Personal with { Gender = Gender.Male } }, default);
        await hr.Employees.UpdateEmploymentAsync(brian.Id, new EmploymentDetails(hired, "brian@co.test", null, null, finance, null, grace), default);
        var types = (await hr.Setup.ListTypesAsync(default)).ToDictionary(t => t.Code, t => t.Id);
        _email.Sent.Clear();
        return new Company(co, grace, mark, amina.Id, brian.Id, types["ANNUAL"], types["MATERNITY"], types["PATERNITY"]);

        static void EmployeeDtoId(EmployeeDto e, out Guid id) => id = e.Id;
    }

    private static readonly DateOnly Mon7Dec = new(2026, 12, 7);

    [Fact]
    public async Task An_employee_requests_leave_their_manager_approves_it_and_both_are_emailed()
    {
        var c = await CompanyAsync();
        LeaveRequestDto request;
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var preview = await amina.Leave.PreviewAsync(new(null, c.Annual, Mon7Dec, Mon7Dec.AddDays(6), false, null), default);
            Assert.Empty(preview.Problems);
            Assert.Equal(5, preview.Days); // Mon–Sun, weekend not counted
            request = await amina.Leave.CreateAsync(new(null, c.Annual, Mon7Dec, Mon7Dec.AddDays(6), false, "Family visit"), default);
            Assert.Equal((LeaveStatus.Pending, false, true), (request.Status, request.CanDecide, request.CanCancel));
            await Assert.ThrowsAsync<DomainException>(() => amina.Leave.ApproveAsync(request.Id, null, default));
        }
        var toManager = Assert.Single(_email.Sent);
        Assert.Equal(["mark@co.test"], toManager.To);
        Assert.Contains("Amina Nakato", toManager.Subject);

        await using (var mark = As(c.Tenant, "mark@co.test", Roles.LineManager))
        {
            var inbox = await mark.Leave.ListAsync(new LeaveRequestQuery("approvals"), default);
            Assert.True(Assert.Single(inbox.Items).CanDecide);
            Assert.Equal(LeaveStatus.Approved, (await mark.Leave.ApproveAsync(request.Id, "Enjoy", default)).Status);
        }
        Assert.Equal(["amina@co.test"], _email.Sent[^1].To);
        Assert.Contains("approved", _email.Sent[^1].Subject);

        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var annual = (await amina.Leave.EmployeeLeaveAsync(null, null, default)).Balances.Single(b => b.Code == "ANNUAL").Balance!;
            // Hired Jan 2025 with 5 days carried from 2025 (capped); 10 months of 2026 earned by 23 Nov; 5 taken.
            Assert.Equal((17.5m, 5m, 5m, 17.5m), (annual.Earned, annual.CarriedForward, annual.Taken, annual.Available));
        }
    }

    [Fact]
    public async Task Managers_decide_only_their_team_and_nobody_decides_their_own_leave()
    {
        var c = await CompanyAsync();
        await using var brian = As(c.Tenant, "brian@co.test", Roles.Employee);
        var request = await brian.Leave.CreateAsync(new(null, c.Annual, Mon7Dec, Mon7Dec, false, null), default);
        Assert.Equal(["grace@co.test"], _email.Sent.Single().To); // Brian's manager is Grace

        await using (var mark = As(c.Tenant, "mark@co.test", Roles.LineManager))
        {
            Assert.Empty((await mark.Leave.ListAsync(new LeaveRequestQuery("approvals"), default)).Items);
            await Assert.ThrowsAsync<NotFoundException>(() => mark.Leave.ApproveAsync(request.Id, null, default));
        }
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            var own = await grace.Leave.CreateAsync(new(null, c.Annual, Mon7Dec, Mon7Dec, false, null), default);
            Assert.False(own.CanDecide);
            await Assert.ThrowsAsync<DomainException>(() => grace.Leave.ApproveAsync(own.Id, null, default));
            await Assert.ThrowsAsync<DomainException>(() => grace.Leave.CreateAsync(new(null, c.Annual, Mon7Dec.AddDays(1), Mon7Dec.AddDays(1), false, null, Approve: true), default));
            Assert.Equal(LeaveStatus.Rejected, (await grace.Leave.RejectAsync(request.Id, "Month-end close", default)).Status);
            await Assert.ThrowsAsync<DomainException>(() => grace.Leave.RejectAsync(own.Id, "x", default));
        }
    }

    [Fact]
    public async Task Requests_are_checked_against_balance_overlaps_eligibility_and_the_per_request_cap()
    {
        var c = await CompanyAsync();
        await using var amina = As(c.Tenant, "amina@co.test", Roles.Employee);
        await amina.Leave.CreateAsync(new(null, c.Annual, Mon7Dec, Mon7Dec.AddDays(2), false, null), default);

        async Task<IReadOnlyList<string>> Problems(Guid type, DateOnly from, DateOnly to, bool half = false) =>
            (await amina.Leave.PreviewAsync(new(null, type, from, to, half, null), default)).Problems;

        Assert.Contains(await Problems(c.Annual, Mon7Dec.AddDays(2), Mon7Dec.AddDays(3)), p => p.Contains("overlaps a pending request"));
        Assert.Contains(await Problems(c.Annual, new DateOnly(2026, 12, 14), new DateOnly(2027, 2, 26)), p => p.Contains("available in 2027")); // 2026 has enough; early 2027 has only carried days and January
        Assert.Contains(await Problems(c.Paternity, Mon7Dec.AddDays(7), Mon7Dec.AddDays(7)), p => p.Contains("for male employees"));
        Assert.Contains(await Problems(c.Maternity, new DateOnly(2027, 1, 4), new DateOnly(2027, 4, 30)), p => p.Contains("at most 60 days"));
        Assert.Contains(await Problems(c.Annual, new DateOnly(2026, 12, 26), new DateOnly(2026, 12, 27)), p => p.Contains("weekends or public holidays"));
        Assert.Contains(await Problems(c.Annual, Mon7Dec.AddDays(7), Mon7Dec.AddDays(8), half: true), p => p.Contains("half day"));
        var ex = await Assert.ThrowsAsync<DomainException>(() => amina.Leave.CreateAsync(new(null, c.Annual, Mon7Dec.AddDays(1), Mon7Dec.AddDays(1), false, null), default));
        Assert.Contains("overlaps", ex.Message);
        await Assert.ThrowsAsync<DomainException>(() => amina.Leave.CreateAsync(new(c.Brian, c.Annual, Mon7Dec.AddDays(7), Mon7Dec.AddDays(7), false, null), default));
    }

    [Fact]
    public async Task Colleagues_see_who_is_away_but_not_why_and_not_pending_requests()
    {
        var c = await CompanyAsync();
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            await grace.Leave.CreateAsync(new(c.Amina, c.Annual, Mon7Dec, Mon7Dec.AddDays(4), false, null, Approve: true), default); // recorded by HR
            await grace.Leave.AdjustAsync(new AdjustLeaveRequest(c.Amina, c.Annual, 2026, 2, "Overtime agreed in lieu"), default);
        }
        Assert.Contains("approved", _email.Sent.Single().Subject);
        await using (var brian = As(c.Tenant, "brian@co.test", Roles.Employee))
        {
            await brian.Leave.CreateAsync(new(null, c.Annual, Mon7Dec, Mon7Dec, false, null), default);
            var calendar = await brian.Leave.CalendarAsync(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), null, default);
            var aminaEntry = Assert.Single(calendar.Entries, e => e.EmployeeName == "Amina Nakato");
            Assert.Null(aminaEntry.LeaveTypeName); // same department: sees she's away, not the kind of leave
            Assert.Equal("Annual leave", Assert.Single(calendar.Entries, e => e.EmployeeName == "Brian Okello").LeaveTypeName);
            Assert.Contains(calendar.Holidays, h => h.Name == "Christmas Day");
            await Assert.ThrowsAsync<NotFoundException>(() => brian.Leave.EmployeeLeaveAsync(c.Amina, null, default));
        }
        await using (var mark = As(c.Tenant, "mark@co.test", Roles.LineManager))
        {
            var entries = (await mark.Leave.CalendarAsync(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), null, default)).Entries;
            Assert.Equal("Annual leave", Assert.Single(entries).LeaveTypeName); // his team only; Brian's pending request isn't his
            var amina = await mark.Leave.EmployeeLeaveAsync(c.Amina, null, default);
            Assert.Equal(2, amina.Balances.Single(b => b.Code == "ANNUAL").Balance!.Adjusted);
            Assert.False(amina.CanManage);
        }
    }

    [Fact]
    public async Task Employees_can_cancel_future_leave_but_only_HR_can_cancel_leave_that_has_started()
    {
        var c = await CompanyAsync();
        Guid future, started;
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            future = (await grace.Leave.CreateAsync(new(c.Amina, c.Annual, Mon7Dec, Mon7Dec, false, null, Approve: true), default)).Id;
            started = (await grace.Leave.CreateAsync(new(c.Amina, c.Annual, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), false, null, Approve: true), default)).Id;
        }
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            Assert.Equal(LeaveStatus.Cancelled, (await amina.Leave.CancelAsync(future, null, default)).Status);
            var ex = await Assert.ThrowsAsync<DomainException>(() => amina.Leave.CancelAsync(started, null, default));
            Assert.Contains("only be cancelled by HR", ex.Message);
        }
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
            Assert.Equal(LeaveStatus.Cancelled, (await grace.Leave.CancelAsync(started, "Came back early", default)).Status);
    }

    [Fact]
    public async Task Holidays_are_generated_once_per_year_and_the_working_week_changes_day_counts()
    {
        var c = await CompanyAsync();
        await using var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin);
        var again = await grace.Setup.GenerateHolidaysAsync(2026, default);
        Assert.Equal(0, again.Added); // seeded with the company
        Assert.Contains("Eid", again.Note);
        Assert.Equal(12, (await grace.Setup.GenerateHolidaysAsync(2028, default)).Added); // Uganda: 10 fixed dates, Good Friday, Easter Monday
        await Assert.ThrowsAsync<DomainException>(() => grace.Setup.AddHolidayAsync(new(new DateOnly(2026, 12, 25), "Xmas"), default));

        await grace.Setup.SaveWorkWeekAsync(new([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]), default);
        Assert.Equal(6, (await grace.Leave.PreviewAsync(new(c.Amina, c.Annual, Mon7Dec, Mon7Dec.AddDays(6), false, null), default)).Days);
        await Assert.ThrowsAsync<DomainException>(() => grace.Setup.SaveWorkWeekAsync(new([]), default));
    }
}
