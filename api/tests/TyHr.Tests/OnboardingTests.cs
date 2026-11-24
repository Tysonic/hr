using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Onboarding;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Onboarding;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Onboarding checklists and probation reviews. Grace runs HR (with her own record), Mark is a line manager, Ivan
/// looks after IT, and Nora is the new starter reporting to Mark. Today is 23 Nov 2026 (fixture clock).
/// </summary>
public class OnboardingTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly FakeEmail _email = new();

    private sealed record Services(Scope Scope, OnboardingService Onboarding, ProbationService Probation, EmployeeService Employees) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        return new Services(scope,
            new OnboardingService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<OnboardingService>.Instance),
            new ProbationService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<ProbationService>.Instance),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock));
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Ivan, Guid Nora);

    /// <summary>Nora started on 1 Jun 2026 with six months' probation, ending 30 Nov 2026.</summary>
    private async Task<Company> CompanyAsync(DateOnly? noraStarts = null)
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace", Roles.HrAdmin);
        var grace = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Atim", email: "grace@co.test"), default)).Id;
        var mark = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Opio", email: "mark@co.test"), default)).Id;
        var ivan = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Ivan", "Ssali", email: "ivan@co.test"), default)).Id;
        var start = noraStarts ?? new DateOnly(2026, 6, 1);
        var nora = EmployeeTests.NewEmployee("Nora", "Achieng", managerId: mark, email: "nora@co.test",
            contract: new SaveContractRequest(ContractType.Permanent, start, null, start.AddMonths(6).AddDays(-1), null));
        var noraId = (await hr.Employees.CreateAsync(nora with { Employment = nora.Employment with { HireDate = start } }, default)).Id;
        return new Company(co, grace, mark, ivan, noraId);
    }

    [Fact]
    public async Task Templates_have_one_default_and_unique_names()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        Assert.NotEmpty((await hr.Onboarding.SetupAsync(default)).Standard);
        var office = await hr.Onboarding.CreateTemplateAsync(new SaveOnboardingTemplateRequest("Office staff", [new("Desk", TaskOwner.Hr, 0, null)], false), default);
        Assert.True(office.IsDefault); // the first one is the default
        var drivers = await hr.Onboarding.CreateTemplateAsync(new SaveOnboardingTemplateRequest("Drivers", [new("Licence check", TaskOwner.Hr, -3, null)], true), default);
        Assert.Equal(["Drivers"], (await hr.Onboarding.SetupAsync(default)).Templates.Where(t => t.IsDefault).Select(t => t.Name));
        Assert.Contains("already a checklist", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Onboarding.CreateTemplateAsync(new SaveOnboardingTemplateRequest("drivers ", [new("x", TaskOwner.Hr, 0, null)], false), default))).Message);
        Assert.Contains("at least one task", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Onboarding.UpdateTemplateAsync(drivers.Id, new SaveOnboardingTemplateRequest("Drivers", [], true), default))).Message);
    }

    [Fact]
    public async Task Starting_onboarding_gives_each_task_to_its_owner_who_alone_can_tick_it_off()
    {
        var c = await CompanyAsync(new DateOnly(2026, 12, 1));
        Guid itTask, managerTask;
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            var started = await hr.Onboarding.StartAsync(c.Nora, new StartOnboardingRequest(null, c.Ivan), default);
            Assert.Equal(OnboardingTemplate.Standard.Length, started.Tasks);
            Assert.Empty(started.Warnings);
            Assert.Contains("already started", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Onboarding.StartAsync(c.Nora, new StartOnboardingRequest(null, null), default))).Message);
            var tasks = await hr.Onboarding.TasksAsync(c.Nora, default);
            Assert.Equal(new DateOnly(2026, 11, 26), tasks.First().DueOn); // contract 5 days before the start
            Assert.All(tasks.Where(t => t.Owner == TaskOwner.Manager), t => Assert.Equal("Mark Opio", t.AssigneeName));
            itTask = tasks.First(t => t.Owner == TaskOwner.It).Id;
            managerTask = tasks.First(t => t.Owner == TaskOwner.Manager).Id;
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["ivan@co.test"]) && m.Subject.Contains("Onboarding Nora Achieng: 2 tasks"));
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["nora@co.test"]) && m.Subject.StartsWith("Welcome"));

        await using (var ivan = As(c.Tenant, "ivan", Roles.Employee))
        {
            Assert.Equal(2, (await ivan.Onboarding.MyTasksAsync(default)).Count);
            var done = await ivan.Onboarding.CompleteAsync(itTask, "Laptop LT-114", default);
            Assert.True(done.IsDone);
            await Assert.ThrowsAsync<NotFoundException>(() => ivan.Onboarding.CompleteAsync(managerTask, null, default));
        }
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            await hr.Onboarding.CompleteAsync(managerTask, null, default); // HR can tick off anyone's
            var progress = Assert.Single(await hr.Onboarding.ProgressAsync(default));
            Assert.Equal((2, OnboardingTemplate.Standard.Length, 0), (progress.Done, progress.Total, progress.Overdue));
        }
    }

    [Fact]
    public async Task Onboarding_without_a_manager_or_IT_contact_leaves_those_tasks_to_HR_and_says_so()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        var loner = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Lona", "Kirabo"), default)).Id;
        var started = await hr.Onboarding.StartAsync(loner, new StartOnboardingRequest(null, null), default);
        Assert.Equal(3, started.Warnings.Count);
        Assert.Contains(started.Warnings, w => w.Contains("no line manager"));
        Assert.All((await hr.Onboarding.TasksAsync(loner, default)).Where(t => t.Owner != TaskOwner.NewHire), t => Assert.Null(t.AssigneeId));
    }

    [Fact]
    public async Task The_manager_recommends_and_HR_decides_and_an_extension_moves_the_probation_end()
    {
        var c = await CompanyAsync();
        ProbationDueDto due;
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            due = Assert.Single(await mark.Probation.DueAsync(default));
            Assert.Equal(("Nora Achieng", 7, true, false), (due.EmployeeName, due.DaysLeft, due.CanRecommend, due.CanDecide));
            Assert.Contains("after the current one", (await Assert.ThrowsAsync<DomainException>(() => mark.Probation.RecommendAsync(due.ContractId,
                new RecommendProbationRequest(ProbationOutcome.Extend, new DateOnly(2026, 11, 1), "Needs time"), default))).Message);
            due = await mark.Probation.RecommendAsync(due.ContractId,
                new RecommendProbationRequest(ProbationOutcome.Extend, new DateOnly(2027, 2, 28), "Good attitude; reports still need work."), default);
            Assert.Equal(ProbationOutcome.Extend, due.Review!.Recommendation);
        }
        await using (var ivan = As(c.Tenant, "ivan", Roles.LineManager))
        {
            Assert.Empty(await ivan.Probation.DueAsync(default)); // not his report
            await Assert.ThrowsAsync<NotFoundException>(() => ivan.Probation.RecommendAsync(due.ContractId,
                new RecommendProbationRequest(ProbationOutcome.Confirm, null, "Fine"), default));
        }
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.True(Assert.Single(await hr.Probation.DueAsync(default)).CanDecide);
            Assert.Equal(1, await hr.Probation.WaitingCountAsync(default));
            await hr.Probation.DecideAsync(due.ContractId, new DecideProbationRequest(ProbationOutcome.Extend, new DateOnly(2027, 2, 28), null), default);
            var contract = Assert.Single((await hr.Employees.GetAsync(c.Nora, default)).Contracts);
            Assert.Equal((new DateOnly(2027, 2, 28), (DateOnly?)null), (contract.ProbationEndDate!.Value, contract.ConfirmedOn));
            Assert.Empty(await hr.Probation.DueAsync(default)); // next due in February
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["nora@co.test"]) && m.Subject.Contains("extended"));
    }

    [Fact]
    public async Task HR_can_confirm_without_a_review_given_a_note_but_never_their_own_probation()
    {
        var c = await CompanyAsync();
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            var contract = Assert.Single(await hr.Probation.DueAsync(default)).ContractId;
            Assert.Contains("note", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Probation.DecideAsync(contract, new DecideProbationRequest(ProbationOutcome.Confirm, null, null), default))).Message);
            var after = await hr.Probation.DecideAsync(contract, new DecideProbationRequest(ProbationOutcome.Confirm, null, "Manager agreed by phone"), default);
            Assert.Equal(ProbationReviewStatus.Confirmed, Assert.Single(after.History).Status);
            Assert.Equal(new DateOnly(2026, 11, 23), Assert.Single((await hr.Employees.GetAsync(c.Nora, default)).Contracts).ConfirmedOn);

            // Grace's own probation, which only another HR person may decide.
            var own = await hr.Employees.AddContractAsync(c.Grace, new SaveContractRequest(ContractType.Permanent, new DateOnly(2026, 9, 1), null,
                new DateOnly(2026, 11, 30), null), default);
            var mine = Assert.Single(await hr.Probation.DueAsync(default), d => d.EmployeeId == c.Grace);
            // A probation that ended long ago without a decision is an old record, not a review to chase.
            await hr.Employees.AddContractAsync(c.Ivan, new SaveContractRequest(ContractType.Permanent, new DateOnly(2025, 1, 6), null, new DateOnly(2025, 7, 5), null), default);
            Assert.DoesNotContain(await hr.Probation.DueAsync(default), d => d.EmployeeId == c.Ivan);
            Assert.False(mine.CanDecide);
            Assert.Contains("Someone else", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Probation.DecideAsync(mine.ContractId, new DecideProbationRequest(ProbationOutcome.Confirm, null, "Me"), default))).Message);
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["nora@co.test"]) && m.Subject.Contains("confirmed"));
    }
}
