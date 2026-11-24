using TyHr.Application.Attendance;
using TyHr.Domain;
using TyHr.Domain.Attendance;
using TyHr.Domain.Leave;

namespace TyHr.Tests;

/// <summary>Timesheet arithmetic and device-file reading, without a database. Times are East Africa Time (UTC+3).</summary>
public class AttendanceRulesTests
{
    private static readonly TimeSpan Eat = TimeSpan.FromHours(3);
    private static readonly DateOnly Mon = new(2026, 11, 2), Today = new(2026, 11, 23), Hired = new(2025, 1, 6);

    // 08:00–17:00 with an hour's lunch: 8 hours a day.
    private static readonly WorkSchedule Office = new("Office hours", new(8, 0), new(17, 0), 60, WorkWeek.MondayToFriday, 15, 30);
    private static readonly WorkSchedule Nights = new("Nights", new(22, 0), new(6, 0), 30, WorkWeek.MondayToFriday, 10, 30);

    private static DateTimeOffset At(DateOnly date, int hour, int minute = 0) => new(date.ToDateTime(new TimeOnly(hour, minute)), Eat);

    private static IReadOnlyList<TimesheetDay> Build(DateOnly from, DateOnly to, IEnumerable<DateTimeOffset> punches, WorkSchedule? schedule = null,
        Dictionary<DateOnly, string>? holidays = null, Dictionary<DateOnly, LeaveDay>? leave = null, DateOnly? hired = null) =>
        Timesheet.Build(from, to, Today, hired ?? Hired, _ => schedule ?? Office, punches, Eat, holidays ?? [], leave ?? []);

    private static TimesheetDay Day(DateOnly date, params DateTimeOffset[] punches) => Build(date, date, punches).Single();

    [Fact]
    public void A_full_day_counts_the_time_between_first_and_last_punch_less_the_break()
    {
        var day = Day(Mon, At(Mon, 7, 55), At(Mon, 12, 30), At(Mon, 13, 30), At(Mon, 17, 5));
        Assert.Equal((DayStatus.Present, 490, 0, 0, 0), (day.Status, day.WorkedMinutes, day.LateMinutes, day.EarlyLeaveMinutes, day.OvertimeMinutes));
        Assert.Equal((4, 480), (day.PunchCount, day.ScheduledMinutes));
        Assert.Equal(new DateTime(2026, 11, 2, 7, 55, 0), day.FirstIn);
    }

    [Fact]
    public void Lateness_counts_from_the_start_once_past_the_grace_period()
    {
        Assert.Equal(0, Day(Mon, At(Mon, 8, 14), At(Mon, 17)).LateMinutes);
        Assert.Equal(20, Day(Mon, At(Mon, 8, 20), At(Mon, 17)).LateMinutes);
        Assert.Equal(45, Day(Mon, At(Mon, 8), At(Mon, 16, 15)).EarlyLeaveMinutes);
    }

    [Fact]
    public void Overtime_is_time_beyond_the_scheduled_day_counted_only_from_the_minimum()
    {
        Assert.Equal(0, Day(Mon, At(Mon, 8), At(Mon, 17, 25)).OvertimeMinutes);
        Assert.Equal(120, Day(Mon, At(Mon, 8), At(Mon, 19)).OvertimeMinutes);
    }

    [Fact]
    public void Working_days_without_punches_are_absences_until_today_and_upcoming_after()
    {
        var days = Build(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), []);
        Assert.Equal([DayStatus.Absent, DayStatus.RestDay, DayStatus.RestDay, DayStatus.Upcoming, DayStatus.Upcoming], days.Select(d => d.Status));
    }

    [Fact]
    public void A_single_punch_is_incomplete_and_counts_no_time()
    {
        var day = Day(Mon, At(Mon, 8, 30));
        Assert.Equal((DayStatus.Incomplete, 0, 30, 0, 0), (day.Status, day.WorkedMinutes, day.LateMinutes, day.EarlyLeaveMinutes, day.OvertimeMinutes));
        Assert.Null(day.LastOut);
    }

    [Fact]
    public void Everything_worked_on_a_rest_day_or_holiday_is_rest_day_overtime_and_a_short_shift_keeps_its_break()
    {
        var sat = new DateOnly(2026, 11, 7);
        var saturday = Day(sat, At(sat, 9), At(sat, 13));
        Assert.Equal((DayStatus.RestDay, 240, 240, 0), (saturday.Status, saturday.WorkedMinutes, saturday.RestDayOvertimeMinutes, saturday.OvertimeMinutes));

        var christmas = new DateOnly(2026, 12, 25);
        var holiday = Build(christmas, christmas, [At(christmas, 8), At(christmas, 17)], holidays: new() { [christmas] = "Christmas Day" }).Single();
        Assert.Equal((DayStatus.Holiday, "Christmas Day", 480, 480), (holiday.Status, holiday.HolidayName, holiday.WorkedMinutes, holiday.RestDayOvertimeMinutes));
    }

    [Fact]
    public void Approved_leave_is_not_an_absence_and_a_half_day_halves_the_expected_time()
    {
        var tue = Mon.AddDays(1);
        var days = Build(Mon, tue, [At(tue, 13), At(tue, 19)], leave: new()
        {
            [Mon] = new LeaveDay("Annual leave", true, false),
            [tue] = new LeaveDay("Unpaid leave", false, true),
        });
        Assert.Equal(DayStatus.OnLeave, days[0].Status);
        // The afternoon worked on a half day: 6 hours less the break against 4 expected, no lateness for the morning off.
        Assert.Equal((DayStatus.OnLeave, 0, 60), (days[1].Status, days[1].LateMinutes, days[1].OvertimeMinutes));

        var totals = TimesheetTotals.Of(days);
        Assert.Equal((2, 0, 1m, 0.5m, 1), (totals.WorkingDays, totals.DaysAbsent, totals.PaidLeaveDays, totals.UnpaidLeaveDays, totals.DaysPresent));
    }

    [Fact]
    public void An_overnight_shift_belongs_to_the_day_it_starts()
    {
        var tue = Mon.AddDays(1);
        var wed = Mon.AddDays(2);
        var days = Build(Mon, tue, [At(Mon, 21, 55), At(tue, 6, 5), At(tue, 22, 20), At(wed, 6)], Nights);
        Assert.Equal((DayStatus.Present, 2, 460, 0), (days[0].Status, days[0].PunchCount, days[0].WorkedMinutes, days[0].LateMinutes));
        Assert.Equal((DayStatus.Present, 20, 430), (days[1].Status, days[1].LateMinutes, days[1].WorkedMinutes));
        Assert.Equal(450, Nights.ScheduledMinutes);
    }

    [Fact]
    public void Days_before_the_hire_date_are_not_counted()
    {
        var days = Build(Mon, Mon.AddDays(1), [], hired: Mon.AddDays(1));
        Assert.Equal([DayStatus.NotEmployed, DayStatus.Absent], days.Select(d => d.Status));
        Assert.Equal(1, TimesheetTotals.Of(days).WorkingDays);
    }

    [Fact]
    public void Totals_add_up_lateness_overtime_and_absences()
    {
        var tue = Mon.AddDays(1);
        var days = Build(Mon, Mon.AddDays(6), [At(Mon, 8, 30), At(Mon, 19), At(tue, 8), At(tue, 17)]);
        var t = TimesheetTotals.Of(days);
        Assert.Equal((5, 2, 3, 1, 30, 90), (t.WorkingDays, t.DaysPresent, t.DaysAbsent, t.DaysLate, t.LateMinutes, t.OvertimeMinutes));
    }

    [Fact]
    public void Schedules_reject_impossible_shifts()
    {
        Assert.Contains("same time", Assert.Throws<DomainException>(() => new WorkSchedule("X", new(8, 0), new(8, 0), 0, WorkWeek.MondayToFriday, 0, 0)).Message);
        Assert.Contains("as long as the shift", Assert.Throws<DomainException>(() => new WorkSchedule("X", new(8, 0), new(9, 0), 60, WorkWeek.MondayToFriday, 0, 0)).Message);
        Assert.Contains("hand", Assert.Throws<DomainException>(() => new ClockEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, ClockDirection.In, ClockSource.Manual, "hr")).Message);
    }

    [Fact]
    public void A_ZKTeco_export_is_read_with_its_single_time_column_and_state_codes()
    {
        const string csv = """
            AC-No.,Name,Time,State,New State,Exception
            0012,Amina,23/11/2026 07:58,C/In,,
            0012,Amina,23/11/2026 17:04,C/Out,,
            7,Brian,23/11/2026 8:31,0,,
            7,Brian,31/11/2026 17:00,1,,
            ,Nobody,23/11/2026 09:00,0,,
            """;
        var r = PunchCsv.Parse(csv);
        Assert.Equal(new PunchColumns("AC-No.", "Time", null, null, "State"), r.Columns);
        Assert.Equal([("12", new DateTime(2026, 11, 23, 7, 58, 0), ClockDirection.In), ("12", new DateTime(2026, 11, 23, 17, 4, 0), ClockDirection.Out),
            ("7", new DateTime(2026, 11, 23, 8, 31, 0), ClockDirection.In)], r.Punches.Select(p => (p.DeviceUserId, p.LocalTime, p.Direction)));
        Assert.Equal(2, r.Errors.Count);
        Assert.Contains("Line 5", r.Errors[0]);
        Assert.Contains("no user number", r.Errors[1]);
    }

    [Fact]
    public void A_Hikvision_export_with_separate_date_and_time_columns_is_read()
    {
        const string tsv = "Employee ID\tName\tDepartment\tDate\tTime\tAttendance Status\n"
                           + "EMP-0004\tBrian\tFinance\t2026-11-23\t08:01:12\tCheck In\n"
                           + "EMP-0004\tBrian\tFinance\t2026-11-23\t17:30:00\tCheck Out\n";
        var r = PunchCsv.Parse(tsv);
        Assert.Equal(new PunchColumns("Employee ID", null, "Date", "Time", "Attendance Status"), r.Columns);
        Assert.Equal([ClockDirection.In, ClockDirection.Out], r.Punches.Select(p => p.Direction));
        Assert.Equal(new DateTime(2026, 11, 23, 8, 1, 12), r.Punches[0].LocalTime);
    }

    [Fact]
    public void Unrecognised_headings_say_what_was_found()
    {
        var e = Assert.Throws<DomainException>(() => PunchCsv.Parse("Who,When\n1,2026-11-23 08:00\n"));
        Assert.Contains("Headings: Who, When", e.Message);
        var r = PunchCsv.Parse("Who,When\n1,2026-11-23 08:00\n", new PunchColumns(Id: "Who", DateTime: "When"));
        Assert.Equal(ClockDirection.Unknown, Assert.Single(r.Punches).Direction);
    }
}
