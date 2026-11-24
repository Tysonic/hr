using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Performance;

namespace TyHr.Application.Performance;

public record PlanCheckInDto(Guid Id, DateOnly On, PlanProgress Progress, string Note, string By, DateTimeOffset At);

public record PlanDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, Guid? OwnerId, string? OwnerName, Guid? AppraisalId,
    string Reason, IReadOnlyList<PlanObjective> Objectives, string? Support, DateOnly StartDate, DateOnly EndDate, PlanStatus Status, string CreatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset? AcknowledgedAt, string? EmployeeComment, string? Outcome, DateTimeOffset? ClosedAt, string? ClosedBy,
    IReadOnlyList<PlanCheckInDto> CheckIns, bool IsMine, bool CanManage, int DaysLeft);

public record PlanListItem(Guid Id, Guid EmployeeId, string EmployeeName, string? OwnerName, DateOnly StartDate, DateOnly EndDate, PlanStatus Status,
    PlanProgress? LastProgress, bool Acknowledged, bool IsMine);

public record CreatePlanRequest(Guid EmployeeId, Guid? OwnerId, Guid? AppraisalId, PlanDetails Details);
public record AddCheckInRequest(DateOnly On, PlanProgress Progress, string Note);
public record ClosePlanRequest(PlanStatus Outcome, string Note);
public record PlanAcknowledgeRequest(string? Comment);

/// <summary>
/// Performance improvement plans. HR manages any; a line manager manages plans for people in their reporting tree;
/// the employee reads and acknowledges their own. Nobody manages their own plan.
/// </summary>
public class ImprovementPlanService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email,
    TimeProvider clock, ILogger<ImprovementPlanService> logger)
{
    private bool IsHr => user.Can(Permissions.PerformanceManage);

    public async Task<IReadOnlyList<PlanListItem>> ListAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        var team = IsHr ? null : await access.TeamAsync(ct);
        var plans = await db.ImprovementPlans.AsNoTracking().OrderBy(p => p.Status).ThenByDescending(p => p.StartDate).ToListAsync(ct);
        plans = plans.Where(p => p.EmployeeId == me || (p.EmployeeId != me && (team == null || team.Contains(p.EmployeeId) || p.OwnerId == me))).ToList();
        var ids = plans.SelectMany(p => new[] { p.EmployeeId, p.OwnerId ?? Guid.Empty }).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        var planIds = plans.Select(p => p.Id).ToList();
        var last = (await db.PlanCheckIns.AsNoTracking().Where(c => planIds.Contains(c.PlanId)).ToListAsync(ct))
            .GroupBy(c => c.PlanId).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.On).ThenByDescending(c => c.At).First().Progress);
        return plans.Select(p => new PlanListItem(p.Id, p.EmployeeId, names.GetValueOrDefault(p.EmployeeId, "?"), p.OwnerId is { } o ? names.GetValueOrDefault(o) : null,
            p.StartDate, p.EndDate, p.Status, last.TryGetValue(p.Id, out var pr) ? pr : null, p.AcknowledgedAt != null, p.EmployeeId == me)).ToList();
    }

    public async Task<PlanDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await VisibleAsync(id, ct), ct);

    public async Task<PlanDto> CreateAsync(CreatePlanRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        await CheckManagesAsync(r.EmployeeId, ct);
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == r.EmployeeId && e.Status != EmployeeStatus.Exited, ct)
                       ?? throw new NotFoundException("There is no such employee.");
        if (await db.ImprovementPlans.AnyAsync(p => p.EmployeeId == r.EmployeeId && p.Status == PlanStatus.Active, ct))
            throw new DomainException($"{employee.FullName} already has an active improvement plan.");
        if (r.AppraisalId is { } a && !await db.Appraisals.AnyAsync(x => x.Id == a && x.EmployeeId == r.EmployeeId, ct))
            throw new DomainException("That review isn't this employee's.");
        var owner = r.OwnerId ?? (IsHr ? employee.ManagerId : await access.MeAsync(ct));
        await CheckOwnerAsync(owner, ct);
        var plan = new ImprovementPlan(r.EmployeeId, owner, r.AppraisalId, r.Details, user.UserName, clock.GetUtcNow());
        db.ImprovementPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        if (employee.WorkEmail != null)
            await SendAsync(employee.WorkEmail, "Your performance improvement plan",
                $"Dear {employee.FirstName},\n\nA performance improvement plan has been set up with you, running from {plan.StartDate:d MMMM yyyy} to {plan.EndDate:d MMMM yyyy}. "
                + "Please read it in TyHR under My reviews and acknowledge it. Your manager will go through it with you.", ct);
        return await ToDtoAsync(plan, ct);
    }

    public async Task<PlanDto> UpdateAsync(Guid id, PlanDetails d, Guid? ownerId, CancellationToken ct)
    {
        var plan = await ManagedAsync(id, ct);
        await CheckOwnerAsync(ownerId, ct);
        plan.Update(d);
        plan.ChangeOwner(ownerId);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(plan, ct);
    }

    public async Task<PlanDto> CheckInAsync(Guid id, AddCheckInRequest r, CancellationToken ct)
    {
        var plan = await ManagedAsync(id, ct);
        plan.CheckActive();
        if (r.On < plan.StartDate) throw new DomainException("A check-in can't be before the plan starts.");
        if (r.On > await TodayAsync(ct)) throw new DomainException("Record check-ins once they've happened.");
        db.PlanCheckIns.Add(new PlanCheckIn(id, r.On, r.Progress, r.Note, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(plan, ct);
    }

    public async Task<PlanDto> CloseAsync(Guid id, ClosePlanRequest r, CancellationToken ct)
    {
        var plan = await ManagedAsync(id, ct);
        plan.Close(r.Outcome, r.Note, user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(plan, ct);
    }

    public async Task<PlanDto> AcknowledgeAsync(Guid id, string? comment, CancellationToken ct)
    {
        var plan = await VisibleAsync(id, ct);
        if (plan.EmployeeId != await access.MeAsync(ct)) throw new DomainException("Only the employee acknowledges their plan.");
        var tracked = await db.ImprovementPlans.SingleAsync(p => p.Id == id, ct);
        tracked.Acknowledge(comment, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(tracked, ct);
    }

    private async Task CheckManagesAsync(Guid employeeId, CancellationToken ct)
    {
        if (employeeId == await access.MeAsync(ct)) throw new DomainException("Someone else must manage your own improvement plan.");
        if (!IsHr && !await access.InTeamAsync(employeeId, ct)) throw new NotFoundException("There is no such employee.");
    }

    private async Task CheckOwnerAsync(Guid? owner, CancellationToken ct)
    {
        if (owner is { } o && !await db.Employees.AnyAsync(e => e.Id == o && e.Status != EmployeeStatus.Exited, ct))
            throw new DomainException("The plan's owner must be a current employee.");
    }

    private async Task<ImprovementPlan> VisibleAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var plan = await db.ImprovementPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        var me = await access.MeAsync(ct);
        if (plan == null || (plan.EmployeeId != me && !IsHr && plan.OwnerId != me && !await access.InTeamAsync(plan.EmployeeId, ct)))
            throw new NotFoundException("There is no such plan.");
        return plan;
    }

    private async Task<ImprovementPlan> ManagedAsync(Guid id, CancellationToken ct)
    {
        var plan = await VisibleAsync(id, ct);
        if (plan.EmployeeId == await access.MeAsync(ct)) throw new DomainException("Someone else must manage your own improvement plan.");
        return await db.ImprovementPlans.SingleAsync(p => p.Id == id, ct);
    }

    private async Task<PlanDto> ToDtoAsync(ImprovementPlan p, CancellationToken ct)
    {
        var me = await access.MeAsync(ct);
        var names = await db.Employees.AsNoTracking().Where(e => e.Id == p.EmployeeId || e.Id == p.OwnerId)
            .ToDictionaryAsync(e => e.Id, e => new { Name = e.FirstName + " " + e.LastName, e.EmployeeNumber }, ct);
        var checkIns = await db.PlanCheckIns.AsNoTracking().Where(c => c.PlanId == p.Id).OrderByDescending(c => c.On).ThenByDescending(c => c.At)
            .Select(c => new PlanCheckInDto(c.Id, c.On, c.Progress, c.Note, c.By, c.At)).ToListAsync(ct);
        var isMine = p.EmployeeId == me;
        return new PlanDto(p.Id, p.EmployeeId, names[p.EmployeeId].Name, names[p.EmployeeId].EmployeeNumber, p.OwnerId,
            p.OwnerId is { } o ? names.GetValueOrDefault(o)?.Name : null, p.AppraisalId, p.Reason, p.Objectives, p.Support, p.StartDate, p.EndDate, p.Status,
            p.CreatedBy, p.CreatedAt, p.AcknowledgedAt, p.EmployeeComment, p.Outcome, p.ClosedAt, p.ClosedBy, checkIns, isMine,
            !isMine && p.Status == PlanStatus.Active, p.EndDate.DayNumber - (await TodayAsync(ct)).DayNumber);
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

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
