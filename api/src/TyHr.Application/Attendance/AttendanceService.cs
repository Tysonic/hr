using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Attendance;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Leave;

namespace TyHr.Application.Attendance;

/// <summary>
/// Clocking in and out, punches imported from biometric devices, schedules, and the timesheets and reports built from
/// them. Everyone sees their own timesheet; line managers their team's; HR and payroll everyone's. HR keeps schedules,
/// imports punches and corrects them.
/// </summary>
public class AttendanceService(IAppDbContext db, ITenantContext tenantContext, EmployeeAccess access, ICurrentUser user, TimeProvider clock)
{
    public const int MaxDays = 62;
    public const long MaxImportBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan DoublePunch = TimeSpan.FromMinutes(1);

    // Schedules

    public async Task<IReadOnlyList<ScheduleDto>> ListSchedulesAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var schedules = await db.WorkSchedules.AsNoTracking().OrderByDescending(s => s.IsActive).ThenBy(s => s.Name).ToListAsync(ct);
        var today = await TodayAsync(ct);
        var current = await CurrentSchedulesAsync(today, ct);
        var defaultId = schedules.FirstOrDefault(s => s.IsDefault)?.Id;
        var employees = await db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Exited).Select(e => e.Id).ToListAsync(ct);
        var counts = employees.Select(e => current.GetValueOrDefault(e) ?? defaultId).Where(v => v != null)
            .GroupBy(v => v!.Value).ToDictionary(g => g.Key, g => g.Count());
        return schedules.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<ScheduleDto> CreateScheduleAsync(SaveScheduleRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var schedule = new WorkSchedule(r.Name, r.StartTime, r.EndTime, r.BreakMinutes, WorkWeek.Of(r.WorkingDays), r.GraceMinutes, r.OvertimeMinimumMinutes);
        await CheckNameAsync(schedule, ct);
        if (!await db.WorkSchedules.AnyAsync(s => s.IsDefault, ct)) schedule.SetDefault(true);
        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(ct);
        return ToDto(schedule, 0);
    }

    public async Task<ScheduleDto> UpdateScheduleAsync(Guid id, SaveScheduleRequest r, CancellationToken ct)
    {
        var schedule = await FindScheduleAsync(id, ct);
        schedule.Update(r.Name, r.StartTime, r.EndTime, r.BreakMinutes, WorkWeek.Of(r.WorkingDays), r.GraceMinutes, r.OvertimeMinimumMinutes);
        await CheckNameAsync(schedule, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(schedule, 0);
    }

    /// <summary>The schedule for everyone without an assignment of their own.</summary>
    public async Task<ScheduleDto> MakeDefaultAsync(Guid id, CancellationToken ct)
    {
        var schedule = await FindScheduleAsync(id, ct);
        foreach (var other in await db.WorkSchedules.Where(s => s.IsDefault && s.Id != id).ToListAsync(ct)) other.SetDefault(false);
        schedule.SetDefault(true);
        await db.SaveChangesAsync(ct);
        return ToDto(schedule, 0);
    }

    public async Task<ScheduleDto> SetScheduleActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var schedule = await FindScheduleAsync(id, ct);
        if (!active)
        {
            var today = await TodayAsync(ct);
            if ((await CurrentSchedulesAsync(today, ct)).Values.Contains(id))
                throw new DomainException($"People still work {schedule.Name}; move them to another schedule first.");
        }
        schedule.SetActive(active);
        await db.SaveChangesAsync(ct);
        return ToDto(schedule, 0);
    }

    public async Task<int> AssignAsync(AssignScheduleRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var schedule = await FindScheduleAsync(r.ScheduleId, ct);
        if (!schedule.IsActive) throw new DomainException($"{schedule.Name} isn't in use.");
        var ids = r.EmployeeIds.Distinct().ToList();
        if (ids.Count == 0) throw new DomainException("Choose who works this schedule.");
        if (await db.Employees.CountAsync(e => ids.Contains(e.Id), ct) != ids.Count) throw new NotFoundException("There is no such employee.");
        // A later assignment on the same date replaces the earlier one.
        db.ScheduleAssignments.RemoveRange(await db.ScheduleAssignments.Where(a => ids.Contains(a.EmployeeId) && a.EffectiveFrom == r.EffectiveFrom).ToListAsync(ct));
        foreach (var id in ids) db.ScheduleAssignments.Add(new ScheduleAssignment(id, r.ScheduleId, r.EffectiveFrom, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }

    public async Task<EmployeeAttendanceDto> EmployeeSetupAsync(Guid employeeId, CancellationToken ct)
    {
        var employee = await VisibleEmployeeAsync(employeeId, ct);
        var assignments = await db.ScheduleAssignments.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.EffectiveFrom).ToListAsync(ct);
        var schedules = await db.WorkSchedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var today = await TodayAsync(ct);
        var current = assignments.FirstOrDefault(a => a.EffectiveFrom <= today)?.ScheduleId;
        var effective = current is { } c ? schedules[c] : schedules.Values.FirstOrDefault(s => s.IsDefault);
        var device = await db.AttendanceEnrollments.AsNoTracking().Where(x => x.EmployeeId == employeeId).Select(x => x.DeviceUserId).SingleOrDefaultAsync(ct);
        return new EmployeeAttendanceDto(employee.Id, employee.EmployeeNumber, employee.FullName, device, effective?.Id, effective?.Name, current == null,
            assignments.Select(a => new AssignmentDto(a.Id, a.ScheduleId, schedules[a.ScheduleId].Name, a.EffectiveFrom, a.RecordedBy)).ToList());
    }

    public async Task<EmployeeAttendanceDto> SetDeviceUserAsync(Guid employeeId, string? deviceUserId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw new NotFoundException("There is no such employee.");
        var enrollment = await db.AttendanceEnrollments.SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (Guard.Optional(deviceUserId) is not { } value)
        {
            if (enrollment != null) db.AttendanceEnrollments.Remove(enrollment);
        }
        else
        {
            if (enrollment == null) db.AttendanceEnrollments.Add(enrollment = new AttendanceEnrollment(employeeId, value));
            else enrollment.SetDeviceUserId(value);
            var normalised = enrollment.DeviceUserId;
            if (await db.AttendanceEnrollments.AnyAsync(x => x.DeviceUserId == normalised && x.EmployeeId != employeeId, ct))
                throw new DomainException($"Device user {normalised} is already someone else's.");
        }
        await db.SaveChangesAsync(ct);
        return await EmployeeSetupAsync(employeeId, ct);
    }

    // Clocking

    public async Task<MyClockDto> MyClockAsync(CancellationToken ct)
    {
        var employee = await MeAsync(ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var now = country.ToLocal(clock.GetUtcNow()).DateTime;
        var today = DateOnly.FromDateTime(now);
        var schedule = (await ScheduleResolverAsync([employee.Id], ct))(employee.Id)(today);
        var (from, to) = UtcRange(today, today, country);
        var punches = await db.ClockEvents.AsNoTracking().Where(p => p.EmployeeId == employee.Id && p.Time >= from && p.Time < to)
            .OrderBy(p => p.Time).ToListAsync(ct);
        var next = punches.Count == 0 ? ClockDirection.In
            : punches[^1].Direction == ClockDirection.In ? ClockDirection.Out
            : punches[^1].Direction == ClockDirection.Out ? ClockDirection.In
            : punches.Count % 2 == 0 ? ClockDirection.In : ClockDirection.Out;
        return new MyClockDto(employee.Id, employee.FullName, now, schedule?.Name, schedule?.StartTime, schedule?.EndTime,
            punches.Select(p => ToDto(p, country)).ToList(), next);
    }

    public async Task<MyClockDto> ClockAsync(ClockRequest r, CancellationToken ct)
    {
        var employee = await MeAsync(ct);
        if (employee.Status != EmployeeStatus.Active) throw new DomainException($"You are {employee.Status.ToString().ToLowerInvariant()}, so you can't clock in.");
        var now = clock.GetUtcNow();
        var last = await db.ClockEvents.AsNoTracking().Where(p => p.EmployeeId == employee.Id).OrderByDescending(p => p.Time).FirstOrDefaultAsync(ct);
        if (last != null && now - last.Time < DoublePunch)
            throw new DomainException($"You clocked {last.Direction.ToString().ToLowerInvariant()} a moment ago.");
        db.ClockEvents.Add(new ClockEvent(employee.Id, now, r.Direction, ClockSource.Web, user.UserName, null, r.Latitude, r.Longitude, r.AccuracyMeters));
        await db.SaveChangesAsync(ct);
        return await MyClockAsync(ct);
    }

    /// <summary>HR enters a missed punch (with the reason), in the company's local time.</summary>
    public async Task<PunchDto> AddPunchAsync(AddPunchRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (r.EmployeeId == (await access.MeAsync(ct))) throw new DomainException("You can't correct your own attendance.");
        if (!await db.Employees.AnyAsync(e => e.Id == r.EmployeeId, ct)) throw new NotFoundException("There is no such employee.");
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var time = new DateTimeOffset(DateTime.SpecifyKind(r.LocalTime, DateTimeKind.Unspecified), country.UtcOffset).ToUniversalTime();
        if (time > clock.GetUtcNow().AddMinutes(5)) throw new DomainException("That time hasn't happened yet.");
        if (await db.ClockEvents.AnyAsync(p => p.EmployeeId == r.EmployeeId && p.Time == time, ct))
            throw new DomainException("There is already a punch at that time.");
        var punch = new ClockEvent(r.EmployeeId, time, r.Direction, ClockSource.Manual, user.UserName, r.Note);
        db.ClockEvents.Add(punch);
        await db.SaveChangesAsync(ct);
        return ToDto(punch, country);
    }

    public async Task DeletePunchAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var punch = await db.ClockEvents.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("There is no such punch.");
        if (punch.EmployeeId == await access.MeAsync(ct)) throw new DomainException("You can't correct your own attendance.");
        db.ClockEvents.Remove(punch);
        await db.SaveChangesAsync(ct);
    }

    // Import

    /// <summary>Reads a device export; with <paramref name="commit"/> saves the punches that are new (re-importing is safe).</summary>
    public async Task<PunchImportResult> ImportAsync(Stream content, PunchColumns? columns, bool commit, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var parsed = PunchCsv.Parse(await reader.ReadToEndAsync(ct), columns);
        var country = await db.CompanyCountryAsync(tenantContext, ct);

        var byDevice = await db.AttendanceEnrollments.AsNoTracking().ToDictionaryAsync(x => x.DeviceUserId, x => x.EmployeeId, StringComparer.OrdinalIgnoreCase, ct);
        var byNumber = await db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmployeeNumber, e => e.Id, StringComparer.OrdinalIgnoreCase, ct);
        Guid? Match(string id) => byDevice.TryGetValue(id, out var e) || byNumber.TryGetValue(id, out e) ? e : null;

        var matched = parsed.Punches.Select(p => (Punch: p, Employee: Match(p.DeviceUserId),
            Time: new DateTimeOffset(p.LocalTime, country.UtcOffset).ToUniversalTime())).ToList();
        var known = matched.Where(m => m.Employee != null).ToList();
        var unknown = matched.Where(m => m.Employee == null).GroupBy(m => m.Punch.DeviceUserId)
            .Select(g => new UnknownDeviceUser(g.Key, g.Count())).OrderByDescending(u => u.Punches).ToList();

        var existing = new HashSet<(Guid, DateTimeOffset)>();
        if (known.Count > 0)
        {
            var from = known.Min(m => m.Time);
            var to = known.Max(m => m.Time);
            var employees = known.Select(m => m.Employee!.Value).Distinct().ToList();
            existing = (await db.ClockEvents.AsNoTracking().Where(p => employees.Contains(p.EmployeeId) && p.Time >= from && p.Time <= to)
                .Select(p => new { p.EmployeeId, p.Time }).ToListAsync(ct)).Select(p => (p.EmployeeId, p.Time)).ToHashSet();
        }
        var fresh = new List<ClockEvent>();
        var duplicates = 0;
        foreach (var m in known)
        {
            if (!existing.Add((m.Employee!.Value, m.Time))) { duplicates++; continue; }
            fresh.Add(new ClockEvent(m.Employee.Value, m.Time, m.Punch.Direction, ClockSource.Import, user.UserName));
        }
        if (commit && fresh.Count > 0)
        {
            db.ClockEvents.AddRange(fresh);
            await db.SaveChangesAsync(ct);
        }
        DateOnly? Day(DateTime? t) => t is { } v ? DateOnly.FromDateTime(v) : null;
        return new PunchImportResult(parsed.Headings, parsed.Columns, parsed.Lines, known.Count, duplicates, commit ? fresh.Count : 0,
            Day(known.Count > 0 ? known.Min(m => m.Punch.LocalTime) : null), Day(known.Count > 0 ? known.Max(m => m.Punch.LocalTime) : null),
            unknown, parsed.Errors.Take(50).ToList());
    }

    // Timesheets and reports

    public async Task<TimesheetDto> TimesheetAsync(Guid? employeeId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        CheckRange(from, to);
        var me = await access.MeAsync(ct);
        var employee = employeeId is { } id && id != me ? await VisibleEmployeeAsync(id, ct) : await MeAsync(ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var days = (await BuildAsync([employee], from, to, ct))[employee.Id];
        var (start, end) = UtcRange(from, to, country);
        var punches = await db.ClockEvents.AsNoTracking().Where(p => p.EmployeeId == employee.Id && p.Time >= start && p.Time < end)
            .OrderBy(p => p.Time).ToListAsync(ct);
        return new TimesheetDto(employee.Id, employee.EmployeeNumber, employee.FullName, from, to, days, TimesheetTotals.Of(days),
            punches.Select(p => ToDto(p, country)).ToList(), user.Can(Permissions.AttendanceManage) && employee.Id != me);
    }

    /// <summary>Totals per person for a period: absences and unpaid leave for payroll, overtime, lateness.</summary>
    public async Task<IReadOnlyList<AttendanceSummaryRow>> SummaryAsync(DateOnly from, DateOnly to, Guid? departmentId, CancellationToken ct)
    {
        CheckRange(from, to);
        var employees = await VisibleEmployeesAsync(departmentId, from, ct);
        var days = await BuildAsync(employees, from, to, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return employees.Select(e => new AttendanceSummaryRow(e.Id, e.EmployeeNumber, e.FullName,
            e.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null, TimesheetTotals.Of(days[e.Id]))).ToList();
    }

    /// <summary>Everyone's day: who's in, late, absent, on leave.</summary>
    public async Task<IReadOnlyList<DayBoardRow>> DayAsync(DateOnly date, Guid? departmentId, CancellationToken ct)
    {
        CheckDate(date);
        var employees = await VisibleEmployeesAsync(departmentId, date, ct);
        var days = await BuildAsync(employees, date, date, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return employees.Select(e => new DayBoardRow(e.Id, e.EmployeeNumber, e.FullName, e.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null,
            days[e.Id].Single())).ToList();
    }

    public async Task<Spreadsheet> SummaryExportAsync(DateOnly from, DateOnly to, Guid? departmentId, CancellationToken ct)
    {
        var rows = await SummaryAsync(from, to, departmentId, ct);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Attendance");
        string[] headings = ["Employee number", "Name", "Department", "Working days", "Present", "Absent", "Incomplete", "Days late", "Minutes late",
            "Paid leave days", "Unpaid leave days", "Hours worked", "Overtime hours", "Rest day / holiday hours"];
        for (var c = 0; c < headings.Length; c++) sheet.Cell(1, c + 1).Value = headings[c];
        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            var t = row.Totals;
            object?[] values = [row.EmployeeNumber, row.EmployeeName, row.DepartmentName, t.WorkingDays, t.DaysPresent, t.DaysAbsent, t.DaysIncomplete,
                t.DaysLate, t.LateMinutes, t.PaidLeaveDays, t.UnpaidLeaveDays, Hours(t.WorkedMinutes), Hours(t.OvertimeMinutes), Hours(t.RestDayOvertimeMinutes)];
            for (var c = 0; c < values.Length; c++)
                switch (values[c])
                {
                    case string s: sheet.Cell(r, c + 1).SetValue(s); break;
                    case int i: sheet.Cell(r, c + 1).Value = i; break;
                    case decimal d: sheet.Cell(r, c + 1).Value = d; break;
                }
            r++;
        }
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return new Spreadsheet(stream.ToArray(), $"attendance-{tenantContext.RequireTenant()}-{from:yyyy-MM-dd}-to-{to:yyyy-MM-dd}.xlsx");

        static decimal Hours(int minutes) => Math.Round(minutes / 60m, 2);
    }

    // Building timesheets

    private async Task<Dictionary<Guid, IReadOnlyList<TimesheetDay>>> BuildAsync(IReadOnlyList<Employee> employees, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var today = DateOnly.FromDateTime(country.ToLocal(clock.GetUtcNow()).DateTime);
        var ids = employees.Select(e => e.Id).ToList();
        var scheduleFor = await ScheduleResolverAsync(ids, ct);

        // A day before (overnight shifts) and a day after (their mornings).
        var (start, end) = UtcRange(from.AddDays(-1), to.AddDays(1), country);
        var punches = (await db.ClockEvents.AsNoTracking().Where(p => ids.Contains(p.EmployeeId) && p.Time >= start && p.Time < end)
            .Select(p => new { p.EmployeeId, p.Time }).ToListAsync(ct)).ToLookup(p => p.EmployeeId, p => p.Time);
        var holidays = await db.PublicHolidays.AsNoTracking().Where(h => h.Date >= from && h.Date <= to).ToDictionaryAsync(h => h.Date, h => h.Name, ct);
        var types = await db.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var leave = (await db.LeaveRequests.AsNoTracking()
                .Where(r => ids.Contains(r.EmployeeId) && r.Status == LeaveStatus.Approved && r.EndDate >= from && r.StartDate <= to).ToListAsync(ct))
            .ToLookup(r => r.EmployeeId);

        var result = new Dictionary<Guid, IReadOnlyList<TimesheetDay>>();
        foreach (var e in employees)
        {
            var leaveDays = new Dictionary<DateOnly, LeaveDay>();
            foreach (var r in leave[e.Id])
                for (var d = r.StartDate > from ? r.StartDate : from; d <= r.EndDate && d <= to; d = d.AddDays(1))
                    leaveDays[d] = new LeaveDay(types[r.LeaveTypeId].Name, types[r.LeaveTypeId].IsPaid, r.HalfDay);
            result[e.Id] = Timesheet.Build(from, to, today, e.HireDate, scheduleFor(e.Id), punches[e.Id], country.UtcOffset, holidays, leaveDays);
        }
        return result;
    }

    /// <summary>For each employee, the schedule on a date: their latest assignment from on or before it, else the company default.</summary>
    private async Task<Func<Guid, Func<DateOnly, WorkSchedule?>>> ScheduleResolverAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var schedules = await db.WorkSchedules.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        var fallback = schedules.Values.FirstOrDefault(s => s.IsDefault);
        var assignments = (await db.ScheduleAssignments.AsNoTracking().Where(a => ids.Contains(a.EmployeeId)).ToListAsync(ct))
            .ToLookup(a => a.EmployeeId);
        return id =>
        {
            var history = assignments[id].OrderByDescending(a => a.EffectiveFrom).ToList();
            return date => history.FirstOrDefault(a => a.EffectiveFrom <= date) is { } a ? schedules[a.ScheduleId] : fallback;
        };
    }

    private async Task<Dictionary<Guid, Guid?>> CurrentSchedulesAsync(DateOnly today, CancellationToken ct) =>
        (await db.ScheduleAssignments.AsNoTracking().Where(a => a.EffectiveFrom <= today).ToListAsync(ct))
            .GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => (Guid?)g.MaxBy(a => a.EffectiveFrom)!.ScheduleId);

    // Access

    private async Task<Employee> MeAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new DomainException("Your sign-in isn't linked to an employee record. Ask HR to set your work email on your record.");
        return await db.Employees.AsNoTracking().SingleAsync(e => e.Id == me, ct);
    }

    private async Task<Employee> VisibleEmployeeAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var visible = id == await access.MeAsync(ct) || user.Can(Permissions.AttendanceViewAll)
                      || (user.Can(Permissions.AttendanceView) && await access.InTeamAsync(id, ct));
        return visible ? await db.Employees.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("There is no such employee.")
            : throw new NotFoundException("There is no such employee.");
    }

    /// <summary>Current employees (and those who left after <paramref name="since"/>) the user may report on.</summary>
    private async Task<IReadOnlyList<Employee>> VisibleEmployeesAsync(Guid? departmentId, DateOnly since, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var query = db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Exited);
        if (!user.Can(Permissions.AttendanceViewAll))
        {
            var team = user.Can(Permissions.AttendanceView) ? await access.TeamAsync(ct) : [];
            var me = await access.MeAsync(ct);
            query = query.Where(e => team.Contains(e.Id) || e.Id == me);
        }
        if (departmentId is { } dept) query = query.Where(e => e.DepartmentId == dept);
        return await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(ct);
    }

    private static void CheckDate(DateOnly date)
    {
        if (date.Year is < 2000 or > 2100) throw new DomainException("Choose a date between 2000 and 2100.");
    }

    private static void CheckRange(DateOnly from, DateOnly to)
    {
        CheckDate(from);
        CheckDate(to);
        if (to < from) throw new DomainException("The period ends before it starts.");
        if (to.DayNumber - from.DayNumber >= MaxDays) throw new DomainException($"Choose at most {MaxDays} days.");
    }

    private static (DateTimeOffset Start, DateTimeOffset End) UtcRange(DateOnly from, DateOnly to, CountryPack country) =>
        (new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), country.UtcOffset).ToUniversalTime(),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), country.UtcOffset).ToUniversalTime());

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    private async Task CheckNameAsync(WorkSchedule schedule, CancellationToken ct)
    {
        if (await db.WorkSchedules.AnyAsync(s => s.Id != schedule.Id && s.Name.ToLower() == schedule.Name.ToLower(), ct))
            throw new DomainException($"There is already a schedule called {schedule.Name}.");
    }

    private async Task<WorkSchedule> FindScheduleAsync(Guid id, CancellationToken ct) =>
        await db.WorkSchedules.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("There is no such schedule.");

    private static ScheduleDto ToDto(WorkSchedule s, int employees) => new(s.Id, s.Name, s.StartTime, s.EndTime, s.BreakMinutes, s.Week.Days,
        s.GraceMinutes, s.OvertimeMinimumMinutes, s.ScheduledMinutes, s.Overnight, s.IsDefault, s.IsActive, employees);

    private static PunchDto ToDto(ClockEvent p, CountryPack country) => new(p.Id, p.Time, country.ToLocal(p.Time).DateTime, p.Direction, p.Source,
        p.RecordedBy, p.Note, p.Latitude, p.Longitude, p.AccuracyMeters);
}
