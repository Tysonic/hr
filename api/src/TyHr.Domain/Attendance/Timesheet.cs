namespace TyHr.Domain.Attendance;

public enum DayStatus
{
    /// <summary>Clocked in and out on a working day.</summary>
    Present,
    /// <summary>Only one punch on a working day: in without out (or the reverse). Needs a correction.</summary>
    Incomplete,
    /// <summary>A working day with no punches and no leave.</summary>
    Absent,
    OnLeave,
    Holiday,
    RestDay,
    /// <summary>Today or later, with nothing recorded yet.</summary>
    Upcoming,
    /// <summary>Before the hire date.</summary>
    NotEmployed,
}

/// <summary>Approved leave on a day, as the timesheet needs it.</summary>
public record LeaveDay(string LeaveType, bool Paid, bool HalfDay);

/// <summary>One day of someone's timesheet. Times are local; minutes are whole.</summary>
public record TimesheetDay(DateOnly Date, DayStatus Status, string? ScheduleName, int ScheduledMinutes, DateTime? FirstIn, DateTime? LastOut,
    int PunchCount, int WorkedMinutes, int LateMinutes, int EarlyLeaveMinutes, int OvertimeMinutes, int RestDayOvertimeMinutes,
    string? HolidayName, LeaveDay? Leave);

/// <summary>A period's totals: what payroll needs (absences, unpaid leave, overtime) and what managers watch (lateness).</summary>
public record TimesheetTotals(int WorkingDays, int DaysPresent, int DaysAbsent, int DaysIncomplete, int DaysLate, int LateMinutes,
    decimal PaidLeaveDays, decimal UnpaidLeaveDays, int WorkedMinutes, int OvertimeMinutes, int RestDayOvertimeMinutes)
{
    public static TimesheetTotals Of(IReadOnlyList<TimesheetDay> days)
    {
        var working = days.Where(d => d.Status is DayStatus.Present or DayStatus.Incomplete or DayStatus.Absent or DayStatus.OnLeave).ToList();
        decimal LeaveDays(bool paid) => days.Where(d => d.Leave is { } l && l.Paid == paid).Sum(d => d.Leave!.HalfDay ? 0.5m : 1m);
        return new TimesheetTotals(working.Count,
            days.Count(d => d.Status is DayStatus.Present or DayStatus.Incomplete || (d.Status == DayStatus.OnLeave && d.PunchCount > 0)),
            days.Count(d => d.Status == DayStatus.Absent), days.Count(d => d.Status == DayStatus.Incomplete),
            days.Count(d => d.LateMinutes > 0), days.Sum(d => d.LateMinutes), LeaveDays(true), LeaveDays(false),
            days.Sum(d => d.WorkedMinutes), days.Sum(d => d.OvertimeMinutes), days.Sum(d => d.RestDayOvertimeMinutes));
    }
}

/// <summary>
/// Turns punches into days. Pure: the caller supplies the schedule for each date, the punches (UTC), holidays and
/// approved leave. The rules:
/// <list type="bullet">
/// <item>A punch belongs to its local date, except that punches up to 6 hours after an overnight shift's end belong
/// to the day the shift started.</item>
/// <item>Worked time runs from the first punch to the last, less the break when the stay was longer than half the
/// shift (a short Saturday morning has no lunch break). A single punch is incomplete and counts no time.</item>
/// <item>Late: first punch after start + grace (lateness counts from the start). Early leave: last punch before the end.</item>
/// <item>Overtime on a working day: time beyond the scheduled minutes (half of them on a half day's leave), counted
/// only from the schedule's minimum. Everything worked on a rest day or holiday is rest-day overtime.</item>
/// <item>Approved leave on a working day makes it a leave day, not an absence, with no lateness counted (a half day
/// may be either half). Days from <paramref name="today"/> on are upcoming, not absent.</item>
/// </list>
/// </summary>
public static class Timesheet
{
    private static readonly TimeSpan OvernightTail = TimeSpan.FromHours(6);

    public static IReadOnlyList<TimesheetDay> Build(DateOnly from, DateOnly to, DateOnly today, DateOnly hireDate, Func<DateOnly, WorkSchedule?> scheduleFor,
        IEnumerable<DateTimeOffset> punches, TimeSpan utcOffset, IReadOnlyDictionary<DateOnly, string> holidays, IReadOnlyDictionary<DateOnly, LeaveDay> leave)
    {
        var byDay = punches.Select(p => p.ToOffset(utcOffset).DateTime).OrderBy(t => t)
            .GroupBy(t => ShiftDate(t, scheduleFor)).ToDictionary(g => g.Key, g => g.ToList());
        var days = new List<TimesheetDay>();
        for (var date = from; date <= to; date = date.AddDays(1))
            days.Add(Day(date, today, hireDate, scheduleFor(date), byDay.GetValueOrDefault(date) ?? [], holidays.GetValueOrDefault(date), leave.GetValueOrDefault(date)));
        return days;
    }

    private static DateOnly ShiftDate(DateTime local, Func<DateOnly, WorkSchedule?> scheduleFor)
    {
        var date = DateOnly.FromDateTime(local);
        var previous = date.AddDays(-1);
        var shift = scheduleFor(previous);
        if (shift is { Overnight: true } && shift.Week.Includes(previous.DayOfWeek) && local.TimeOfDay < shift.EndTime.ToTimeSpan() + OvernightTail)
            return previous;
        return date;
    }

    private static TimesheetDay Day(DateOnly date, DateOnly today, DateOnly hireDate, WorkSchedule? schedule, List<DateTime> punches, string? holiday, LeaveDay? leave)
    {
        var working = schedule != null && schedule.Week.Includes(date.DayOfWeek) && holiday == null;
        var scheduled = working ? schedule!.ScheduledMinutes : 0;
        DateTime? first = punches.Count > 0 ? punches[0] : null;
        DateTime? last = punches.Count > 1 ? punches[^1] : null;
        var worked = 0;
        if (first != null && last != null)
        {
            var span = (int)(last.Value - first.Value).TotalMinutes;
            var breakMinutes = schedule?.BreakMinutes ?? 0;
            var halfShift = schedule == null ? 0 : (schedule.ScheduledMinutes + breakMinutes) / 2;
            worked = span > halfShift ? Math.Max(0, span - breakMinutes) : span;
        }

        if (date < hireDate) return Empty(DayStatus.NotEmployed);
        if (!working)
            return new TimesheetDay(date, holiday != null ? DayStatus.Holiday : DayStatus.RestDay, schedule?.Name, 0, first, last, punches.Count,
                worked, 0, 0, 0, worked, holiday, null);

        var start = date.ToDateTime(schedule!.StartTime);
        var end = date.ToDateTime(schedule.EndTime).AddDays(schedule.Overnight ? 1 : 0);
        var late = first != null && first > start.AddMinutes(schedule.GraceMinutes) ? (int)(first.Value - start).TotalMinutes : 0;
        var early = last != null && last < end ? (int)(end - last.Value).TotalMinutes : 0;
        var expected = leave is { HalfDay: true } ? scheduled / 2 : scheduled;
        var extra = worked - expected;
        var overtime = punches.Count > 1 && extra >= Math.Max(1, schedule.OvertimeMinimumMinutes) ? extra : 0;

        DayStatus status;
        if (leave != null)
        {
            status = DayStatus.OnLeave;
            late = early = 0;
        }
        else if (punches.Count == 0) status = date >= today ? DayStatus.Upcoming : DayStatus.Absent;
        else if (punches.Count == 1) status = DayStatus.Incomplete;
        else status = DayStatus.Present;
        if (status == DayStatus.Incomplete) early = 0;

        return new TimesheetDay(date, status, schedule.Name, scheduled, first, last, punches.Count, worked, late, early, overtime, 0, null, leave);

        TimesheetDay Empty(DayStatus s) => new(date, s, schedule?.Name, 0, null, null, 0, 0, 0, 0, 0, 0, holiday, null);
    }
}
