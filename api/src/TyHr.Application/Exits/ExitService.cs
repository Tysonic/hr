using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;
using TyHr.Domain.Leave;

namespace TyHr.Application.Exits;

/// <summary>
/// Exits: resignations the employee hands in and HR accepts, or exits HR records; the notice period; the clearance
/// checklist; the confidential exit interview; and completing the exit, which marks the employee as exited.
/// </summary>
public class ExitService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email, TimeProvider clock,
    ILogger<ExitService> logger)
{
    private bool IsHr => user.Can(Permissions.ExitsManage);
    private bool IsFinance => user.Can(Permissions.PayrollPrepare);

    // HR

    public async Task<IReadOnlyList<ExitListItem>> ListAsync(bool closed, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var since = clock.GetUtcNow().AddDays(-365);
        var exits = await db.EmployeeExits.AsNoTracking()
            .Where(x => closed ? (x.CompletedAt >= since || x.WithdrawnAt >= since) : x.Status == ExitStatus.Requested || x.Status == ExitStatus.InProgress)
            .ToListAsync(ct);
        var ids = exits.Select(x => x.Id).ToList();
        var employeeIds = exits.Select(x => x.EmployeeId).ToList();
        var people = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.EmployeeNumber, e.DepartmentId }).ToDictionaryAsync(e => e.Id, ct);
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var items = (await db.ClearanceItems.AsNoTracking().Where(i => ids.Contains(i.ExitId)).Select(i => new { i.ExitId, i.CompletedAt }).ToListAsync(ct))
            .ToLookup(i => i.ExitId);
        var interviewed = (await db.ExitInterviews.AsNoTracking().Where(i => ids.Contains(i.ExitId) && i.SubmittedAt != null).Select(i => i.ExitId).ToListAsync(ct)).ToHashSet();
        return exits.Select(x =>
        {
            var e = people[x.EmployeeId];
            return new ExitListItem(x.Id, x.EmployeeId, e.Name, e.EmployeeNumber, e.DepartmentId is { } d ? depts.GetValueOrDefault(d) : null, x.Type, x.Status,
                x.NoticeGivenOn, x.LastWorkingDay, items[x.Id].Count(i => i.CompletedAt != null), items[x.Id].Count(), interviewed.Contains(x.Id));
        }).OrderBy(x => x.Status).ThenBy(x => x.LastWorkingDay).ToList();
    }

    public async Task<ExitDto> GetAsync(Guid id, CancellationToken ct) => await ToDtoAsync(await FindAsync(id, ct), ct);

    /// <summary>An employee's exits, newest first, for their record.</summary>
    public async Task<IReadOnlyList<ExitDto>> ForEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var exits = await db.EmployeeExits.AsNoTracking().Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.RequestedAt).ToListAsync(ct);
        var result = new List<ExitDto>();
        foreach (var x in exits) result.Add(await ToDtoAsync(x, ct));
        return result;
    }

    public async Task<NoticeDto> NoticeAsync(Guid employeeId, DateOnly givenOn, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("There is no such employee.");
        return Notice(await db.CompanyCountryAsync(tenantContext, ct), employee.HireDate, givenOn);
    }

    public async Task<ExitActionResult> CreateAsync(CreateExitRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await CurrentEmployeeAsync(r.EmployeeId, ct);
        if (employee.Id == await access.MeAsync(ct)) throw new DomainException("Someone else in HR must handle your own exit.");
        if (r.NoticeGivenOn < employee.HireDate) throw new DomainException("Notice can't be given before they started.");
        await CheckNoOpenExitAsync(employee, ct);
        var exit = EmployeeExit.Record(employee.Id, r.Type, r.NoticeGivenOn, r.LastWorkingDay, r.PayInLieu, r.Reason, user.UserName, clock.GetUtcNow());
        db.EmployeeExits.Add(exit);
        var warnings = await StartClearanceAsync(exit, employee, r.ItContactId, ct);
        await db.SaveChangesAsync(ct);
        await NotifyClearanceAsync(exit, employee, ct);
        return new ExitActionResult(await ToDtoAsync(exit, ct), warnings);
    }

    public async Task<ExitActionResult> AcceptAsync(Guid id, AcceptResignationRequest r, CancellationToken ct)
    {
        var exit = await FindAsync(id, ct);
        var employee = await CurrentEmployeeAsync(exit.EmployeeId, ct);
        if (employee.Id == await access.MeAsync(ct)) throw new DomainException("Someone else in HR must accept your own resignation.");
        exit.Accept(r.LastWorkingDay, r.PayInLieu, user.UserName, clock.GetUtcNow());
        var warnings = await StartClearanceAsync(exit, employee, r.ItContactId, ct);
        await db.SaveChangesAsync(ct);
        if (employee.WorkEmail != null)
            await SendAsync(employee.WorkEmail, "Your resignation has been accepted",
                $"Dear {employee.FirstName},\n\nYour resignation has been accepted. Your last working day is {exit.LastWorkingDay:dddd d MMMM yyyy}.\n\n"
                + "Your exit checklist and a short exit interview are in TyHR under My profile. Thank you for your work with us.", ct);
        await NotifyClearanceAsync(exit, employee, ct);
        return new ExitActionResult(await ToDtoAsync(exit, ct), warnings);
    }

    public async Task<ExitDto> UpdateAsync(Guid id, UpdateExitRequest r, CancellationToken ct)
    {
        var exit = await FindAsync(id, ct);
        await CheckNotMineAsync(exit, ct);
        exit.Update(r.Type, r.NoticeGivenOn, r.LastWorkingDay, r.PayInLieu, r.Reason);
        foreach (var item in await db.ClearanceItems.Where(i => i.ExitId == id && i.CompletedAt == null).ToListAsync(ct)) item.Reschedule(exit.LastWorkingDay);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(exit, ct);
    }

    /// <summary>Withdraws an open exit: the employee their own unaccepted resignation, or HR any open exit.</summary>
    public async Task<ExitDto> WithdrawAsync(Guid id, string? note, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var exit = await db.EmployeeExits.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such exit.");
        var mine = exit.EmployeeId == await access.MeAsync(ct);
        if (mine && exit.Status != ExitStatus.Requested) throw new DomainException("HR has accepted your resignation; talk to them about withdrawing it.");
        if (!mine && !IsHr) throw new NotFoundException("There is no such exit.");
        exit.Withdraw(note, user.UserName, clock.GetUtcNow());
        db.ClearanceItems.RemoveRange(await db.ClearanceItems.Where(i => i.ExitId == id && i.CompletedAt == null).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return mine ? await ToDtoAsync(exit, ct, asEmployee: true) : await ToDtoAsync(exit, ct);
    }

    /// <summary>Marks the employee as exited once their last day has passed and clearance is done; ends contracts and later leave.</summary>
    public async Task<ExitActionResult> CompleteAsync(Guid id, CompleteExitRequest r, CancellationToken ct)
    {
        var exit = await FindAsync(id, ct);
        await CheckNotMineAsync(exit, ct);
        var today = await TodayAsync(ct);
        if (exit.Status == ExitStatus.InProgress && today < exit.LastWorkingDay)
            throw new DomainException($"Complete the exit on or after their last working day ({exit.LastWorkingDay:d MMM yyyy}).");
        var open = await db.ClearanceItems.CountAsync(i => i.ExitId == id && i.CompletedAt == null, ct);
        if (open > 0) throw new DomainException($"{open} clearance item{(open == 1 ? " is" : "s are")} still open.");
        var employee = await db.Employees.SingleAsync(e => e.Id == exit.EmployeeId, ct);
        var warnings = new List<string>();
        await db.InTransactionAsync(async () =>
        {
            exit.Complete(r.EligibleForRehire, r.Note, today, user.UserName, clock.GetUtcNow());
            employee.SetStatus(EmployeeStatus.Exited);
            foreach (var c in await db.EmploymentContracts.Where(c => c.EmployeeId == employee.Id && c.StartDate <= exit.LastWorkingDay
                         && (c.EndDate == null || c.EndDate > exit.LastWorkingDay)).ToListAsync(ct))
                c.EndOn(exit.LastWorkingDay);
            var later = await db.LeaveRequests.Where(l => l.EmployeeId == employee.Id && l.EndDate > exit.LastWorkingDay
                && (l.Status == LeaveStatus.Pending || l.Status == LeaveStatus.Approved)).ToListAsync(ct);
            foreach (var l in later.Where(l => l.StartDate > exit.LastWorkingDay)) l.Cancel(user.UserName, clock.GetUtcNow(), "Left the company.");
            if (later.Any(l => l.StartDate <= exit.LastWorkingDay))
                warnings.Add("Leave running past the last working day wasn't changed; check it before the final pay.");
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        var reports = await db.Employees.Where(e => e.ManagerId == employee.Id && e.Status != EmployeeStatus.Exited).Select(e => e.FirstName + " " + e.LastName).ToListAsync(ct);
        if (reports.Count > 0) warnings.Add($"{string.Join(", ", reports)} still report{(reports.Count == 1 ? "s" : "")} to {employee.FullName}; give them a new manager.");
        return new ExitActionResult(await ToDtoAsync(exit, ct), warnings);
    }

    // Clearance

    public async Task<ExitDto> AddItemAsync(Guid exitId, AddClearanceRequest r, CancellationToken ct)
    {
        var exit = await FindAsync(exitId, ct);
        if (exit.Status != ExitStatus.InProgress) throw new DomainException("Add clearance items while the exit is in progress.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == exit.EmployeeId, ct);
        var assignee = r.Step.Owner switch
        {
            ClearanceOwner.Employee => employee.Id,
            ClearanceOwner.Manager => r.AssigneeId ?? employee.ManagerId ?? throw new DomainException($"{employee.FullName} has no line manager; choose who does it."),
            ClearanceOwner.It => r.AssigneeId ?? throw new DomainException("Choose the IT contact who does it."),
            _ => (Guid?)null,
        };
        if (assignee is { } a && a != employee.Id) await CheckAssigneeAsync(a, ct);
        var item = new ClearanceItem(exitId, employee.Id, r.Step, exit.LastWorkingDay, assignee);
        db.ClearanceItems.Add(item);
        await db.SaveChangesAsync(ct);
        await NotifyClearanceAsync(exit, employee, ct, [item]);
        return await ToDtoAsync(exit, ct);
    }

    public async Task DeleteItemAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!IsHr) throw new NotFoundException("There is no such clearance item.");
        var item = await db.ClearanceItems.SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("There is no such clearance item.");
        await CheckOpenAsync(item.ExitId, ct);
        db.ClearanceItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ClearanceItemDto> ClearAsync(Guid id, string? note, CancellationToken ct)
    {
        var item = await OwnItemAsync(id, ct);
        item.Complete(user.UserName, note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return (await ItemDtosAsync([item], ct))[0];
    }

    public async Task<ClearanceItemDto> ReopenItemAsync(Guid id, CancellationToken ct)
    {
        var item = await OwnItemAsync(id, ct);
        item.Reopen();
        await db.SaveChangesAsync(ct);
        return (await ItemDtosAsync([item], ct))[0];
    }

    /// <summary>Clearance items for the signed-in user: open ones (theirs, plus finance's for payroll) and those cleared in the last 30 days.</summary>
    public async Task<IReadOnlyList<ClearanceItemDto>> MyItemsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        if (me == null && !IsFinance) return [];
        var since = clock.GetUtcNow().AddDays(-30);
        var finance = IsFinance && !IsHr;
        var openExits = db.EmployeeExits.Where(x => x.Status == ExitStatus.InProgress).Select(x => x.Id);
        var items = await db.ClearanceItems.AsNoTracking()
            .Where(i => (i.AssigneeId == me || (finance && i.Owner == ClearanceOwner.Finance)) && i.EmployeeId != me
                        && openExits.Contains(i.ExitId) && (i.CompletedAt == null || i.CompletedAt >= since))
            .OrderBy(i => i.CompletedAt != null).ThenBy(i => i.DueOn).ToListAsync(ct);
        return await ItemDtosAsync(items, ct);
    }

    // The leaver

    public async Task<MyExitDto?> MineAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (await access.MeAsync(ct) is not { } me) return null;
        var exit = await db.EmployeeExits.AsNoTracking().Where(x => x.EmployeeId == me && (x.Status == ExitStatus.Requested || x.Status == ExitStatus.InProgress))
            .SingleOrDefaultAsync(ct);
        return exit == null ? null : await ToMyDtoAsync(exit, ct);
    }

    public async Task<MyExitDto> ResignAsync(ResignRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new DomainException("Your sign-in isn't linked to an employee record.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == me, ct);
        await CheckNoOpenExitAsync(employee, ct);
        var today = await TodayAsync(ct);
        if (r.LastWorkingDay < today) throw new DomainException("Your last working day can't be in the past.");
        var exit = EmployeeExit.Resign(me, today, r.LastWorkingDay, r.Reason, user.UserName, clock.GetUtcNow());
        db.EmployeeExits.Add(exit);
        await db.SaveChangesAsync(ct);
        if (employee.ManagerId is { } m && await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == m, ct) is { WorkEmail: { } to } manager)
            await SendAsync(to, $"{employee.FullName} has resigned",
                $"Dear {manager.FirstName},\n\n{employee.FullName} has handed in their resignation in TyHR, proposing {exit.LastWorkingDay:dddd d MMMM yyyy} as their last working day. HR will confirm it.", ct);
        return await ToMyDtoAsync(exit, ct);
    }

    /// <summary>The leaver fills in their exit interview while their exit is open. HR can't change it afterwards.</summary>
    public async Task<MyExitDto> MyInterviewAsync(InterviewAnswers a, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new NotFoundException("You have no exit in progress.");
        var exit = await db.EmployeeExits.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == me && x.Status == ExitStatus.InProgress, ct)
                   ?? throw new DomainException("The exit interview opens once HR has accepted your resignation.");
        await SaveInterviewAsync(exit, a, true, ct);
        return await ToMyDtoAsync(exit, ct);
    }

    /// <summary>HR records an exit interview held in person.</summary>
    public async Task<ExitDto> InterviewAsync(Guid id, InterviewAnswers a, CancellationToken ct)
    {
        var exit = await FindAsync(id, ct);
        await CheckNotMineAsync(exit, ct);
        if (exit.Status is ExitStatus.Withdrawn) throw new DomainException("This exit was withdrawn.");
        await SaveInterviewAsync(exit, a, false, ct);
        return await ToDtoAsync(exit, ct);
    }

    private async Task SaveInterviewAsync(EmployeeExit exit, InterviewAnswers a, bool byEmployee, CancellationToken ct)
    {
        var interview = await db.ExitInterviews.SingleOrDefaultAsync(i => i.ExitId == exit.Id, ct);
        if (interview == null) db.ExitInterviews.Add(interview = new ExitInterview(exit.Id, exit.EmployeeId));
        interview.Save(a, byEmployee, user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resignations waiting for HR, for the approvals inbox.</summary>
    public async Task<int> WaitingCountAsync(CancellationToken ct) =>
        IsHr ? await db.EmployeeExits.CountAsync(x => x.Status == ExitStatus.Requested, ct) : 0;

    // Helpers

    private static NoticeDto Notice(CountryPack country, DateOnly hired, DateOnly givenOn)
    {
        var rule = country.Exits.Notice(hired, givenOn);
        return new NoticeDto(rule.Days, rule.Basis, rule.Source, ExitRules.MonthsOfService(hired, givenOn), givenOn.AddDays(rule.Days));
    }

    private async Task<List<string>> StartClearanceAsync(EmployeeExit exit, Employee employee, Guid? itContactId, CancellationToken ct)
    {
        var warnings = new List<string>();
        if (itContactId is { } it) await CheckAssigneeAsync(it, ct);
        foreach (var step in ClearanceStep.Standard)
        {
            Guid? assignee = step.Owner switch
            {
                ClearanceOwner.Employee => employee.WorkEmail != null ? employee.Id : null,
                ClearanceOwner.Manager => employee.ManagerId,
                ClearanceOwner.It => itContactId,
                _ => null,
            };
            db.ClearanceItems.Add(new ClearanceItem(exit.Id, employee.Id, step, exit.LastWorkingDay, assignee));
        }
        if (employee.ManagerId == null) warnings.Add($"{employee.FullName} has no line manager, so HR has the manager's clearance items.");
        if (itContactId == null) warnings.Add("No IT contact was chosen, so HR has the IT items.");
        if (employee.WorkEmail == null) warnings.Add($"{employee.FullName} has no work email, so can't sign in; HR has their items and records the exit interview.");
        return warnings;
    }

    private async Task NotifyClearanceAsync(EmployeeExit exit, Employee employee, CancellationToken ct, IReadOnlyList<ClearanceItem>? only = null)
    {
        var items = only ?? await db.ClearanceItems.AsNoTracking().Where(i => i.ExitId == exit.Id).ToListAsync(ct);
        foreach (var group in items.Where(i => i.AssigneeId != null && i.AssigneeId != employee.Id).GroupBy(i => i.AssigneeId!.Value))
        {
            var person = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == group.Key, ct);
            if (person?.WorkEmail == null) continue;
            var list = string.Join("\n", group.OrderBy(i => i.DueOn).Select(i => $"- {i.Title} (by {i.DueOn:d MMM})"));
            await SendAsync(person.WorkEmail, $"Exit clearance for {employee.FullName}",
                $"Dear {person.FirstName},\n\n{employee.FullName}'s last working day is {exit.LastWorkingDay:dddd d MMMM yyyy}. Please clear these, and tick them off in TyHR under My tasks:\n\n{list}", ct);
        }
    }

    private async Task<EmployeeExit> FindAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!IsHr) throw new NotFoundException("There is no such exit.");
        return await db.EmployeeExits.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such exit.");
    }

    private async Task<Employee> CurrentEmployeeAsync(Guid id, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("There is no such employee.");
        if (employee.Status == EmployeeStatus.Exited) throw new DomainException($"{employee.FullName} has already left.");
        return employee;
    }

    private async Task CheckNoOpenExitAsync(Employee employee, CancellationToken ct)
    {
        if (await db.EmployeeExits.AnyAsync(x => x.EmployeeId == employee.Id && (x.Status == ExitStatus.Requested || x.Status == ExitStatus.InProgress), ct))
            throw new DomainException($"{employee.FullName} already has an exit in progress.");
    }

    private async Task CheckNotMineAsync(EmployeeExit exit, CancellationToken ct)
    {
        if (exit.EmployeeId == await access.MeAsync(ct)) throw new DomainException("Someone else in HR must handle your own exit.");
    }

    private async Task CheckOpenAsync(Guid exitId, CancellationToken ct)
    {
        if (!await db.EmployeeExits.AnyAsync(x => x.Id == exitId && x.Status == ExitStatus.InProgress, ct)) throw new DomainException("This exit is closed.");
    }

    private async Task CheckAssigneeAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id && e.Status != EmployeeStatus.Exited && e.WorkEmail != null, ct))
            throw new DomainException("Choose a current employee with a work email.");
    }

    /// <summary>An item the user may tick off: HR any; payroll finance items; others only their own. Never one on their own exit.</summary>
    private async Task<ClearanceItem> OwnItemAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var item = await db.ClearanceItems.SingleOrDefaultAsync(i => i.Id == id, ct);
        var me = await access.MeAsync(ct);
        var allowed = item != null && (IsHr || (IsFinance && item.Owner == ClearanceOwner.Finance) || (me != null && item.AssigneeId == me));
        if (!allowed) throw new NotFoundException("There is no such clearance item.");
        if (item!.EmployeeId == me && item.Owner != ClearanceOwner.Employee) throw new DomainException("Someone else clears this item on your own exit.");
        await CheckOpenAsync(item.ExitId, ct);
        return item;
    }

    private async Task<IReadOnlyList<ClearanceItemDto>> ItemDtosAsync(IReadOnlyList<ClearanceItem> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var today = await TodayAsync(ct);
        var me = await access.MeAsync(ct);
        var exitIds = items.Select(i => i.ExitId).Distinct().ToList();
        var lastDays = await db.EmployeeExits.AsNoTracking().Where(x => exitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.LastWorkingDay, ct);
        var ids = items.Select(i => i.EmployeeId).Concat(items.Where(i => i.AssigneeId != null).Select(i => i.AssigneeId!.Value)).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        return items.Select(i => new ClearanceItemDto(i.Id, i.ExitId, i.EmployeeId, names.GetValueOrDefault(i.EmployeeId, "?"), lastDays[i.ExitId], i.Title,
            i.Description, i.Owner, i.AssigneeId, i.AssigneeId is { } a ? names.GetValueOrDefault(a) : null, i.DueOn, i.IsDone, !i.IsDone && i.DueOn < today,
            i.CompletedAt, i.CompletedBy, i.Note,
            (i.EmployeeId != me || i.Owner == ClearanceOwner.Employee)
            && (IsHr || (IsFinance && i.Owner == ClearanceOwner.Finance) || (me != null && i.AssigneeId == me)))).ToList();
    }

    private static ExitInterviewDto? InterviewDto(ExitInterview? i) => i?.SubmittedAt == null ? null : new ExitInterviewDto(
        new InterviewAnswers(i.MainReason, i.RoleRating, i.ManagerRating, i.PayRating, i.GrowthRating, i.CultureRating, i.WouldRecommend, i.WouldReturn,
            i.WentWell, i.CouldImprove, i.Comments), i.ByEmployee, i.SubmittedBy, i.SubmittedAt);

    private async Task<ExitDto> ToDtoAsync(EmployeeExit x, CancellationToken ct, bool asEmployee = false)
    {
        var e = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == x.EmployeeId, ct);
        var manager = e.ManagerId is { } m ? await db.Employees.AsNoTracking().Where(p => p.Id == m).Select(p => p.FirstName + " " + p.LastName).SingleOrDefaultAsync(ct) : null;
        var position = e.PositionId is { } p ? await db.Positions.AsNoTracking().Where(q => q.Id == p).Select(q => q.Title).SingleOrDefaultAsync(ct) : null;
        var dept = e.DepartmentId is { } d ? await db.Departments.AsNoTracking().Where(q => q.Id == d).Select(q => q.Name).SingleOrDefaultAsync(ct) : null;
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var notice = Notice(country, e.HireDate, x.NoticeGivenOn);
        var items = await ItemDtosAsync(await db.ClearanceItems.AsNoTracking().Where(i => i.ExitId == x.Id).OrderBy(i => i.DueOn).ThenBy(i => i.Title).ToListAsync(ct), ct);
        var interview = asEmployee ? null : InterviewDto(await db.ExitInterviews.AsNoTracking().SingleOrDefaultAsync(i => i.ExitId == x.Id, ct));
        var given = x.LastWorkingDay.DayNumber - x.NoticeGivenOn.DayNumber;
        var noNotice = x.Type is ExitType.Dismissal or ExitType.Death or ExitType.EndOfContract;
        var shortNotice = !noNotice && !x.PayInLieu && given < notice.Days;
        var warnings = new List<string>();
        if (x.IsOpen && shortNotice)
            warnings.Add($"{given} days' notice is less than the statutory {notice.Days} ({notice.Basis}). Pay the difference in lieu or agree it in writing.");
        if (x.IsOpen && interview == null) warnings.Add("No exit interview yet.");
        var today = await TodayAsync(ct);
        var canComplete = x.Status == ExitStatus.InProgress && today >= x.LastWorkingDay && items.All(i => i.IsDone);
        return new ExitDto(x.Id, e.Id, e.FullName, e.EmployeeNumber, position, dept, manager, e.HireDate, x.Type, x.Reason, x.Status, x.NoticeGivenOn,
            x.LastWorkingDay, x.PayInLieu, notice, given, shortNotice, x.RequestedBy, x.RequestedAt, x.AcceptedBy, x.AcceptedAt, x.EligibleForRehire,
            x.CompletionNote, x.CompletedBy, x.CompletedAt, x.WithdrawnBy, x.WithdrawnAt, x.WithdrawalNote, items, interview, warnings, canComplete);
    }

    private async Task<MyExitDto> ToMyDtoAsync(EmployeeExit x, CancellationToken ct)
    {
        var items = await ItemDtosAsync(await db.ClearanceItems.AsNoTracking().Where(i => i.ExitId == x.Id && i.Owner == ClearanceOwner.Employee)
            .OrderBy(i => i.DueOn).ToListAsync(ct), ct);
        var interview = await db.ExitInterviews.AsNoTracking().SingleOrDefaultAsync(i => i.ExitId == x.Id, ct);
        return new MyExitDto(x.Id, x.Type, x.Status, x.NoticeGivenOn, x.LastWorkingDay, x.Type == ExitType.Resignation ? x.Reason : null, items,
            interview?.ByEmployee == true ? InterviewDto(interview) : null, x.Status == ExitStatus.Requested,
            x.Status == ExitStatus.InProgress && interview?.SubmittedAt == null);
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
