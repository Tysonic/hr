using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TyHr.Application.Attendance;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Application.Leave;
using TyHr.Application.Onboarding;
using TyHr.Application.SelfService;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.SelfService;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Employee self-service: change requests to one's own record, announcements, a manager's team and the approvals
/// inbox. The company: Grace (HR admin, with her own record), Mark (line manager) with Amina reporting to him and Brian
/// reporting to Amina; Paul is the payroll officer.
/// </summary>
public class SelfServiceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TempFolder _files = new();
    private readonly FakeEmail _email = new();

    public void Dispose() => _files.Dispose();

    private sealed record Services(Scope Scope, ProfileChangeService Changes, AnnouncementService Announcements, TeamService Team, EmployeeService Employees)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string email, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: email.Split('@')[0], roles: roles, email: email);
        var access = new EmployeeAccess(scope.Db, scope.User);
        var store = new LocalFileStore(Options.Create(new StorageOptions { Path = _files.Path }));
        var documents = new DocumentService(scope.Db, scope.TenantContext, store, scope.User, fx.Clock);
        var leave = new LeaveService(scope.Db, scope.TenantContext, access, scope.User, new LeaveNotifier(scope.Db, _email, NullLogger<LeaveNotifier>.Instance),
            documents, fx.Clock);
        var changes = new ProfileChangeService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<ProfileChangeService>.Instance);
        return new Services(scope, changes, new AnnouncementService(scope.Db, scope.TenantContext, scope.User, fx.Clock),
            new TeamService(scope.Db, scope.TenantContext, scope.User, access, new AttendanceService(scope.Db, scope.TenantContext, access, scope.User, fx.Clock),
                leave, changes, new ProbationService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<ProbationService>.Instance),
                new TyHr.Application.Exits.ExitService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<TyHr.Application.Exits.ExitService>.Instance), fx.Clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock));
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Amina, Guid Brian);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace@co.test", Roles.HrAdmin);
        var grace = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Atim", email: "grace@co.test"), default)).Id;
        var mark = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Opio", email: "mark@co.test"), default)).Id;
        var amina = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", managerId: mark, email: "amina@co.test"), default)).Id;
        var brian = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Brian", "Okello", managerId: amina, email: "brian@co.test"), default)).Id;
        return new Company(co, grace, mark, amina, brian);
    }

    private static PersonalDetails With(EmployeeDto e, string? phone = null, string? address = null, string? lastName = null) =>
        new(e.FirstName, e.MiddleName, lastName ?? e.LastName, e.Personal!.Gender, e.Personal.DateOfBirth, e.Personal.MaritalStatus, e.Personal.Nationality,
            e.Personal.IdType, e.Personal.IdNumber, e.Personal.TaxNumber, e.Personal.SocialSecurityNumber, e.Personal.PersonalEmail,
            phone ?? e.Personal.PersonalPhone, address ?? e.Personal.HomeAddress);

    [Fact]
    public async Task Employees_see_their_whole_own_record_but_cannot_change_it_directly()
    {
        var c = await CompanyAsync();
        await using var amina = As(c.Tenant, "amina@co.test", Roles.Employee);
        var me = await amina.Employees.MineAsync(default);
        Assert.Equal(c.Amina, me.Id);
        Assert.NotNull(me.Personal);
        Assert.NotNull(me.Payment);
        Assert.False(me.Access.CanEdit);

        await using var stranger = As(c.Tenant, "nobody@co.test", Roles.Employee);
        Assert.Contains("isn't linked", (await Assert.ThrowsAsync<DomainException>(() => stranger.Employees.MineAsync(default))).Message);
    }

    [Fact]
    public async Task An_approved_change_applies_only_the_fields_the_employee_changed()
    {
        var c = await CompanyAsync();
        ProfileChangeDto request;
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var me = await amina.Employees.MineAsync(default);
            Assert.Contains("Nothing", (await Assert.ThrowsAsync<DomainException>(() =>
                amina.Changes.RequestPersonalAsync(new RequestPersonalChange(With(me), null), default))).Message);
            request = await amina.Changes.RequestPersonalAsync(new RequestPersonalChange(With(me, phone: "0772 123 456", address: "Plot 4, Ntinda"), "Moved house"), default);
            var change = Assert.Single(request.Changes, f => f.Label == "Personal phone");
            Assert.Equal("+256772123456", change.Proposed); // tidied as HR's own edits would be
            Assert.Equal(2, request.Changes.Count);
            Assert.Contains("already", (await Assert.ThrowsAsync<DomainException>(() =>
                amina.Changes.RequestPersonalAsync(new RequestPersonalChange(With(me, phone: "0772 999 999"), null), default))).Message);
        }
        Assert.Contains(_email.Sent, m => m.Subject.Contains("Amina Nakato asks to change their personal details"));

        await using (var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            // Meanwhile HR corrects her surname: approving her request must not undo that.
            var record = await hr.Employees.GetAsync(c.Amina, default);
            await hr.Employees.UpdatePersonalAsync(c.Amina, With(record, lastName: "Nakato-Ssali"), default);
            Assert.Equal(request.Id, Assert.Single(await hr.Changes.ListAsync(null, default)).Id);
            await hr.Changes.ApproveAsync(request.Id, null, default);
            var after = await hr.Employees.GetAsync(c.Amina, default);
            Assert.Equal(("Nakato-Ssali", "+256772123456", "Plot 4, Ntinda"), (after.LastName, after.Personal!.PersonalPhone, after.Personal.HomeAddress));
            Assert.Contains("already", (await Assert.ThrowsAsync<DomainException>(() => hr.Changes.ApproveAsync(request.Id, null, default))).Message);
        }
        Assert.Contains(_email.Sent, m => m.Subject.Contains("personal details change is approved") && m.To.Contains("amina@co.test"));
    }

    [Fact]
    public async Task Payment_changes_are_for_payroll_or_HR_admins_never_on_their_own_record_and_are_told_to_every_address()
    {
        var c = await CompanyAsync();
        ProfileChangeDto aminas, graces;
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
            aminas = await amina.Changes.RequestPaymentAsync(new RequestPaymentChange(
                new PaymentDetails(PaymentMethod.Bank, "Stanbic", null, "9030 0123 45678", null, null, null), null), default);
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
            graces = await grace.Changes.RequestPaymentAsync(new RequestPaymentChange(
                new PaymentDetails(PaymentMethod.MobileMoney, null, null, null, null, "MTN MoMo", "0772000111"), null), default);

        await using (var officer = As(c.Tenant, "henry@co.test", Roles.HrOfficer))
        {
            Assert.Empty(await officer.Changes.ListAsync(null, default));
            await Assert.ThrowsAsync<NotFoundException>(() => officer.Changes.ApproveAsync(aminas.Id, null, default));
        }
        await using (var grace = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            var pending = await grace.Changes.ListAsync(null, default);
            Assert.Equal([aminas.Id], pending.Select(p => p.Id)); // not her own
            Assert.Contains("own record", (await Assert.ThrowsAsync<DomainException>(() => grace.Changes.ApproveAsync(graces.Id, null, default))).Message);
        }
        await using (var paul = As(c.Tenant, "paul@co.test", Roles.PayrollOfficer))
        {
            Assert.Equal(2, (await paul.Changes.ListAsync(null, default)).Count);
            await paul.Changes.ApproveAsync(aminas.Id, null, default);
            Assert.Contains("why", (await Assert.ThrowsAsync<DomainException>(() => paul.Changes.RejectAsync(graces.Id, " ", default))).Message);
            await paul.Changes.RejectAsync(graces.Id, "Please use a number in your own name.", default);
        }
        await using (var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            var payment = (await hr.Employees.GetAsync(c.Amina, default)).Payment!;
            Assert.Equal((PaymentMethod.Bank, "9030012345678"), (payment.Method, payment.BankAccountNumber));
        }
        Assert.Contains(_email.Sent, m => m.Subject.Contains("payment details change is approved") && m.TextBody.Contains("didn't ask"));
        Assert.Contains(_email.Sent, m => m.Subject.Contains("declined") && m.TextBody.Contains("own name"));
    }

    [Fact]
    public async Task Employees_can_add_or_change_a_next_of_kin_and_withdraw_a_pending_request()
    {
        var c = await CompanyAsync();
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var add = await amina.Changes.RequestNextOfKinAsync(new RequestNextOfKinChange(null, new SaveNextOfKinRequest("John Nakato", "Brother", "0701 222333", null, null), null), default);
            Assert.Equal("Next of kin (new)", add.What);
            Assert.True(add.CanWithdraw);
            var withdrawn = await amina.Changes.WithdrawAsync(add.Id, default);
            Assert.Equal(ProfileChangeStatus.Withdrawn, withdrawn.Status);
            add = await amina.Changes.RequestNextOfKinAsync(new RequestNextOfKinChange(null, new SaveNextOfKinRequest("John Nakato", "Brother", "0701 222333", null, null), null), default);
            await using var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin);
            await hr.Changes.ApproveAsync(add.Id, null, default);
        }
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var kin = Assert.Single((await amina.Employees.MineAsync(default)).NextOfKin!);
            Assert.Equal("+256701222333", kin.Phone);
            var change = await amina.Changes.RequestNextOfKinAsync(new RequestNextOfKinChange(kin.Id, new SaveNextOfKinRequest("John Nakato", "Brother", "0701 444555", null, null), null), default);
            Assert.Equal(("Phone", "+256701222333", "+256701444555"), (change.Changes.Single().Label, change.Changes.Single().Before, change.Changes.Single().Proposed));
            Assert.Equal(3, (await amina.Changes.MineAsync(default)).Count);
        }
    }

    [Fact]
    public async Task Announcements_show_until_they_expire_and_only_HR_sees_expired_ones()
    {
        var c = await CompanyAsync();
        await using (var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            Assert.Contains("passed", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Announcements.CreateAsync(new SaveAnnouncementRequest("Old", "x", false, new DateOnly(2026, 11, 1)), default))).Message);
            var party = await hr.Announcements.CreateAsync(new SaveAnnouncementRequest("End of year party", "Friday 18 Dec at 4pm.", false, new DateOnly(2026, 12, 18)), default);
            await hr.Announcements.CreateAsync(new SaveAnnouncementRequest("New leave policy", "See the handbook.", true, null), default);
            // Expire the party notice by hand, as if the date had passed.
            await hr.Scope.Db.Database.ExecuteSqlInterpolatedAsync($"""update "Announcements" set "ExpiresOn" = {new DateOnly(2026, 11, 20)} where "Id" = {party.Id}""");
            Assert.Equal(2, (await hr.Announcements.ListAsync(all: true, default)).Count);
        }
        await using var amina = As(c.Tenant, "amina@co.test", Roles.Employee);
        var seen = await amina.Announcements.ListAsync(all: true, default);
        Assert.Equal(["New leave policy"], seen.Select(a => a.Title));
    }

    [Fact]
    public async Task A_manager_sees_their_whole_reporting_tree_and_an_inbox_of_what_waits_for_them()
    {
        var c = await CompanyAsync();
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
            await amina.Changes.RequestPersonalAsync(new RequestPersonalChange(With(await amina.Employees.MineAsync(default), address: "Kira"), null), default);

        await using (var mark = As(c.Tenant, "mark@co.test", Roles.LineManager))
        {
            var team = await mark.Team.MyTeamAsync(default);
            Assert.Equal([("Amina Nakato", true), ("Brian Okello", false)], team.Select(t => (t.Name, t.DirectReport)));
            Assert.NotNull(team[0].Today);
            var inbox = await mark.Team.InboxAsync(default);
            Assert.Equal((0, 0), (inbox.ProfileChanges, inbox.Total)); // profile changes are HR's, not the manager's
        }
        await using (var brian = As(c.Tenant, "brian@co.test", Roles.Employee))
            Assert.Empty(await brian.Team.MyTeamAsync(default));
        await using (var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
            Assert.Equal((1, 1), ((await hr.Team.InboxAsync(default)).ProfileChanges, (await hr.Team.InboxAsync(default)).Total));
    }
}
