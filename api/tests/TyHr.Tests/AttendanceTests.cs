using System.Text;
using TyHr.Application.Attendance;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Attendance;
using TyHr.Domain.Leave;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Attendance end to end against Postgres. Each test has its own clock starting Monday 23 Nov 2026, 12:00 in Kampala
/// (09:00 UTC). The company: Grace (HR admin), Mark (line manager) with Amina, and Brian who reports to Grace.
/// </summary>
public class AttendanceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Eat = TimeSpan.FromHours(3);
    private static readonly DateOnly Mon16 = new(2026, 11, 16), Fri20 = new(2026, 11, 20), Mon23 = new(2026, 11, 23);
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    private sealed record Services(Scope Scope, AttendanceService Attendance, EmployeeService Employees) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string email, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: email.Split('@')[0], roles: roles, email: email);
        var access = new EmployeeAccess(scope.Db, scope.User);
        return new Services(scope, new AttendanceService(scope.Db, scope.TenantContext, access, scope.User, _clock),
            new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, _clock));
    }

    private sealed record Company(string Tenant, Guid Grace, Guid Mark, Guid Amina, Guid Brian, string BrianNumber);

    private async Task<Company> CompanyAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var hr = As(co, "grace@co.test", Roles.HrAdmin);
        await AttendanceDefaults.SeedAsync(hr.Scope.Db, default);
        var grace = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Grace", "Admin", email: "grace@co.test"), default);
        var mark = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Manager", email: "mark@co.test"), default);
        var amina = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Amina", "Nakato", managerId: mark.Id, email: "amina@co.test"), default);
        var brian = await hr.Employees.CreateAsync(EmployeeTests.NewEmployee("Brian", "Okello", managerId: grace.Id, email: "brian@co.test"), default);
        return new Company(co, grace.Id, mark.Id, amina.Id, brian.Id, brian.EmployeeNumber);
    }

    private static Stream Csv(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Employees_clock_in_and_out_on_the_web_and_their_timesheet_shows_lateness()
    {
        var c = await CompanyAsync();
        _clock.Advance(TimeSpan.FromHours(-3.5)); // 08:30 in Kampala
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
        {
            var before = await amina.Attendance.MyClockAsync(default);
            Assert.Equal((ClockDirection.In, "Office hours", new TimeOnly(8, 0)), (before.Next, before.ScheduleName, before.StartTime));

            var clockedIn = await amina.Attendance.ClockAsync(new ClockRequest(ClockDirection.In, 0.3476, 32.5825, 20), default);
            Assert.Equal(ClockDirection.Out, clockedIn.Next);
            Assert.Equal(new DateTime(2026, 11, 23, 8, 30, 0), Assert.Single(clockedIn.Today).LocalTime);

            _clock.Advance(TimeSpan.FromSeconds(20));
            Assert.Contains("a moment ago", (await Assert.ThrowsAsync<DomainException>(() =>
                amina.Attendance.ClockAsync(new ClockRequest(ClockDirection.In, null, null, null), default))).Message);

            _clock.Advance(TimeSpan.FromHours(9)); // 17:30
            await amina.Attendance.ClockAsync(new ClockRequest(ClockDirection.Out, null, null, null), default);

            var sheet = await amina.Attendance.TimesheetAsync(null, Mon23, Mon23, default);
            var day = Assert.Single(sheet.Days);
            Assert.Equal((DayStatus.Present, 30, 480, 0), (day.Status, day.LateMinutes, day.WorkedMinutes, day.OvertimeMinutes));
            Assert.False(sheet.CanCorrect);
        }
    }

    [Fact]
    public async Task People_without_an_employee_record_are_told_to_ask_HR()
    {
        var c = await CompanyAsync();
        await using var stranger = As(c.Tenant, "nobody@co.test", Roles.Employee);
        Assert.Contains("Ask HR", (await Assert.ThrowsAsync<DomainException>(() => stranger.Attendance.MyClockAsync(default))).Message);
    }

    [Fact]
    public async Task Device_exports_match_by_device_number_or_employee_number_and_reimporting_adds_nothing()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace@co.test", Roles.HrOfficer);
        await hr.Attendance.SetDeviceUserAsync(c.Amina, "0012", default);
        await using (var other = As(c.Tenant, "grace@co.test", Roles.HrOfficer))
            Assert.Contains("already someone else's", (await Assert.ThrowsAsync<DomainException>(() => other.Attendance.SetDeviceUserAsync(c.Brian, "12", default))).Message);

        var csv = $"""
            AC-No.,Name,Time,State
            12,Amina,16/11/2026 07:58,C/In
            12,Amina,16/11/2026 17:04,C/Out
            {c.BrianNumber},Brian,16/11/2026 08:40,C/In
            {c.BrianNumber},Brian,16/11/2026 17:00,C/Out
            99,Visitor,16/11/2026 09:00,C/In
            """;
        var check = await hr.Attendance.ImportAsync(Csv(csv), null, commit: false, default);
        Assert.Equal((5, 4, 0, 0), (check.Lines, check.Matched, check.Imported, check.AlreadyImported));
        Assert.Equal((Mon16, Mon16), (check.From, check.To));
        Assert.Equal("99", Assert.Single(check.UnknownUsers).DeviceUserId);

        Assert.Equal(4, (await hr.Attendance.ImportAsync(Csv(csv), null, commit: true, default)).Imported);
        var again = await hr.Attendance.ImportAsync(Csv(csv), null, commit: true, default);
        Assert.Equal((0, 4), (again.Imported, again.AlreadyImported));

        var summary = (await hr.Attendance.SummaryAsync(Mon16, Fri20, null, default)).ToDictionary(r => r.EmployeeName);
        Assert.Equal((1, 4, 0), (summary["Amina Nakato"].Totals.DaysPresent, summary["Amina Nakato"].Totals.DaysAbsent, summary["Amina Nakato"].Totals.DaysLate));
        Assert.Equal((1, 40), (summary["Brian Okello"].Totals.DaysLate, summary["Brian Okello"].Totals.LateMinutes));
        Assert.Equal(5, summary["Mark Manager"].Totals.DaysAbsent);

        var export = await hr.Attendance.SummaryExportAsync(Mon16, Fri20, null, default);
        Assert.EndsWith("2026-11-16-to-2026-11-20.xlsx", export.FileName);
    }

    [Fact]
    public async Task Approved_leave_shows_on_the_timesheet_instead_of_an_absence()
    {
        var c = await CompanyAsync();
        await using (var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin))
        {
            var unpaid = new LeaveType("UNPAID", "Unpaid leave", LeaveAccrual.NoBalance, null, null, 0, false, false, null, 0, null, null);
            hr.Scope.Db.LeaveTypes.Add(unpaid);
            await hr.Scope.Db.SaveChangesAsync();
            var request = new LeaveRequest(c.Amina, unpaid.Id, Mon16, Mon16.AddDays(1), false, 2, "Family", null, "grace", _clock.GetUtcNow());
            request.Approve("grace", _clock.GetUtcNow(), null);
            hr.Scope.Db.LeaveRequests.Add(request);
            await hr.Scope.Db.SaveChangesAsync();
        }
        await using var amina = As(c.Tenant, "amina@co.test", Roles.Employee);
        var sheet = await amina.Attendance.TimesheetAsync(null, Mon16, Fri20, default);
        Assert.Equal([DayStatus.OnLeave, DayStatus.OnLeave, DayStatus.Absent, DayStatus.Absent, DayStatus.Absent], sheet.Days.Select(d => d.Status));
        Assert.Equal((2m, 3), (sheet.Totals.UnpaidLeaveDays, sheet.Totals.DaysAbsent));
    }

    [Fact]
    public async Task Managers_see_their_team_only_and_HR_corrects_others_but_not_themselves()
    {
        var c = await CompanyAsync();
        await using (var mark = As(c.Tenant, "mark@co.test", Roles.LineManager))
        {
            Assert.Equal(c.Amina, (await mark.Attendance.TimesheetAsync(c.Amina, Mon16, Fri20, default)).EmployeeId);
            await Assert.ThrowsAsync<NotFoundException>(() => mark.Attendance.TimesheetAsync(c.Brian, Mon16, Fri20, default));
            Assert.Equal(["Mark Manager", "Amina Nakato"], (await mark.Attendance.DayAsync(Mon16, null, default)).Select(r => r.EmployeeName).OrderByDescending(n => n));
        }
        await using (var amina = As(c.Tenant, "amina@co.test", Roles.Employee))
            await Assert.ThrowsAsync<NotFoundException>(() => amina.Attendance.TimesheetAsync(c.Mark, Mon16, Fri20, default));

        await using var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin);
        Assert.Contains("your own", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Grace, new DateTime(2026, 11, 16, 8, 0, 0), ClockDirection.In, "Forgot"), default))).Message);
        Assert.Contains("happened yet", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Brian, new DateTime(2026, 11, 24, 8, 0, 0), ClockDirection.In, "Early"), default))).Message);

        await hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Brian, new DateTime(2026, 11, 16, 8, 0, 0), ClockDirection.In, "Device was down"), default);
        var half = await hr.Attendance.TimesheetAsync(c.Brian, Mon16, Mon16, default);
        Assert.Equal(DayStatus.Incomplete, half.Days[0].Status);
        Assert.True(half.CanCorrect);
        var outPunch = await hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Brian, new DateTime(2026, 11, 16, 17, 0, 0), ClockDirection.Out, "Device was down"), default);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 14, 0, 0, TimeSpan.Zero), outPunch.Time); // stored in UTC
        Assert.Equal(DayStatus.Present, (await hr.Attendance.TimesheetAsync(c.Brian, Mon16, Mon16, default)).Days[0].Status);

        await hr.Attendance.DeletePunchAsync(outPunch.Id, default);
        Assert.Equal(DayStatus.Incomplete, (await hr.Attendance.TimesheetAsync(c.Brian, Mon16, Mon16, default)).Days[0].Status);
    }

    [Fact]
    public async Task Schedules_apply_from_their_date_and_the_default_covers_everyone_else()
    {
        var c = await CompanyAsync();
        await using var hr = As(c.Tenant, "grace@co.test", Roles.HrAdmin);
        var office = Assert.Single(await hr.Attendance.ListSchedulesAsync(default));
        Assert.Equal((true, 4), (office.IsDefault, office.EmployeeCount));

        var nights = await hr.Attendance.CreateScheduleAsync(new SaveScheduleRequest("Nights", new(22, 0), new(6, 0), 30,
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], 10, 30), default);
        Assert.False(nights.IsDefault);
        Assert.True(nights.Overnight);
        Assert.Contains("already a schedule", (await Assert.ThrowsAsync<DomainException>(() =>
            hr.Attendance.CreateScheduleAsync(new SaveScheduleRequest("nights", new(20, 0), new(4, 0), 0, [DayOfWeek.Monday], 0, 0), default))).Message);

        Assert.Equal(1, await hr.Attendance.AssignAsync(new AssignScheduleRequest([c.Brian], nights.Id, Fri20), default));
        var brian = await hr.Attendance.EmployeeSetupAsync(c.Brian, default);
        Assert.Equal(("Nights", false), (brian.CurrentScheduleName, brian.OnDefaultSchedule));

        // Brian's night of Friday 20th runs into Saturday morning; Thursday was still office hours.
        await hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Brian, new DateTime(2026, 11, 20, 21, 58, 0), ClockDirection.In, "Test"), default);
        await hr.Attendance.AddPunchAsync(new AddPunchRequest(c.Brian, new DateTime(2026, 11, 21, 6, 2, 0), ClockDirection.Out, "Test"), default);
        var sheet = await hr.Attendance.TimesheetAsync(c.Brian, Fri20.AddDays(-1), Fri20.AddDays(1), default);
        Assert.Equal([("Office hours", DayStatus.Absent), ("Nights", DayStatus.Present), ("Nights", DayStatus.RestDay)],
            sheet.Days.Select(d => (d.ScheduleName, d.Status)));
        Assert.Equal(454, sheet.Days[1].WorkedMinutes);

        Assert.Contains("still work", (await Assert.ThrowsAsync<DomainException>(() => hr.Attendance.SetScheduleActiveAsync(nights.Id, false, default))).Message);
        Assert.Contains("default", (await Assert.ThrowsAsync<DomainException>(() => hr.Attendance.SetScheduleActiveAsync(office.Id, false, default))).Message);
        await hr.Attendance.MakeDefaultAsync(nights.Id, default);
        Assert.Equal(["Nights"], (await hr.Attendance.ListSchedulesAsync(default)).Where(s => s.IsDefault).Select(s => s.Name));
    }
}
