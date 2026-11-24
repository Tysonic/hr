using TyHr.Domain.Attendance;

namespace TyHr.Application.Attendance;

public record ScheduleDto(Guid Id, string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, IReadOnlyList<DayOfWeek> WorkingDays,
    int GraceMinutes, int OvertimeMinimumMinutes, int ScheduledMinutes, bool Overnight, bool IsDefault, bool IsActive, int EmployeeCount);

public record SaveScheduleRequest(string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, IReadOnlyList<DayOfWeek> WorkingDays,
    int GraceMinutes, int OvertimeMinimumMinutes);

/// <summary>Puts employees on a schedule from a date.</summary>
public record AssignScheduleRequest(IReadOnlyList<Guid> EmployeeIds, Guid ScheduleId, DateOnly EffectiveFrom);

/// <summary>An employee's attendance set-up: current schedule, schedule history and device user number.</summary>
public record EmployeeAttendanceDto(Guid EmployeeId, string EmployeeNumber, string EmployeeName, string? DeviceUserId, Guid? CurrentScheduleId,
    string? CurrentScheduleName, bool OnDefaultSchedule, IReadOnlyList<AssignmentDto> Assignments);

public record AssignmentDto(Guid Id, Guid ScheduleId, string ScheduleName, DateOnly EffectiveFrom, string RecordedBy);
public record SetDeviceUserRequest(string? DeviceUserId);

public record ClockRequest(ClockDirection Direction, double? Latitude, double? Longitude, double? AccuracyMeters);

public record PunchDto(Guid Id, DateTimeOffset Time, DateTime LocalTime, ClockDirection Direction, ClockSource Source, string RecordedBy, string? Note,
    double? Latitude, double? Longitude, double? AccuracyMeters);

/// <summary>The signed-in person's clock: today's punches and what the next punch should be.</summary>
public record MyClockDto(Guid EmployeeId, string EmployeeName, DateTime LocalNow, string? ScheduleName, TimeOnly? StartTime, TimeOnly? EndTime,
    IReadOnlyList<PunchDto> Today, ClockDirection Next);

public record AddPunchRequest(Guid EmployeeId, DateTime LocalTime, ClockDirection Direction, string Note);

public record TimesheetDto(Guid EmployeeId, string EmployeeNumber, string EmployeeName, DateOnly From, DateOnly To, IReadOnlyList<TimesheetDay> Days,
    TimesheetTotals Totals, IReadOnlyList<PunchDto> Punches, bool CanCorrect);

public record AttendanceSummaryRow(Guid EmployeeId, string EmployeeNumber, string EmployeeName, string? DepartmentName, TimesheetTotals Totals);

public record DayBoardRow(Guid EmployeeId, string EmployeeNumber, string EmployeeName, string? DepartmentName, TimesheetDay Day);

/// <summary>What an import file holds, checked without saving; <see cref="Imported"/> is set once saved.</summary>
public record PunchImportResult(IReadOnlyList<string> Headings, PunchColumns Columns, int Lines, int Matched, int AlreadyImported, int Imported,
    DateOnly? From, DateOnly? To, IReadOnlyList<UnknownDeviceUser> UnknownUsers, IReadOnlyList<string> Errors);

public record UnknownDeviceUser(string DeviceUserId, int Punches);
