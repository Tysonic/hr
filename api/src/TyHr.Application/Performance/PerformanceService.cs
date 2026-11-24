using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Performance;
using PeerFeedback = TyHr.Domain.Performance.PeerFeedback;

namespace TyHr.Application.Performance;

/// <summary>
/// Review cycles and appraisals. HR runs cycles; each employee and their manager work through goals, self-review and
/// the manager's review. Nobody reviews themselves; an appraisal without a manager is HR's to complete.
/// </summary>
public class PerformanceService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email,
    TimeProvider clock, ILogger<PerformanceService> logger)
{
    private bool IsHr => user.Can(Permissions.PerformanceManage);

    // Cycles (HR)

    public async Task<IReadOnlyList<CycleSummaryDto>> CyclesAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var counts = await db.Appraisals.GroupBy(a => a.CycleId)
            .Select(g => new { g.Key, Total = g.Count(), Done = g.Count(a => a.Status == AppraisalStatus.Completed) }).ToDictionaryAsync(x => x.Key, ct);
        return (await db.ReviewCycles.AsNoTracking().OrderByDescending(c => c.PeriodEnd).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new CycleSummaryDto(c.Id, c.Name, c.PeriodStart, c.PeriodEnd, c.Status, c.SelfReview, c.GoalsDue, c.ReviewsDue,
                counts.GetValueOrDefault(c.Id)?.Total ?? 0, counts.GetValueOrDefault(c.Id)?.Done ?? 0)).ToList();
    }

    public async Task<CycleDto> CycleAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await FindCycleAsync(id, ct), ct);

    public async Task<CycleDto> CreateCycleAsync(CycleDetails d, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var cycle = new ReviewCycle(d, user.UserName, clock.GetUtcNow());
        db.ReviewCycles.Add(cycle);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(cycle, ct);
    }

    public async Task<CycleDto> UpdateCycleAsync(Guid id, CycleDetails d, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        cycle.Update(d);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(cycle, ct);
    }

    public async Task DeleteCycleAsync(Guid id, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        if (cycle.Status != CycleStatus.Draft) throw new DomainException("Only a cycle that hasn't started can be deleted.");
        db.ReviewCycles.Remove(cycle);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Starts goal setting: an appraisal for each current employee in scope hired by the period's end.</summary>
    public async Task<CycleActionResult> LaunchAsync(Guid id, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        var depts = cycle.DepartmentIds;
        var people = await db.Employees.Where(e => e.Status != EmployeeStatus.Exited && e.HireDate <= cycle.PeriodEnd
                                                   && (depts.Count == 0 || (e.DepartmentId != null && depts.Contains(e.DepartmentId.Value))))
            .ToListAsync(ct);
        if (people.Count == 0) throw new DomainException("Nobody is in this cycle's scope.");
        cycle.Launch(clock.GetUtcNow());
        db.Appraisals.AddRange(people.Select(e => new Appraisal(cycle.Id, e.Id, e.ManagerId)));
        await db.SaveChangesAsync(ct);

        var warnings = new List<string>();
        var noManager = people.Count(e => e.ManagerId == null);
        if (noManager > 0) warnings.Add($"{noManager} {(noManager == 1 ? "person has" : "people have")} no line manager, so HR does their manager review.");
        var noEmail = people.Count(e => e.WorkEmail == null);
        if (noEmail > 0) warnings.Add($"{noEmail} {(noEmail == 1 ? "person has" : "people have")} no work email, so can't sign in to set goals or review themselves.");
        var due = cycle.GoalsDue is { } g ? $" by {g:d MMMM yyyy}" : "";
        foreach (var e in people.Where(e => e.WorkEmail != null))
            await SendAsync(e.WorkEmail!, $"{cycle.Name}: time to set your goals",
                $"Dear {e.FirstName},\n\n{cycle.Name} has started. Please agree your goals for {cycle.PeriodStart:d MMM yyyy} to {cycle.PeriodEnd:d MMM yyyy} with your manager{due}.\n\nOpen TyHR and go to My reviews.", ct);
        return new CycleActionResult(await ToDtoAsync(cycle, ct), warnings);
    }

    /// <summary>Opens reviews. Appraisals whose goals aren't agreed yet join as soon as they are.</summary>
    public async Task<CycleActionResult> StartReviewsAsync(Guid id, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        cycle.StartReviews();
        var appraisals = await db.Appraisals.Where(a => a.CycleId == id).ToListAsync(ct);
        foreach (var a in appraisals) a.StartReview(cycle);
        await db.SaveChangesAsync(ct);
        var waiting = appraisals.Count(a => a.Status == AppraisalStatus.GoalSetting);
        var people = await db.Employees.AsNoTracking().Where(e => appraisals.Select(a => a.EmployeeId).Contains(e.Id) && e.WorkEmail != null).ToListAsync(ct);
        var due = cycle.ReviewsDue is { } r ? $" by {r:d MMMM yyyy}" : "";
        foreach (var e in people)
        {
            var a = appraisals.Single(x => x.EmployeeId == e.Id);
            if (a.Status == AppraisalStatus.SelfReview)
                await SendAsync(e.WorkEmail!, $"{cycle.Name}: your self-review", $"Dear {e.FirstName},\n\nPlease rate yourself against your goals{due}.\n\nOpen TyHR and go to My reviews.", ct);
        }
        var managerIds = appraisals.Where(a => a.Status == AppraisalStatus.ManagerReview && a.ManagerId != null).Select(a => a.ManagerId!.Value).Distinct().ToList();
        foreach (var m in await db.Employees.AsNoTracking().Where(e => managerIds.Contains(e.Id) && e.WorkEmail != null).ToListAsync(ct))
            await SendAsync(m.WorkEmail!, $"{cycle.Name}: reviews for your team", $"Dear {m.FirstName},\n\nYour team's reviews are open{due}.\n\nOpen TyHR and go to My reviews.", ct);
        var warnings = waiting > 0 ? new[] { $"{waiting} {(waiting == 1 ? "person hasn't" : "people haven't")} agreed goals yet; they move to review once they do." } : [];
        return new CycleActionResult(await ToDtoAsync(cycle, ct), warnings);
    }

    public async Task<CycleActionResult> CloseAsync(Guid id, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        if (cycle.HoldsResults && await db.Appraisals.AnyAsync(a => a.CycleId == id && a.Status == AppraisalStatus.Calibration, ct))
            throw new DomainException("Release the calibrated results before closing the cycle.");
        cycle.Close(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        var open = await db.Appraisals.CountAsync(a => a.CycleId == id && a.Status != AppraisalStatus.Completed, ct);
        return new CycleActionResult(await ToDtoAsync(cycle, ct),
            open > 0 ? [$"{open} review{(open == 1 ? " was" : "s were")} not finished and stay{(open == 1 ? "s" : "")} that way."] : []);
    }

    public async Task<AppraisalDto> ChangeManagerAsync(Guid appraisalId, Guid? managerId, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(appraisalId, ct);
        CheckOpen(cycle);
        if (managerId is { } m && !await db.Employees.AnyAsync(e => e.Id == m && e.Status != EmployeeStatus.Exited, ct))
            throw new DomainException("The reviewer must be a current employee.");
        a.ChangeManager(managerId);
        await db.SaveChangesAsync(ct);
        return await AppraisalAsync(appraisalId, ct);
    }

    private async Task<ReviewCycle> FindCycleAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.ReviewCycles.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("There is no such review cycle.");
    }

    private async Task<CycleDto> ToDtoAsync(ReviewCycle c, CancellationToken ct)
    {
        var appraisals = await db.Appraisals.AsNoTracking().Where(a => a.CycleId == c.Id).ToListAsync(ct);
        var ids = appraisals.Select(a => a.EmployeeId).Concat(appraisals.Where(a => a.ManagerId != null).Select(a => a.ManagerId!.Value)).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.EmployeeNumber, e.DepartmentId }).ToDictionaryAsync(e => e.Id, ct);
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var appraisalIds = appraisals.Select(a => a.Id).ToList();
        var feedback = (await db.PeerFeedback.AsNoTracking().Where(f => appraisalIds.Contains(f.AppraisalId)).Select(f => new { f.AppraisalId, f.Status }).ToListAsync(ct))
            .ToLookup(f => f.AppraisalId);
        var rows = appraisals.Select(a =>
        {
            var e = people[a.EmployeeId];
            return new AppraisalRowDto(a.Id, a.EmployeeId, e.Name, e.EmployeeNumber, e.DepartmentId is { } d ? depts.GetValueOrDefault(d) : null, a.ManagerId,
                a.ManagerId is { } m ? people.GetValueOrDefault(m)?.Name : null, a.Status, a.GoalsAgreed, a.GoalsSubmittedAt != null, a.OverallRating,
                a.FinalRating, a.CalibrationNote, a.AcknowledgedAt != null, feedback[a.Id].Count(), feedback[a.Id].Count(f => f.Status == FeedbackStatus.Submitted));
        }).OrderBy(r => r.EmployeeName).ToList();
        var distribution = Enumerable.Range(1, 5).ToDictionary(i => i,
            i => appraisals.Count(a => a.FinalRating is { } r && (int)Math.Round(r, MidpointRounding.AwayFromZero) == i));
        return new CycleDto(c.Id, c.Name, c.PeriodStart, c.PeriodEnd, c.Status, c.SelfReview, c.PeerFeedback, c.Calibrate, c.ResultsReleasedAt, c.GoalsDue,
            c.ReviewsDue, c.Scale, c.DepartmentIds, c.CreatedBy,
            c.LaunchedAt, c.ClosedAt, rows, distribution);
    }

    // Appraisals (employee, manager, HR)

    public async Task<MyAppraisalsDto> MineAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        var asHrManager = IsHr; // HR completes appraisals that have no manager
        var list = await db.Appraisals.AsNoTracking()
            .Join(db.ReviewCycles.AsNoTracking(), a => a.CycleId, c => c.Id, (a, c) => new { a, c })
            .Where(x => x.a.EmployeeId == me || x.a.ManagerId == me || (asHrManager && x.a.ManagerId == null && x.a.EmployeeId != me))
            .OrderByDescending(x => x.c.PeriodEnd).ToListAsync(ct);
        var ids = list.Select(x => x.a.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        AppraisalListItem Item(Appraisal a, ReviewCycle c, bool asEmployee)
        {
            var open = c.Status != CycleStatus.Closed;
            var due = open && (asEmployee
                ? a.Status == AppraisalStatus.GoalSetting && a.GoalsSubmittedAt == null || a.Status == AppraisalStatus.SelfReview
                  || a.Status == AppraisalStatus.Completed && a.AcknowledgedAt == null
                : a.Status == AppraisalStatus.GoalSetting && a.GoalsSubmittedAt != null && !a.GoalsAgreed || a.Status == AppraisalStatus.ManagerReview);
            if (!open && asEmployee) due = a.Status == AppraisalStatus.Completed && a.AcknowledgedAt == null;
            return new AppraisalListItem(a.Id, c.Name, c.Status, names[a.EmployeeId], a.Status, a.GoalsAgreed, a.GoalsSubmittedAt != null,
                asEmployee && a.Status != AppraisalStatus.Completed ? null : a.FinalRating, a.AcknowledgedAt != null,
                a.Status == AppraisalStatus.GoalSetting ? c.GoalsDue : c.ReviewsDue, due);
        }
        var mine = list.Where(x => x.a.EmployeeId == me).Select(x => Item(x.a, x.c, true)).ToList();
        var team = list.Where(x => x.a.EmployeeId != me).Select(x => Item(x.a, x.c, false)).OrderBy(i => i.EmployeeName).ToList();
        var feedback = me == null ? [] : await FeedbackRequestsAsync(me.Value, null, ct);
        var plans = me == null ? [] : await db.ImprovementPlans.AsNoTracking().Where(p => p.EmployeeId == me && p.Status == PlanStatus.Active)
            .Select(p => p.AcknowledgedAt == null).ToListAsync(ct);
        return new MyAppraisalsDto(mine, team, feedback, plans.Count,
            mine.Count(i => i.ActionDue) + team.Count(i => i.ActionDue) + feedback.Count(f => f.Open && f.Status == FeedbackStatus.Requested) + plans.Count(unread => unread));
    }

    public async Task<AppraisalDto> AppraisalAsync(Guid id, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        var role = await RoleAsync(a, ct);
        var goals = await GoalsAsync(id, ct);
        var people = await db.Employees.AsNoTracking().Where(e => e.Id == a.EmployeeId || e.Id == a.ManagerId).ToDictionaryAsync(e => e.Id, ct);
        var employee = people[a.EmployeeId];
        var position = employee.PositionId is { } p ? await db.Positions.Where(x => x.Id == p).Select(x => x.Title).SingleOrDefaultAsync(ct) : null;
        var open = cycle.Status != CycleStatus.Closed;
        var isEmployee = role == AppraisalRole.Employee;
        var isReviewer = role == AppraisalRole.Manager || (role == AppraisalRole.Hr && a.ManagerId == null);
        var showSelf = isEmployee || a.SelfSubmittedAt != null;
        var showManager = !isEmployee || a.Status == AppraisalStatus.Completed;
        var editable = open && a.Status == AppraisalStatus.GoalSetting && !a.GoalsAgreed;
        var can = new AppraisalPermissions(
            EditGoals: editable && (isEmployee || isReviewer),
            SubmitGoals: editable && isEmployee && a.GoalsSubmittedAt == null,
            AgreeGoals: editable && isReviewer,
            ReopenGoals: open && (isReviewer || role == AppraisalRole.Hr) && a.GoalsAgreed && a.SelfSubmittedAt == null
                         && a.Status is AppraisalStatus.GoalSetting or AppraisalStatus.SelfReview,
            SelfReview: open && isEmployee && a.Status == AppraisalStatus.SelfReview,
            ManagerReview: open && isReviewer && a.Status == AppraisalStatus.ManagerReview,
            Acknowledge: isEmployee && a.Status == AppraisalStatus.Completed && a.AcknowledgedAt == null,
            RequestFeedback: open && cycle.PeerFeedback && (isReviewer || role == AppraisalRole.Hr)
                             && a.Status is AppraisalStatus.SelfReview or AppraisalStatus.ManagerReview,
            Calibrate: open && role == AppraisalRole.Hr && a.Status == AppraisalStatus.Calibration);
        var feedback = isEmployee ? [] : await FeedbackAsync(id, ct);
        return new AppraisalDto(a.Id, cycle.Id, cycle.Name, cycle.Status, cycle.PeriodStart, cycle.PeriodEnd, cycle.GoalsDue, cycle.ReviewsDue, cycle.Scale,
            cycle.SelfReview, a.EmployeeId, employee.FullName, position, a.ManagerId, a.ManagerId is { } m ? people.GetValueOrDefault(m)?.FullName : null, a.Status,
            goals.Select(g => new GoalDto(g.Id, g.Title, g.Measure, g.Weight, showSelf ? g.SelfRating : null, showSelf ? g.SelfComment : null,
                showManager ? g.ManagerRating : null, showManager ? g.ManagerComment : null)).ToList(),
            a.GoalsSubmittedAt, a.GoalsAgreedAt, showSelf ? a.SelfComment : null, a.SelfSubmittedAt, showManager ? a.ManagerComment : null, a.ManagerSubmittedAt,
            showManager ? a.OverallRating : null, showManager ? a.FinalRating : null, isEmployee ? null : a.CalibrationNote, a.AcknowledgedAt, a.EmployeeComment,
            role, can, feedback, cycle.PeerFeedback);
    }

    /// <summary>Replaces the draft goals (employee or reviewer, before they're agreed).</summary>
    public async Task<AppraisalDto> SaveGoalsAsync(Guid id, IReadOnlyList<SaveGoal> goals, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        CheckOpen(cycle);
        var role = await RoleAsync(a, ct);
        if (role != AppraisalRole.Employee && !IsReviewer(a, role)) throw new DomainException("Only the employee and their manager set goals.");
        a.CheckGoalsEditable();
        if (goals.Count > 20) throw new DomainException("Keep it to 20 goals.");
        var existing = await db.Goals.Where(g => g.AppraisalId == id).ToListAsync(ct);
        var kept = goals.Where(g => g.Id != null).Select(g => g.Id!.Value).ToHashSet();
        if (kept.Any(k => existing.All(e => e.Id != k))) throw new NotFoundException("There is no such goal.");
        db.Goals.RemoveRange(existing.Where(e => !kept.Contains(e.Id)));
        for (var i = 0; i < goals.Count; i++)
        {
            var g = goals[i];
            var details = new GoalDetails(g.Title, g.Measure, g.Weight);
            if (g.Id is { } gid) existing.Single(e => e.Id == gid).Update(details, i);
            else db.Goals.Add(new Goal(id, details, i));
        }
        await db.SaveChangesAsync(ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> SubmitGoalsAsync(Guid id, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        if (await RoleAsync(a, ct) != AppraisalRole.Employee) throw new DomainException("Only the employee sends their goals to their manager.");
        CheckOpen(cycle);
        a.SubmitGoals(await GoalsAsync(id, ct), clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await NotifyReviewerAsync(a, cycle, "goals to agree", "has proposed their goals. Please review and agree them, or change them together.", ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> AgreeGoalsAsync(Guid id, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        if (!IsReviewer(a, await RoleAsync(a, ct))) throw new DomainException("The manager agrees the goals.");
        CheckOpen(cycle);
        a.AgreeGoals(await GoalsAsync(id, ct), user.UserName, clock.GetUtcNow(), cycle);
        await db.SaveChangesAsync(ct);
        await NotifyEmployeeAsync(a, cycle, "your goals are agreed",
            a.Status == AppraisalStatus.SelfReview ? "Your goals are agreed, and your self-review is open." : "Your goals are agreed.", ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> ReopenGoalsAsync(Guid id, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        var role = await RoleAsync(a, ct);
        if (!IsReviewer(a, role) && role != AppraisalRole.Hr) throw new DomainException("The manager or HR reopens goals.");
        CheckOpen(cycle);
        a.ReopenGoals();
        foreach (var g in await db.Goals.Where(g => g.AppraisalId == id).ToListAsync(ct)) g.ClearRatings(); // draft self-ratings no longer apply
        await db.SaveChangesAsync(ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> SelfReviewAsync(Guid id, SaveReviewRequest r, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        if (await RoleAsync(a, ct) != AppraisalRole.Employee) throw new DomainException("Only the employee writes their self-review.");
        CheckOpen(cycle);
        if (a.Status != AppraisalStatus.SelfReview) throw new DomainException("Self-review isn't open.");
        var goals = await TrackedGoalsAsync(id, ct);
        foreach (var rating in r.Ratings)
            if (rating.Rating is { } v) Goal(goals, rating.GoalId).RateSelf(v, rating.Comment);
        if (r.Submit) a.SubmitSelf(goals, r.Comment, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        if (r.Submit) await NotifyReviewerAsync(a, cycle, "self-review done", "has finished their self-review. Please complete your review.", ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> ManagerReviewAsync(Guid id, SaveReviewRequest r, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        if (!IsReviewer(a, await RoleAsync(a, ct))) throw new DomainException("Only the employee's manager completes this review.");
        CheckOpen(cycle);
        if (a.Status != AppraisalStatus.ManagerReview) throw new DomainException("This review isn't waiting for the manager.");
        var goals = await TrackedGoalsAsync(id, ct);
        foreach (var rating in r.Ratings)
            if (rating.Rating is { } v) Goal(goals, rating.GoalId).RateManager(v, rating.Comment);
        if (r.Submit) a.SubmitManager(goals, r.Comment, user.UserName, clock.GetUtcNow(), cycle);
        await db.SaveChangesAsync(ct);
        if (r.Submit && a.Status == AppraisalStatus.Completed)
            await NotifyEmployeeAsync(a, cycle, "your review is ready", "Your manager has completed your review. Please read it and acknowledge it.", ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<AppraisalDto> AcknowledgeAsync(Guid id, string? comment, CancellationToken ct)
    {
        var (a, _) = await FindAppraisalAsync(id, ct);
        if (await RoleAsync(a, ct) != AppraisalRole.Employee) throw new DomainException("Only the employee acknowledges their review.");
        a.Acknowledge(comment, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await AppraisalAsync(id, ct);
    }

    // Calibration (HR)

    public async Task<AppraisalDto> SetFinalRatingAsync(Guid id, FinalRatingRequest r, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        CheckOpen(cycle);
        if (!IsHr) throw new NotFoundException("There is no such review.");
        if (a.EmployeeId == await access.MeAsync(ct)) throw new DomainException("Someone else in HR must calibrate your own rating.");
        a.SetFinalRating(r.Rating, r.Note, user.UserName);
        await db.SaveChangesAsync(ct);
        return await AppraisalAsync(id, ct);
    }

    /// <summary>Sends calibrated results to employees; reviews finished after this go straight to them.</summary>
    public async Task<CycleActionResult> ReleaseResultsAsync(Guid id, CancellationToken ct)
    {
        var cycle = await FindCycleAsync(id, ct);
        cycle.ReleaseResults(clock.GetUtcNow());
        var held = await db.Appraisals.Where(a => a.CycleId == id && a.Status == AppraisalStatus.Calibration).ToListAsync(ct);
        foreach (var a in held) a.Release();
        await db.SaveChangesAsync(ct);
        foreach (var a in held)
            await NotifyEmployeeAsync(a, cycle, "your review is ready", "Your review is complete. Please read it and acknowledge it.", ct);
        var waiting = await db.Appraisals.CountAsync(a => a.CycleId == id && a.Status != AppraisalStatus.Completed, ct);
        return new CycleActionResult(await ToDtoAsync(cycle, ct),
            waiting > 0 ? [$"{waiting} review{(waiting == 1 ? " is" : "s are")} still in progress; they go to the employee as soon as they're finished."] : []);
    }

    // 360° feedback

    /// <summary>The reviewer or HR asks colleagues for feedback. Already-asked colleagues are skipped.</summary>
    public async Task<AppraisalDto> RequestFeedbackAsync(Guid id, IReadOnlyList<Guid> reviewerIds, CancellationToken ct)
    {
        var (a, cycle) = await FindAppraisalAsync(id, ct);
        var role = await RoleAsync(a, ct);
        CheckOpen(cycle);
        if (!cycle.PeerFeedback) throw new DomainException("This cycle doesn't use 360° feedback.");
        if (!IsReviewer(a, role) && role != AppraisalRole.Hr) throw new DomainException("The reviewer or HR asks for feedback.");
        if (a.Status is not (AppraisalStatus.SelfReview or AppraisalStatus.ManagerReview)) throw new DomainException("Ask for feedback while the review is in progress.");
        var ids = reviewerIds.Distinct().ToList();
        if (ids.Contains(a.EmployeeId)) throw new DomainException("The employee's own view is their self-review.");
        if (a.ManagerId is { } m && ids.Contains(m)) throw new DomainException("The reviewer gives their view in the review itself.");
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.Status != EmployeeStatus.Exited).ToListAsync(ct);
        if (people.Count != ids.Count) throw new DomainException("Feedback can only come from current employees.");
        if (people.FirstOrDefault(e => e.WorkEmail == null) is { } noEmail) throw new DomainException($"{noEmail.FullName} has no work email, so can't sign in to give feedback.");
        var asked = await db.PeerFeedback.Where(f => f.AppraisalId == id).Select(f => f.ReviewerId).ToListAsync(ct);
        var fresh = people.Where(p => !asked.Contains(p.Id)).ToList();
        if (asked.Count + fresh.Count > 10) throw new DomainException("Ask at most 10 people.");
        db.PeerFeedback.AddRange(fresh.Select(p => new PeerFeedback(id, p.Id, user.UserName, clock.GetUtcNow())));
        await db.SaveChangesAsync(ct);
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == a.EmployeeId, ct);
        var due = cycle.ReviewsDue is { } d ? $" by {d:d MMMM yyyy}" : "";
        foreach (var p in fresh)
            await SendAsync(p.WorkEmail!, $"{cycle.Name}: feedback on {employee.FullName}",
                $"Dear {p.FirstName},\n\nYou've been asked for feedback on {employee.FullName} for {cycle.Name}{due}. It goes to their reviewer and HR only.\n\nOpen TyHR and go to My reviews.", ct);
        return await AppraisalAsync(id, ct);
    }

    public async Task<FeedbackRequestDto> FeedbackRequestAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new NotFoundException("There is no such feedback request.");
        return (await FeedbackRequestsAsync(me, id, ct)).SingleOrDefault() ?? throw new NotFoundException("There is no such feedback request.");
    }

    public async Task<FeedbackRequestDto> SubmitFeedbackAsync(Guid id, SubmitFeedbackRequest r, CancellationToken ct)
    {
        var f = await OwnFeedbackAsync(id, ct);
        f.Submit(r.Rating, r.Strengths, r.Improvements, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await FeedbackRequestAsync(id, ct);
    }

    public async Task<FeedbackRequestDto> DeclineFeedbackAsync(Guid id, CancellationToken ct)
    {
        var f = await OwnFeedbackAsync(id, ct);
        f.Decline(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await FeedbackRequestAsync(id, ct);
    }

    private async Task<PeerFeedback> OwnFeedbackAsync(Guid id, CancellationToken ct)
    {
        var dto = await FeedbackRequestAsync(id, ct);
        if (!dto.Open) throw new DomainException("This review has moved on, so feedback is closed.");
        return await db.PeerFeedback.SingleAsync(f => f.Id == id, ct);
    }

    /// <summary>Feedback requests addressed to the employee: open ones, and the last 90 days' answered ones.</summary>
    private async Task<List<FeedbackRequestDto>> FeedbackRequestsAsync(Guid me, Guid? id, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-90);
        var rows = await db.PeerFeedback.AsNoTracking().Where(f => f.ReviewerId == me && (id == null || f.Id == id))
            .Join(db.Appraisals.AsNoTracking(), f => f.AppraisalId, a => a.Id, (f, a) => new { f, a })
            .Join(db.ReviewCycles.AsNoTracking(), x => x.a.CycleId, c => c.Id, (x, c) => new { x.f, x.a, c })
            .Where(x => id != null || x.f.Status == FeedbackStatus.Requested || x.f.SubmittedAt >= since)
            .ToListAsync(ct);
        var employeeIds = rows.Select(x => x.a.EmployeeId).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.PositionId }).ToDictionaryAsync(e => e.Id, ct);
        var positions = await db.Positions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var appraisalIds = rows.Select(x => x.a.Id).ToList();
        var goals = (await db.Goals.AsNoTracking().Where(g => appraisalIds.Contains(g.AppraisalId)).OrderBy(g => g.Order).ToListAsync(ct)).ToLookup(g => g.AppraisalId);
        return rows.Select(x =>
        {
            var e = people[x.a.EmployeeId];
            var open = x.c.Status != CycleStatus.Closed && x.a.Status is AppraisalStatus.SelfReview or AppraisalStatus.ManagerReview;
            return new FeedbackRequestDto(x.f.Id, x.a.Id, e.Name, e.PositionId is { } p ? positions.GetValueOrDefault(p) : null, x.c.Name,
                goals[x.a.Id].Select(g => g.Title).ToList(), x.c.Scale, x.f.Status, x.f.Rating, x.f.Strengths, x.f.Improvements, open, x.c.ReviewsDue);
        }).OrderByDescending(f => f.Open).ThenBy(f => f.EmployeeName).ToList();
    }

    private async Task<IReadOnlyList<FeedbackDto>> FeedbackAsync(Guid appraisalId, CancellationToken ct)
    {
        var list = await db.PeerFeedback.AsNoTracking().Where(f => f.AppraisalId == appraisalId).OrderBy(f => f.RequestedAt).ToListAsync(ct);
        var ids = list.Select(f => f.ReviewerId).ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        return list.Select(f => new FeedbackDto(f.Id, f.ReviewerId, names.GetValueOrDefault(f.ReviewerId, "?"), f.Status, f.Rating, f.Strengths, f.Improvements,
            f.RequestedAt, f.SubmittedAt)).ToList();
    }

    // Helpers

    private async Task<(Appraisal, ReviewCycle)> FindAppraisalAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var a = await db.Appraisals.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such review.");
        await RoleAsync(a, ct); // throws if the user may not see it
        return (a, await db.ReviewCycles.SingleAsync(c => c.Id == a.CycleId, ct));
    }

    /// <summary>The user's part in the appraisal; not found if they have none.</summary>
    private async Task<AppraisalRole> RoleAsync(Appraisal a, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        if (me != null && a.EmployeeId == me) return AppraisalRole.Employee;
        if (me != null && a.ManagerId == me) return AppraisalRole.Manager;
        if (IsHr) return AppraisalRole.Hr;
        throw new NotFoundException("There is no such review.");
    }

    private static bool IsReviewer(Appraisal a, AppraisalRole role) => role == AppraisalRole.Manager || (role == AppraisalRole.Hr && a.ManagerId == null);

    private static void CheckOpen(ReviewCycle cycle)
    {
        if (cycle.Status == CycleStatus.Closed) throw new DomainException("This review cycle is closed.");
    }

    private Task<List<Goal>> GoalsAsync(Guid id, CancellationToken ct) => db.Goals.AsNoTracking().Where(g => g.AppraisalId == id).OrderBy(g => g.Order).ToListAsync(ct);
    private Task<List<Goal>> TrackedGoalsAsync(Guid id, CancellationToken ct) => db.Goals.Where(g => g.AppraisalId == id).OrderBy(g => g.Order).ToListAsync(ct);

    private static Goal Goal(List<Goal> goals, Guid id) => goals.SingleOrDefault(g => g.Id == id) ?? throw new NotFoundException("There is no such goal.");

    private async Task NotifyReviewerAsync(Appraisal a, ReviewCycle cycle, string what, string text, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == a.EmployeeId, ct);
        var to = a.ManagerId is { } m
            ? await db.Employees.AsNoTracking().Where(e => e.Id == m).Select(e => e.WorkEmail).SingleOrDefaultAsync(ct)
            : await db.CompanyProfiles.AsNoTracking().Select(p => p.Email).SingleOrDefaultAsync(ct);
        if (to != null) await SendAsync(to, $"{cycle.Name}: {employee.FullName}'s {what}", $"{employee.FullName} {text}\n\nOpen TyHR and go to My reviews.", ct);
    }

    private async Task NotifyEmployeeAsync(Appraisal a, ReviewCycle cycle, string what, string text, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == a.EmployeeId, ct);
        if (e.WorkEmail != null) await SendAsync(e.WorkEmail, $"{cycle.Name}: {what}", $"Dear {e.FirstName},\n\n{text}\n\nOpen TyHR and go to My reviews.", ct);
    }

    private async Task SendAsync(string to, string subject, string text, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(new EmailMessage([to], subject, string.Join("", text.Split('\n').Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>")), text), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not send \"{Subject}\".", subject);
        }
    }
}
