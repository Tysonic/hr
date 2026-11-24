using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Performance;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Performance;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Review cycles and appraisals. Grace runs HR (and has her own record, with no manager); Amina reports to Mark;
/// Lona has no manager. Today is 23 Nov 2026 (fixture clock).
/// </summary>
public class PerformanceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private readonly FakeEmail _email = new();

    private sealed record Services(Scope Scope, PerformanceService Performance, EmployeeService Employees, ImprovementPlanService Plans,
        SalaryReviewService Salaries) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var access = new EmployeeAccess(scope.Db, scope.User);
        var employees = new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock);
        return new Services(scope, new PerformanceService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<PerformanceService>.Instance),
            employees, new ImprovementPlanService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<ImprovementPlanService>.Instance),
            new SalaryReviewService(scope.Db, scope.TenantContext, scope.User, access, employees, fx.Clock));
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Amina, Guid Lona);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace", Roles.HrAdmin);
        var grace = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Atim", email: "grace@co.test"), default)).Id;
        var mark = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Opio", email: "mark@co.test"), default)).Id;
        var amina = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", managerId: mark, email: "amina@co.test"), default)).Id;
        var lona = (await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Lona", "Kirabo", email: "lona@co.test"), default)).Id;
        return new Company(co, grace, mark, amina, lona);
    }

    private static CycleDetails Annual(bool selfReview = true, params Guid[] departments) =>
        new("2026 annual review", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), selfReview, new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 20), null, departments);

    private async Task<(CycleDto Cycle, Guid Amina, Guid Lona, Guid Grace)> LaunchedAsync(Company c, bool selfReview = true)
    {
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        var cycle = await hr.Performance.CreateCycleAsync(Annual(selfReview), default);
        cycle = (await hr.Performance.LaunchAsync(cycle.Id, default)).Cycle;
        Guid Of(Guid e) => cycle.Appraisals.Single(a => a.EmployeeId == e).Id;
        return (cycle, Of(c.Amina), Of(c.Lona), Of(c.Grace));
    }

    private static SaveGoal Goal(string title, int weight, Guid? id = null) => new(id, title, "Measured monthly", weight);

    [Fact]
    public async Task Launching_makes_an_appraisal_for_everyone_in_scope_with_their_line_manager()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace", Roles.HrAdmin);
        Assert.Contains("five labels", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Performance.CreateCycleAsync(Annual() with { Scale = ["Bad", "Good"] }, default))).Message);
        var cycle = await hr.Performance.CreateCycleAsync(Annual(), default);
        Assert.Equal(ReviewCycle.DefaultScale, cycle.Scale);
        var launched = await hr.Performance.LaunchAsync(cycle.Id, default);
        Assert.Equal(4, launched.Cycle.Appraisals.Count);
        Assert.Equal("Mark Opio", launched.Cycle.Appraisals.Single(a => a.EmployeeId == c.Amina).ManagerName);
        Assert.Contains(launched.Warnings, w => w.Contains("3 people have no line manager"));
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["amina@co.test"]) && m.Subject.Contains("set your goals"));
        Assert.Contains("already started", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.LaunchAsync(cycle.Id, default))).Message);
        Assert.Contains("can't change", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Performance.UpdateCycleAsync(cycle.Id, Annual(selfReview: false), default))).Message);

        var empty = await hr.Performance.CreateCycleAsync(Annual(true, Guid.NewGuid()) with { Name = "Sales only" }, default);
        Assert.Contains("Nobody", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.LaunchAsync(empty.Id, default))).Message);
    }

    [Fact]
    public async Task Goals_are_weighted_to_100_and_agreed_then_self_and_manager_ratings_give_a_weighted_result()
    {
        var c = await CompanyAsync();
        var (cycle, amina, _, _) = await LaunchedAsync(c);

        AppraisalDto a;
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
        {
            a = await me.Performance.SaveGoalsAsync(amina, [Goal("Close the books by day 5", 60), Goal("Train a junior", 50)], default);
            Assert.Contains("add up to 110%", (await Assert.ThrowsAsync<DomainException>(() => me.Performance.SubmitGoalsAsync(amina, default))).Message);
            a = await me.Performance.SaveGoalsAsync(amina, [Goal("Close the books by day 5", 60, a.Goals[0].Id), Goal("Train a junior", 40, a.Goals[1].Id)], default);
            a = await me.Performance.SubmitGoalsAsync(amina, default);
            Assert.False(a.Can.AgreeGoals);
            await Assert.ThrowsAsync<DomainException>(() => me.Performance.AgreeGoalsAsync(amina, default));
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["mark@co.test"]) && m.Subject.Contains("goals to agree"));

        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            var team = await mark.Performance.MineAsync(default);
            Assert.True(Assert.Single(team.Team).ActionDue);
            a = await mark.Performance.AgreeGoalsAsync(amina, default);
            Assert.Equal(AppraisalStatus.GoalSetting, a.Status); // reviews haven't opened yet
        }
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
            Assert.Contains((await hr.Performance.StartReviewsAsync(cycle.Id, default)).Warnings, w => w.Contains("3 people haven't agreed goals"));

        var goals = a.Goals;
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
        {
            a = await me.Performance.SelfReviewAsync(amina, new SaveReviewRequest([new(goals[0].Id, 5, "Always on time")], null, false), default);
            Assert.Contains("every goal", (await Assert.ThrowsAsync<DomainException>(() =>
                me.Performance.SelfReviewAsync(amina, new SaveReviewRequest([], "Good year", true), default))).Message);
            a = await me.Performance.SelfReviewAsync(amina, new SaveReviewRequest([new(goals[1].Id, 4, null)], "Good year", true), default);
            Assert.Equal(AppraisalStatus.ManagerReview, a.Status);
        }
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            a = await mark.Performance.AppraisalAsync(amina, default);
            Assert.Equal([5, 4], a.Goals.Select(g => g.SelfRating)); // visible now she has sent them
            Assert.Contains("Overall comments", (await Assert.ThrowsAsync<DomainException>(() => mark.Performance.ManagerReviewAsync(amina,
                new SaveReviewRequest([new(goals[0].Id, 4, null), new(goals[1].Id, 3, null)], null, true), default))).Message);
        }
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            Assert.All((await me.Performance.AppraisalAsync(amina, default)).Goals, g => Assert.Null(g.ManagerRating)); // Mark's draft stays hidden
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            a = await mark.Performance.ManagerReviewAsync(amina,
                new SaveReviewRequest([new(goals[0].Id, 4, null), new(goals[1].Id, 3, null)], "Solid year; coach more.", true), default);
            Assert.Equal((AppraisalStatus.Completed, 3.6m), (a.Status, a.OverallRating!.Value)); // 4 × 60% + 3 × 40%
        }
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
        {
            a = await me.Performance.AppraisalAsync(amina, default);
            Assert.Equal((3.6m, "Solid year; coach more."), (a.OverallRating!.Value, a.ManagerComment));
            a = await me.Performance.AcknowledgeAsync(amina, "Thanks", default);
            Assert.NotNull(a.AcknowledgedAt);
        }
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
            Assert.Equal(1, (await hr.Performance.CycleAsync(cycle.Id, default)).Distribution[4]);
    }

    [Fact]
    public async Task HR_reviews_people_without_a_manager_but_never_themselves_and_outsiders_see_nothing()
    {
        var c = await CompanyAsync();
        var (cycle, amina, lona, grace) = await LaunchedAsync(c, selfReview: false);
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            await hr.Performance.StartReviewsAsync(cycle.Id, default);
            var a = await hr.Performance.SaveGoalsAsync(lona, [Goal("Answer the phones", 100)], default);
            a = await hr.Performance.AgreeGoalsAsync(lona, default);
            Assert.Equal(AppraisalStatus.ManagerReview, a.Status); // reviews are open and there's no self-review
            Assert.True(a.Can.ManagerReview);

            var own = await hr.Performance.AppraisalAsync(grace, default);
            Assert.Equal(AppraisalRole.Employee, own.Role);
            Assert.False(own.Can.AgreeGoals);
            await hr.Performance.SaveGoalsAsync(grace, [Goal("Run the review cycle", 100)], default);
            Assert.Contains("manager agrees", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.AgreeGoalsAsync(grace, default))).Message);
            Assert.Contains("review themselves", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.ChangeManagerAsync(grace, c.Grace, default))).Message);

            // Mark's appraisal of Amina isn't HR's to do.
            Assert.False((await hr.Performance.AppraisalAsync(amina, default)).Can.AgreeGoals);
        }
        await using (var lonaSelf = As(c.Tenant, "lona", Roles.Employee))
            await Assert.ThrowsAsync<NotFoundException>(() => lonaSelf.Performance.AppraisalAsync(amina, default));

        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            var closed = await hr.Performance.CloseAsync(cycle.Id, default);
            Assert.Contains(closed.Warnings, w => w.Contains("4 reviews were not finished"));
            Assert.Contains("closed", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.SaveGoalsAsync(lona, [Goal("x", 100)], default))).Message);
        }
    }

    [Fact]
    public async Task Reopening_agreed_goals_clears_draft_self_ratings()
    {
        var c = await CompanyAsync();
        var (cycle, amina, _, _) = await LaunchedAsync(c);
        Guid goal;
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            goal = (await me.Performance.SaveGoalsAsync(amina, [Goal("Close the books", 100)], default)).Goals[0].Id;
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
            await mark.Performance.AgreeGoalsAsync(amina, default);
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
            await hr.Performance.StartReviewsAsync(cycle.Id, default);
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            await me.Performance.SelfReviewAsync(amina, new SaveReviewRequest([new(goal, 5, null)], null, false), default);
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            var a = await mark.Performance.ReopenGoalsAsync(amina, default);
            Assert.Equal(AppraisalStatus.GoalSetting, a.Status);
        }
        await using var check = As(c.Tenant, "grace", Roles.HrAdmin);
        Assert.Null((await check.Scope.Db.Goals.SingleAsync(g => g.Id == goal)).SelfRating);
    }

    /// <summary>Takes Amina's appraisal to the manager's review: one goal (60%) and another (40%), agreed and self-rated.</summary>
    private async Task<(Guid Cycle, Guid Amina, Guid[] Goals)> AtManagerReviewAsync(Company c, CycleDetails details)
    {
        Guid cycleId, amina;
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            var cycle = await hr.Performance.CreateCycleAsync(details, default);
            cycle = (await hr.Performance.LaunchAsync(cycle.Id, default)).Cycle;
            (cycleId, amina) = (cycle.Id, cycle.Appraisals.Single(a => a.EmployeeId == c.Amina).Id);
        }
        Guid[] goals;
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            goals = (await me.Performance.SaveGoalsAsync(amina, [Goal("Close the books", 60), Goal("Train a junior", 40)], default)).Goals.Select(g => g.Id).ToArray();
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager)) await mark.Performance.AgreeGoalsAsync(amina, default);
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin)) await hr.Performance.StartReviewsAsync(cycleId, default);
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            await me.Performance.SelfReviewAsync(amina, new SaveReviewRequest([new(goals[0], 4, null), new(goals[1], 4, null)], null, true), default);
        return (cycleId, amina, goals);
    }

    private static SaveReviewRequest MarksReview(Guid[] goals) => new([new(goals[0], 4, null), new(goals[1], 3, null)], "Solid year.", true);

    [Fact]
    public async Task Colleagues_feedback_goes_to_the_reviewer_only_and_HR_calibrates_before_results_are_released()
    {
        var c = await CompanyAsync();
        var (cycle, amina, goals) = await AtManagerReviewAsync(c, Annual() with { PeerFeedback = true, Calibrate = true });
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            Assert.Contains("self-review", (await Assert.ThrowsAsync<DomainException>(() => mark.Performance.RequestFeedbackAsync(amina, [c.Amina], default))).Message);
            var a = await mark.Performance.RequestFeedbackAsync(amina, [c.Lona, c.Grace], default);
            Assert.Equal(2, a.Feedback.Count);
            await mark.Performance.RequestFeedbackAsync(amina, [c.Lona], default); // asked already: skipped
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["lona@co.test"]) && m.Subject.Contains("feedback on Amina Nakato"));
        await using (var lona = As(c.Tenant, "lona", Roles.Employee))
        {
            var request = Assert.Single((await lona.Performance.MineAsync(default)).Feedback);
            Assert.Equal(["Close the books", "Train a junior"], request.Goals);
            Assert.Contains("strengths", (await Assert.ThrowsAsync<DomainException>(() =>
                lona.Performance.SubmitFeedbackAsync(request.Id, new SubmitFeedbackRequest(4, " ", null), default))).Message);
            Assert.Equal(FeedbackStatus.Submitted, (await lona.Performance.SubmitFeedbackAsync(request.Id, new SubmitFeedbackRequest(4, "Always helpful", null), default)).Status);
        }
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            Assert.Empty((await me.Performance.AppraisalAsync(amina, default)).Feedback);

        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            var a = await mark.Performance.AppraisalAsync(amina, default);
            Assert.Equal("Always helpful", a.Feedback.Single(f => f.Status == FeedbackStatus.Submitted).Strengths);
            a = await mark.Performance.ManagerReviewAsync(amina, MarksReview(goals), default);
            Assert.Equal((AppraisalStatus.Calibration, 3.6m), (a.Status, a.FinalRating!.Value));
        }
        Assert.DoesNotContain(_email.Sent, m => m.Subject.Contains("your review is ready"));
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
            Assert.Null((await me.Performance.AppraisalAsync(amina, default)).FinalRating);

        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.Contains("Say why", (await Assert.ThrowsAsync<DomainException>(() =>
                hr.Performance.SetFinalRatingAsync(amina, new FinalRatingRequest(4.0m, null), default))).Message);
            await hr.Performance.SetFinalRatingAsync(amina, new FinalRatingRequest(4.0m, "In line with peers in Finance"), default);
            Assert.Contains("Release", (await Assert.ThrowsAsync<DomainException>(() => hr.Performance.CloseAsync(cycle, default))).Message);
            var released = await hr.Performance.ReleaseResultsAsync(cycle, default);
            Assert.Equal(1, released.Cycle.Distribution[4]);
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["amina@co.test"]) && m.Subject.Contains("your review is ready"));
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
        {
            var a = await me.Performance.AppraisalAsync(amina, default);
            Assert.Equal((AppraisalStatus.Completed, 4.0m, 3.6m, (string?)null), (a.Status, a.FinalRating!.Value, a.OverallRating!.Value, a.CalibrationNote));
        }
    }

    [Fact]
    public async Task A_salary_review_turns_final_ratings_into_raises_for_people_who_may_set_pay()
    {
        var c = await CompanyAsync();
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
            await hr.Employees.AddSalaryAsync(c.Amina, new AddSalaryRequest(new DateOnly(2025, 1, 6), 1_000_000, "Starting salary"), default);
        var (cycle, amina, goals) = await AtManagerReviewAsync(c, Annual());
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager)) await mark.Performance.ManagerReviewAsync(amina, MarksReview(goals), default);

        await using (var henry = As(c.Tenant, "henry", Roles.HrOfficer))
            await Assert.ThrowsAsync<NotFoundException>(() => henry.Salaries.GetAsync(cycle, default));
        await using var paul = As(c.Tenant, "paul", Roles.PayrollOfficer);
        var review = await paul.Salaries.SaveGuidelineAsync(cycle, new SaveGuidelineRequest([0, 0, 3, 5, 8], new DateOnly(2027, 1, 1)), default);
        var rec = Assert.Single(review.Recommendations);
        Assert.Equal((3.6m, 5m, 1_050_000m), (rec.Rating, rec.Percent, rec.ProposedSalary)); // 3.6 rounds to 4: 5%
        Assert.Contains(review.Left, l => l.Contains("3 reviews aren't complete"));
        Assert.Contains("doesn't cut pay", (await Assert.ThrowsAsync<DomainException>(() =>
            paul.Salaries.AdjustAsync(rec.Id, new AdjustRecommendationRequest(900_000, null, false), default))).Message);
        review = await paul.Salaries.AdjustAsync(rec.Id, new AdjustRecommendationRequest(1_060_000, "Market adjustment", false), default);
        review = await paul.Salaries.SaveGuidelineAsync(cycle, new SaveGuidelineRequest([0, 0, 4, 6, 10], new DateOnly(2027, 1, 1)), default);
        Assert.Equal(1_060_000m, review.Recommendations[0].ProposedSalary); // adjusted by hand, so the guideline leaves it alone

        var applied = await paul.Salaries.ApplyAsync(cycle, default);
        Assert.Equal(1, applied.Applied);
        var change = await paul.Scope.Db.SalaryChanges.SingleAsync(s => s.EmployeeId == c.Amina && s.EffectiveDate == new DateOnly(2027, 1, 1));
        Assert.Equal((1_060_000m, "Salary review: 2026 annual review"), (change.MonthlyBasic, change.Reason));
        Assert.Contains("no raises", (await Assert.ThrowsAsync<DomainException>(() => paul.Salaries.ApplyAsync(cycle, default))).Message);
    }

    [Fact]
    public async Task Managers_run_improvement_plans_for_their_team_which_the_employee_acknowledges()
    {
        var c = await CompanyAsync();
        var details = new PlanDetails("Month-end close is often late.", [new("Close the books by day 5", "Each month"), new("Reconcile bank daily", null)],
            "Weekly coaching with Mark", new DateOnly(2026, 11, 2), new DateOnly(2027, 1, 29));
        PlanDto plan;
        await using (var mark = As(c.Tenant, "mark", Roles.LineManager))
        {
            await Assert.ThrowsAsync<NotFoundException>(() => mark.Plans.CreateAsync(new CreatePlanRequest(c.Lona, null, null, details), default)); // not his team
            plan = await mark.Plans.CreateAsync(new CreatePlanRequest(c.Amina, null, null, details), default);
            Assert.Equal(("Mark Opio", PlanStatus.Active), (plan.OwnerName, plan.Status));
            Assert.Contains("already has", (await Assert.ThrowsAsync<DomainException>(() =>
                mark.Plans.CreateAsync(new CreatePlanRequest(c.Amina, null, null, details), default))).Message);
            Assert.Contains("six months", (await Assert.ThrowsAsync<DomainException>(() =>
                mark.Plans.UpdateAsync(plan.Id, details with { EndDate = new DateOnly(2027, 6, 1) }, plan.OwnerId, default))).Message);
            plan = await mark.Plans.CheckInAsync(plan.Id, new AddCheckInRequest(new DateOnly(2026, 11, 20), PlanProgress.AtRisk, "October close was day 8."), default);
            Assert.Single(plan.CheckIns);
        }
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["amina@co.test"]) && m.Subject.Contains("improvement plan"));
        await using (var me = As(c.Tenant, "amina", Roles.Employee))
        {
            var mine = await me.Plans.GetAsync(plan.Id, default);
            Assert.Equal((true, false), (mine.IsMine, mine.CanManage));
            await Assert.ThrowsAsync<DomainException>(() => me.Plans.CheckInAsync(plan.Id, new AddCheckInRequest(new DateOnly(2026, 11, 20), PlanProgress.OnTrack, "x"), default));
            Assert.NotNull((await me.Plans.AcknowledgeAsync(plan.Id, "I'll work on it", default)).AcknowledgedAt);
        }
        await using (var lona = As(c.Tenant, "lona", Roles.Employee))
            await Assert.ThrowsAsync<NotFoundException>(() => lona.Plans.GetAsync(plan.Id, default));
        await using (var hr = As(c.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.Contains("Outcome", (await Assert.ThrowsAsync<DomainException>(() => hr.Plans.CloseAsync(plan.Id, new ClosePlanRequest(PlanStatus.Succeeded, ""), default))).Message);
            Assert.Equal(PlanStatus.Succeeded, (await hr.Plans.CloseAsync(plan.Id, new ClosePlanRequest(PlanStatus.Succeeded, "Closed on day 4 for two months."), default)).Status);
        }
    }
}
