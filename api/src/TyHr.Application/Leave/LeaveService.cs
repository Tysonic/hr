using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Documents;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;

namespace TyHr.Application.Leave;

/// <summary>
/// Leave requests, approvals, balances and the leave calendar. Who may do what:
/// <list type="bullet">
/// <item>Anyone with an employee record (matched by work email) requests and sees their own leave.</item>
/// <item>Line managers decide requests from their team; HR (<see cref="Permissions.LeaveManage"/>) decides anyone's,
/// records leave on someone's behalf (optionally already approved) and adjusts balances. Nobody decides their own.</item>
/// <item>HR and payroll see everyone's leave; managers their team's; everyone else sees who in their department is away,
/// without the kind of leave.</item>
/// </list>
/// </summary>
public class LeaveService(IAppDbContext db, ITenantContext tenantContext, EmployeeAccess access, ICurrentUser user, LeaveNotifier notifier,
    DocumentService documents, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    private bool CanManage => user.Can(Permissions.LeaveManage);
    private bool CanViewAll => user.Can(Permissions.LeaveViewAll);

    // Requests

    public async Task<LeavePreview> PreviewAsync(CreateLeaveRequest r, CancellationToken ct)
    {
        var employee = await TargetEmployeeAsync(r.EmployeeId, ct);
        return await EvaluateAsync(employee, r, ct);
    }

    public async Task<LeaveRequestDto> CreateAsync(CreateLeaveRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var employee = await TargetEmployeeAsync(r.EmployeeId, ct);
        var me = await access.MeAsync(ct);
        if (r.Approve && (!CanManage || employee.Id == me)) throw new DomainException("You can't approve your own leave; it goes to your manager.");
        if (r.DocumentId is { } documentId && !await db.EmployeeDocuments.AnyAsync(d => d.Id == documentId && d.EmployeeId == employee.Id, ct))
            throw new DomainException("That document isn't on this employee's record.");

        var check = await EvaluateAsync(employee, r, ct);
        if (check.Problems.Count > 0) throw new DomainException(string.Join(" ", check.Problems));

        var request = new LeaveRequest(employee.Id, r.LeaveTypeId, r.StartDate, r.EndDate, r.HalfDay, check.Days, r.Reason, r.DocumentId,
            user.UserName, clock.GetUtcNow());
        if (r.Approve) request.Approve(user.UserName, clock.GetUtcNow(), "Recorded by HR");
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync(ct);

        var dto = await GetAsync(request.Id, ct);
        if (request.Status == LeaveStatus.Approved) await notifier.DecidedAsync(dto, ct);
        else await notifier.RequestedAsync(dto, ct);
        return dto;
    }

    public async Task<LeaveRequestDto> ApproveAsync(Guid id, string? note, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (!await CanDecideAsync(request, ct)) throw new DomainException("You can't decide this request.");
        request.Approve(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        var dto = await GetAsync(id, ct);
        await notifier.DecidedAsync(dto, ct);
        return dto;
    }

    public async Task<LeaveRequestDto> RejectAsync(Guid id, string? note, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (!await CanDecideAsync(request, ct)) throw new DomainException("You can't decide this request.");
        request.Reject(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        var dto = await GetAsync(id, ct);
        await notifier.DecidedAsync(dto, ct);
        return dto;
    }

    public async Task<LeaveRequestDto> CancelAsync(Guid id, string? note, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (!await CanCancelAsync(request, ct))
            throw new DomainException(request.Status == LeaveStatus.Approved
                ? "Leave that has started can only be cancelled by HR." : "You can't cancel this request.");
        var wasApproved = request.Status == LeaveStatus.Approved;
        request.Cancel(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        var dto = await GetAsync(id, ct);
        if (wasApproved || request.RequestedBy != user.UserName) await notifier.DecidedAsync(dto, ct);
        return dto;
    }

    /// <summary>Files a supporting document (e.g. a medical certificate) on the employee's record and links it to the request.</summary>
    public async Task<LeaveRequestDto> AttachDocumentAsync(Guid id, string fileName, long size, Stream content, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (request.EmployeeId != await access.MeAsync(ct) && !CanManage) throw new DomainException("You can't add documents to this request.");
        if (!request.IsLive) throw new DomainException($"This request is {request.Status.ToString().ToLowerInvariant()}.");
        var type = await db.LeaveTypes.AsNoTracking().SingleAsync(t => t.Id == request.LeaveTypeId, ct);
        var document = await documents.UploadAsync(request.EmployeeId, new DocumentDetails(
            $"{type.Name} {request.StartDate:d MMM yyyy}" + (request.EndDate != request.StartDate ? $" to {request.EndDate:d MMM yyyy}" : ""),
            DocumentCategory.Medical, null, null, "Attached to a leave request"), fileName, size, content, ct);
        request.AttachDocument(document.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>A request's supporting document, for the employee, HR and whoever may decide it.</summary>
    public async Task<DocumentDownload> DocumentAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (request.DocumentId is not { } documentId) throw new NotFoundException("This request has no document.");
        return await documents.DownloadAsync(request.EmployeeId, documentId, ct);
    }

    public async Task<LeaveRequestDto> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        return (await ToDtosAsync([request], ct)).Single();
    }

    public async Task<PagedResult<LeaveRequestDto>> ListAsync(LeaveRequestQuery q, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var (page, pageSize) = Paging.Normalize(q.Page, q.PageSize);
        var me = await access.MeAsync(ct);
        var query = db.LeaveRequests.AsNoTracking();
        switch (q.Scope)
        {
            case "mine":
                query = query.Where(r => r.EmployeeId == me);
                break;
            case "approvals":
                // Pending requests the user may decide: everyone's for HR, the team's for a manager; never their own.
                if (!CanManage)
                {
                    if (!user.Can(Permissions.LeaveApprove)) return new PagedResult<LeaveRequestDto>([], page, pageSize, 0);
                    var team = await access.TeamAsync(ct);
                    query = query.Where(r => team.Contains(r.EmployeeId));
                }
                query = query.Where(r => r.Status == LeaveStatus.Pending && r.EmployeeId != me);
                break;
            default:
                if (!CanViewAll)
                {
                    var team = await access.TeamAsync(ct);
                    query = query.Where(r => team.Contains(r.EmployeeId) || r.EmployeeId == me);
                }
                break;
        }
        if (q.EmployeeId is { } employeeId) query = query.Where(r => r.EmployeeId == employeeId);
        if (q.Status is { } status) query = query.Where(r => r.Status == status);
        if (q.From is { } from) query = query.Where(r => r.EndDate >= from);
        if (q.To is { } to) query = query.Where(r => r.StartDate <= to);

        var total = await query.CountAsync(ct);
        var ordered = q.Scope == "approvals" ? query.OrderBy(r => r.StartDate) : query.OrderByDescending(r => r.StartDate);
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<LeaveRequestDto>(await ToDtosAsync(items, ct), page, pageSize, total);
    }

    // Balances

    /// <summary>One employee's leave year: balances of every active type, adjustments and requests.</summary>
    public async Task<EmployeeLeaveDto> EmployeeLeaveAsync(Guid? employeeId, int? year, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        var id = employeeId ?? me ?? throw new DomainException(NoRecordMessage);
        if (id != me && !CanViewAll && !await access.InTeamAsync(id, ct)) throw new NotFoundException("There is no such employee.");
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("There is no such employee.");

        var calendar = await CalendarAsync(ct);
        var leaveYear = year ?? calendar.LeaveYearOf(Today);
        var (start, end) = calendar.LeaveYear(leaveYear);
        var asOf = Today < start ? start : Today > end ? end : Today;
        var types = await db.LeaveTypes.AsNoTracking().Where(t => t.IsActive || db.LeaveRequests.Any(r => r.LeaveTypeId == t.Id && r.EmployeeId == id))
            .OrderBy(t => t.Name).ToListAsync(ct);
        var requests = await db.LeaveRequests.AsNoTracking().Where(r => r.EmployeeId == id).ToListAsync(ct);
        var adjustments = await db.LeaveAdjustments.AsNoTracking().Where(a => a.EmployeeId == id).ToListAsync(ct);

        var balances = types
            .Where(t => t.EligibleGender == null || t.EligibleGender == employee.Gender)
            .Select(t =>
            {
                var rules = new LeaveBalances(calendar, t, employee.HireDate, requests, adjustments.Where(a => a.LeaveTypeId == t.Id).ToList());
                var used = requests.Where(r => r.LeaveTypeId == t.Id && r.Status == LeaveStatus.Approved)
                    .Sum(r => calendar.LeaveYearOf(r.StartDate) == leaveYear ? r.Days : 0);
                return new LeaveTypeBalanceDto(t.Id, t.Code, t.Name, t.Accrual, t.IsPaid, t.HasBalance ? rules.For(leaveYear, asOf) : null, used,
                    t.MinServiceMonths > 0 ? employee.HireDate.AddMonths(t.MinServiceMonths) : null);
            }).ToList();
        var typeNames = types.ToDictionary(t => t.Id, t => t.Name);
        var yearAdjustments = adjustments.Where(a => a.LeaveYear == leaveYear).OrderBy(a => a.RecordedAt)
            .Select(a => new LeaveAdjustmentDto(a.Id, a.LeaveTypeId, typeNames.GetValueOrDefault(a.LeaveTypeId, "?"), a.LeaveYear, a.Days, a.Reason,
                a.RecordedBy, a.RecordedAt)).ToList();
        var yearRequests = await ToDtosAsync(requests.Where(r => r.EndDate >= start && r.StartDate <= end).OrderByDescending(r => r.StartDate).ToList(), ct);
        return new EmployeeLeaveDto(employee.Id, employee.EmployeeNumber, employee.FullName, employee.Gender, employee.HireDate, leaveYear, start, end,
            balances, yearAdjustments, yearRequests, CanManage && id != me);
    }

    public async Task<EmployeeLeaveDto> AdjustAsync(AdjustLeaveRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (r.EmployeeId == await access.MeAsync(ct)) throw new DomainException("You can't adjust your own leave balance.");
        if (!await db.Employees.AnyAsync(e => e.Id == r.EmployeeId, ct)) throw new NotFoundException("There is no such employee.");
        var type = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct) ?? throw new NotFoundException("There is no such leave type.");
        if (!type.HasBalance) throw new DomainException($"{type.Name} has no balance to adjust.");
        db.LeaveAdjustments.Add(new LeaveAdjustment(r.EmployeeId, r.LeaveTypeId, r.LeaveYear, r.Days, r.Reason, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await EmployeeLeaveAsync(r.EmployeeId, r.LeaveYear, ct);
    }

    // Calendar

    public async Task<LeaveCalendarDto> CalendarAsync(DateOnly from, DateOnly to, Guid? departmentId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (to < from || to.DayNumber - from.DayNumber > 92) throw new DomainException("Show at most three months at a time.");
        var me = await access.MeAsync(ct);
        var team = await access.TeamAsync(ct);
        Guid? myDepartment = me == null ? null : await db.Employees.Where(e => e.Id == me).Select(e => e.DepartmentId).SingleAsync(ct);

        var query = db.LeaveRequests.AsNoTracking().Where(r => r.EndDate >= from && r.StartDate <= to
            && (r.Status == LeaveStatus.Pending || r.Status == LeaveStatus.Approved));
        var employees = db.Employees.AsNoTracking();
        if (departmentId is { } dept) employees = employees.Where(e => e.DepartmentId == dept);
        if (!CanViewAll)
            // Your own leave, your team's, and your department's approved leave.
            employees = employees.Where(e => e.Id == me || team.Contains(e.Id) || (myDepartment != null && e.DepartmentId == myDepartment));
        var rows = await query.Join(employees, r => r.EmployeeId, e => e.Id, (r, e) => new
            {
                r.Id, EmployeeId = e.Id, e.FirstName, e.LastName, e.DepartmentId, r.LeaveTypeId, r.StartDate, r.EndDate, r.HalfDay, r.Status,
            }).ToListAsync(ct);

        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var entries = rows
            .Where(x => CanViewAll || x.EmployeeId == me || team.Contains(x.EmployeeId) || x.Status == LeaveStatus.Approved)
            .Select(x =>
            {
                var seesType = CanViewAll || x.EmployeeId == me || team.Contains(x.EmployeeId);
                return new LeaveCalendarEntry(x.Id, x.EmployeeId, $"{x.FirstName} {x.LastName}",
                    x.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null, seesType ? types.GetValueOrDefault(x.LeaveTypeId) : null,
                    x.StartDate, x.EndDate, x.HalfDay, x.Status);
            })
            .OrderBy(e => e.StartDate).ThenBy(e => e.EmployeeName).ToList();
        var holidays = await db.PublicHolidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).OrderBy(h => h.Date)
            .Select(h => new HolidayDto(h.Id, h.Date, h.Name)).ToListAsync(ct);
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        return new LeaveCalendarDto(from, to, entries, holidays, profile.WorkWeek.Days);
    }

    // Rules

    private const string NoRecordMessage = "Your sign-in isn't linked to an employee record. Ask HR to set your work email on your record.";

    private async Task<Employee> TargetEmployeeAsync(Guid? employeeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct);
        var id = employeeId ?? me ?? throw new DomainException(NoRecordMessage);
        if (id != me && !CanManage) throw new DomainException("You can only request leave for yourself.");
        return await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("There is no such employee.");
    }

    private async Task<LeavePreview> EvaluateAsync(Employee employee, CreateLeaveRequest r, CancellationToken ct)
    {
        var problems = new List<string>();
        var type = await db.LeaveTypes.AsNoTracking().SingleOrDefaultAsync(t => t.Id == r.LeaveTypeId, ct)
            ?? throw new NotFoundException("There is no such leave type.");
        var calendar = await CalendarAsync(ct);
        var days = r.EndDate < r.StartDate ? 0 : calendar.CountDays(r.StartDate, r.EndDate, type.CountsCalendarDays, r.HalfDay);
        var requests = await db.LeaveRequests.AsNoTracking().Where(x => x.EmployeeId == employee.Id).ToListAsync(ct);
        var adjustments = await db.LeaveAdjustments.AsNoTracking().Where(a => a.EmployeeId == employee.Id && a.LeaveTypeId == type.Id).ToListAsync(ct);
        var rules = new LeaveBalances(calendar, type, employee.HireDate, requests, adjustments);
        decimal? availableAfter = null;

        if (!type.IsActive) problems.Add($"{type.Name} is no longer offered.");
        if (employee.Status != EmployeeStatus.Active) problems.Add($"{employee.FullName} is {employee.Status.ToString().ToLowerInvariant()}.");
        if (r.EndDate < r.StartDate) problems.Add("The leave ends before it starts.");
        else if (r.HalfDay && r.EndDate != r.StartDate) problems.Add("A half day is a single day: start and end on the same date.");
        else if (days == 0) problems.Add("Those dates are all weekends or public holidays, so no leave days are needed.");
        if (r.StartDate < employee.HireDate) problems.Add($"{employee.FullName} started on {employee.HireDate:d MMM yyyy}.");
        if (type.EligibleGender is { } gender && employee.Gender != gender) problems.Add($"{type.Name} is for {gender.ToString().ToLowerInvariant()} employees.");
        if (!rules.Eligible(r.StartDate))
            problems.Add($"{type.Name} can be taken after {type.MinServiceMonths} months' service, from {employee.HireDate.AddMonths(type.MinServiceMonths):d MMM yyyy}.");
        if (type.MaxDaysPerRequest is { } max && days > max) problems.Add($"{type.Name} is at most {max:0.#} days at a time; this is {days:0.#}.");

        var overlap = requests.FirstOrDefault(x => x.IsLive && x.StartDate <= r.EndDate && x.EndDate >= r.StartDate);
        if (overlap != null)
            problems.Add($"It overlaps {(overlap.Status == LeaveStatus.Pending ? "a pending" : "approved")} request for {overlap.StartDate:d MMM}–{overlap.EndDate:d MMM yyyy}.");

        if (type.HasBalance && days > 0 && r.EndDate >= r.StartDate)
        {
            foreach (var (leaveYear, needed) in calendar.DaysByLeaveYear(r.StartDate, r.EndDate, type.CountsCalendarDays, r.HalfDay))
            {
                var yearEnd = calendar.LeaveYear(leaveYear).End;
                var balance = rules.For(leaveYear, r.EndDate < yearEnd ? r.EndDate : yearEnd);
                var left = balance.Available - needed;
                availableAfter ??= left;
                if (left < 0)
                    problems.Add($"Only {Math.Max(0, balance.Available):0.#} days of {type.Name.ToLowerInvariant()} are available " +
                                 $"{(calendar.YearStartMonth == 1 ? $"in {leaveYear}" : $"in the leave year from {calendar.LeaveYear(leaveYear).Start:MMM yyyy}")}; this needs {needed:0.#}.");
            }
        }
        var needsDocument = type.DocumentRequiredAfterDays is { } after && days > after && r.DocumentId == null;
        return new LeavePreview(days, availableAfter, needsDocument, problems);
    }

    private async Task<bool> CanDecideAsync(LeaveRequest r, CancellationToken ct)
    {
        if (r.EmployeeId == await access.MeAsync(ct)) return false;
        return CanManage || (user.Can(Permissions.LeaveApprove) && await access.InTeamAsync(r.EmployeeId, ct));
    }

    private async Task<bool> CanCancelAsync(LeaveRequest r, CancellationToken ct)
    {
        if (!r.IsLive) return false;
        if (r.EmployeeId == await access.MeAsync(ct)) return r.Status == LeaveStatus.Pending || r.StartDate > Today;
        return CanManage;
    }

    /// <summary>A request the user may see: their own, their team's, or anyone's for HR and payroll. Others get "not found".</summary>
    private async Task<LeaveRequest> FindAsync(Guid id, CancellationToken ct)
    {
        var request = await db.LeaveRequests.SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("There is no such leave request.");
        if (!CanViewAll && request.EmployeeId != await access.MeAsync(ct) && !await access.InTeamAsync(request.EmployeeId, ct))
            throw new NotFoundException("There is no such leave request.");
        return request;
    }

    private async Task<LeaveCalendar> CalendarAsync(CancellationToken ct)
    {
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var holidays = await db.PublicHolidays.AsNoTracking().Select(h => h.Date).ToListAsync(ct);
        return new LeaveCalendar(profile.WorkWeek, holidays, profile.FinancialYearStartMonth);
    }

    private async Task<List<LeaveRequestDto>> ToDtosAsync(IReadOnlyList<LeaveRequest> requests, CancellationToken ct)
    {
        if (requests.Count == 0) return [];
        var employeeIds = requests.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking().Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeNumber, Name = e.FirstName + " " + e.LastName }, ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var result = new List<LeaveRequestDto>();
        foreach (var r in requests)
        {
            var type = types[r.LeaveTypeId];
            result.Add(new LeaveRequestDto(r.Id, r.EmployeeId, employees[r.EmployeeId].EmployeeNumber, employees[r.EmployeeId].Name, r.LeaveTypeId,
                type.Name, r.StartDate, r.EndDate, r.HalfDay, r.Days, r.Reason, r.Status, r.RequestedBy, r.RequestedAt, r.DecidedBy, r.DecidedAt,
                r.DecisionNote, r.DocumentId, type.DocumentRequiredAfterDays is { } after && r.Days > after && r.DocumentId == null,
                r.Status == LeaveStatus.Pending && await CanDecideAsync(r, ct), await CanCancelAsync(r, ct)));
        }
        return result;
    }
}
