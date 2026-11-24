using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Onboarding;

namespace TyHr.Application.Onboarding;

/// <summary>
/// Onboarding checklists: HR keeps templates; starting onboarding copies one into tasks for the new starter, each owned
/// by HR, an IT contact, their line manager or the new hire, and due relative to the start date. Owners see their tasks
/// under My tasks; HR can tick off any of them.
/// </summary>
public class OnboardingService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email,
    TimeProvider clock, ILogger<OnboardingService> logger)
{
    private bool IsHr => user.Can(Permissions.EmployeesManage);

    // Templates

    public async Task<OnboardingSetupDto> SetupAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var templates = await db.OnboardingTemplates.AsNoTracking().OrderByDescending(t => t.IsDefault).ThenBy(t => t.Name).ToListAsync(ct);
        return new OnboardingSetupDto(templates.Select(ToDto).ToList(), OnboardingTemplate.Standard);
    }

    public async Task<OnboardingTemplateDto> CreateTemplateAsync(SaveOnboardingTemplateRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        await CheckNameAsync(r.Name, null, ct);
        var first = !await db.OnboardingTemplates.AnyAsync(ct);
        if (r.IsDefault) await ClearDefaultAsync(ct);
        var t = new OnboardingTemplate(r.Name, r.Items, r.IsDefault || first);
        db.OnboardingTemplates.Add(t);
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<OnboardingTemplateDto> UpdateTemplateAsync(Guid id, SaveOnboardingTemplateRequest r, CancellationToken ct)
    {
        var t = await TemplateAsync(id, ct);
        await CheckNameAsync(r.Name, id, ct);
        if (r.IsDefault && !t.IsDefault) await ClearDefaultAsync(ct);
        t.Update(r.Name, r.Items, r.IsDefault);
        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    /// <summary>Deletes a template. Tasks already made from it are copies, so they stay.</summary>
    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        db.OnboardingTemplates.Remove(await TemplateAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<OnboardingTemplate> TemplateAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.OnboardingTemplates.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("There is no such checklist.");
    }

    private async Task CheckNameAsync(string name, Guid? id, CancellationToken ct)
    {
        var n = name.Trim().ToLower();
        if (await db.OnboardingTemplates.AnyAsync(t => t.Name.ToLower() == n && t.Id != id, ct))
            throw new DomainException($"There is already a checklist called {name.Trim()}.");
    }

    private async Task ClearDefaultAsync(CancellationToken ct)
    {
        foreach (var other in await db.OnboardingTemplates.Where(t => t.IsDefault).ToListAsync(ct)) other.ClearDefault();
    }

    private static OnboardingTemplateDto ToDto(OnboardingTemplate t) => new(t.Id, t.Name, t.Items, t.IsDefault);

    // Starting onboarding

    /// <summary>
    /// Makes the employee's tasks from a template. Manager tasks go to their line manager and IT tasks to the IT
    /// contact; without one, HR does them. Each owner gets an email listing their tasks (best effort).
    /// </summary>
    public async Task<OnboardingStartResult> StartAsync(Guid employeeId, StartOnboardingRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("There is no such employee.");
        if (employee.Status == EmployeeStatus.Exited) throw new DomainException($"{employee.FullName} has left.");
        if (await db.OnboardingTasks.AnyAsync(t => t.EmployeeId == employeeId && t.CompletedAt == null, ct))
            throw new DomainException($"{employee.FullName}'s onboarding has already started. Add tasks to it instead.");

        var template = r.TemplateId is { } id ? await TemplateAsync(id, ct) : await db.OnboardingTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.IsDefault, ct);
        IReadOnlyList<ChecklistItem> items = template?.Items ?? (IReadOnlyList<ChecklistItem>)OnboardingTemplate.Standard;
        if (r.ItContactId is { } it && !await db.Employees.AnyAsync(e => e.Id == it && e.Status != EmployeeStatus.Exited && e.WorkEmail != null, ct))
            throw new DomainException("The IT contact must be a current employee with a work email.");

        var warnings = new List<string>();
        if (items.Any(i => i.Owner == TaskOwner.Manager) && employee.ManagerId == null)
            warnings.Add($"{employee.FirstName} has no line manager, so HR has the manager's tasks.");
        if (items.Any(i => i.Owner == TaskOwner.It) && r.ItContactId == null)
            warnings.Add("No IT contact was chosen, so HR has the IT tasks.");
        if (items.Any(i => i.Owner == TaskOwner.NewHire) && employee.WorkEmail == null)
            warnings.Add($"{employee.FirstName} has no work email yet, so they can't sign in to see their tasks; HR can tick them off.");

        var tasks = items.Select(i => new OnboardingTask(employeeId, i, employee.HireDate, i.Owner switch
        {
            TaskOwner.Manager => employee.ManagerId,
            TaskOwner.It => r.ItContactId,
            TaskOwner.NewHire => employee.Id,
            _ => null,
        })).ToList();
        db.OnboardingTasks.AddRange(tasks);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(employee, tasks, ct);
        return new OnboardingStartResult(tasks.Count, warnings);
    }

    public async Task<OnboardingTaskDto> AddTaskAsync(Guid employeeId, AddOnboardingTaskRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == employeeId, ct) ?? throw new NotFoundException("There is no such employee.");
        await CheckAssigneeAsync(r.AssigneeId, ct);
        var task = new OnboardingTask(employeeId, r.Item, employee.HireDate, r.AssigneeId);
        db.OnboardingTasks.Add(task);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(employee, [task], ct);
        return (await ToDtosAsync([task], ct))[0];
    }

    private async Task CheckAssigneeAsync(Guid? id, CancellationToken ct)
    {
        if (id is { } a && !await db.Employees.AnyAsync(e => e.Id == a && e.Status != EmployeeStatus.Exited, ct))
            throw new DomainException("Tasks can only be given to current employees.");
    }

    private async Task NotifyAsync(Employee employee, IReadOnlyList<OnboardingTask> tasks, CancellationToken ct)
    {
        var ids = tasks.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.WorkEmail != null)
            .Select(e => new { e.Id, e.FirstName, e.WorkEmail }).ToListAsync(ct);
        foreach (var p in people)
        {
            var mine = tasks.Where(t => t.AssigneeId == p.Id).OrderBy(t => t.DueOn).ToList();
            var forSelf = p.Id == employee.Id;
            var subject = forSelf ? "Welcome: your first tasks in TyHR" : $"Onboarding {employee.FullName}: {mine.Count} task{(mine.Count == 1 ? "" : "s")} for you";
            var intro = forSelf
                ? $"Hello {p.FirstName},\n\nWelcome aboard. Here is what we'd like you to do in your first weeks:"
                : $"Hello {p.FirstName},\n\n{employee.FullName} starts on {employee.HireDate:d MMMM yyyy}. Please take care of:";
            var lines = mine.Select(t => $"- {t.Title} (by {t.DueOn:d MMM yyyy})");
            var text = $"{intro}\n{string.Join("\n", lines)}\n\nTick them off in TyHR under My tasks.";
            var html = $"<p>{WebUtility.HtmlEncode(intro).Replace("\n", "<br>")}</p><ul>{string.Join("", mine.Select(t => $"<li>{WebUtility.HtmlEncode(t.Title)} (by {t.DueOn:d MMM yyyy})</li>"))}</ul><p>Tick them off in TyHR under My tasks.</p>";
            try
            {
                await email.SendAsync(new EmailMessage([p.WorkEmail!], subject, html, text), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "Could not email onboarding tasks.");
            }
        }
    }

    // Tasks

    /// <summary>An employee's onboarding tasks, for HR and anyone who may open the employee's record.</summary>
    public async Task<IReadOnlyList<OnboardingTaskDto>> TasksAsync(Guid employeeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!await access.CanOpenAsync(employeeId, ct)) throw new NotFoundException("There is no such employee.");
        var tasks = await db.OnboardingTasks.AsNoTracking().Where(t => t.EmployeeId == employeeId).OrderBy(t => t.DueOn).ThenBy(t => t.Title).ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    /// <summary>Tasks given to the signed-in employee: open ones, and those done in the last 30 days.</summary>
    public async Task<IReadOnlyList<OnboardingTaskDto>> MyTasksAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (await access.MeAsync(ct) is not { } me) return [];
        var since = clock.GetUtcNow().AddDays(-30);
        var tasks = await db.OnboardingTasks.AsNoTracking().Where(t => t.AssigneeId == me && (t.CompletedAt == null || t.CompletedAt >= since))
            .OrderBy(t => t.CompletedAt != null).ThenBy(t => t.DueOn).ToListAsync(ct);
        return await ToDtosAsync(tasks, ct);
    }

    /// <summary>New starters whose onboarding is open or finished in the last 30 days.</summary>
    public async Task<IReadOnlyList<OnboardingProgressDto>> ProgressAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var today = await TodayAsync(ct);
        var since = clock.GetUtcNow().AddDays(-30);
        var tasks = await db.OnboardingTasks.AsNoTracking().ToListAsync(ct);
        var active = tasks.GroupBy(t => t.EmployeeId).Where(g => g.Any(t => !t.IsDone || t.CompletedAt >= since)).ToList();
        var ids = active.Select(g => g.Key).ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
        var managerIds = employees.Values.Where(e => e.ManagerId != null).Select(e => e.ManagerId!.Value).ToList();
        var managers = await db.Employees.AsNoTracking().Where(e => managerIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        return active.Where(g => employees.ContainsKey(g.Key)).Select(g =>
        {
            var e = employees[g.Key];
            var open = g.Where(t => !t.IsDone).ToList();
            return new OnboardingProgressDto(e.Id, e.FullName, e.EmployeeNumber, e.HireDate, e.ManagerId is { } m ? managers.GetValueOrDefault(m) : null,
                g.Count(t => t.IsDone), g.Count(), open.Count(t => t.DueOn < today), open.Count > 0 ? open.Min(t => t.DueOn) : null);
        }).OrderBy(p => p.Done == p.Total).ThenBy(p => p.StartDate).ToList();
    }

    public async Task<OnboardingTaskDto> CompleteAsync(Guid id, string? note, CancellationToken ct)
    {
        var task = await OwnTaskAsync(id, ct);
        task.Complete(user.UserName, note, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([task], ct))[0];
    }

    public async Task<OnboardingTaskDto> ReopenAsync(Guid id, CancellationToken ct)
    {
        var task = await OwnTaskAsync(id, ct);
        task.Reopen();
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([task], ct))[0];
    }

    public async Task<OnboardingTaskDto> ReassignAsync(Guid id, Guid? assigneeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var task = await db.OnboardingTasks.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("There is no such task.");
        await CheckAssigneeAsync(assigneeId, ct);
        task.Reassign(assigneeId);
        await db.SaveChangesAsync(ct);
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == task.EmployeeId, ct);
        await NotifyAsync(employee, [task], ct);
        return (await ToDtosAsync([task], ct))[0];
    }

    public async Task DeleteTaskAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var task = await db.OnboardingTasks.SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("There is no such task.");
        db.OnboardingTasks.Remove(task);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A task the user may tick off: any for HR, else only one given to them.</summary>
    private async Task<OnboardingTask> OwnTaskAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var task = await db.OnboardingTasks.SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task == null || (!IsHr && (task.AssigneeId == null || task.AssigneeId != await access.MeAsync(ct)))) throw new NotFoundException("There is no such task.");
        return task;
    }

    private async Task<IReadOnlyList<OnboardingTaskDto>> ToDtosAsync(IReadOnlyList<OnboardingTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var today = await TodayAsync(ct);
        var me = await access.MeAsync(ct);
        var ids = tasks.Select(t => t.EmployeeId).Concat(tasks.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value)).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { Name = e.FirstName + " " + e.LastName, e.EmployeeNumber, e.HireDate }, ct);
        return tasks.Select(t =>
        {
            var e = people[t.EmployeeId];
            return new OnboardingTaskDto(t.Id, t.EmployeeId, e.Name, e.EmployeeNumber, e.HireDate, t.Title, t.Description, t.Owner, t.AssigneeId,
                t.AssigneeId is { } a ? people.GetValueOrDefault(a)?.Name : null, t.DueOn, t.IsDone, !t.IsDone && t.DueOn < today, t.CompletedAt, t.CompletedBy,
                t.Note, IsHr || (me != null && t.AssigneeId == me));
        }).ToList();
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);
}
