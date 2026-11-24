using TyHr.Domain.Leave;

namespace TyHr.Domain.Attendance;

/// <summary>
/// A shift: when people are expected, on which days. A shift ending before it starts runs overnight (22:00–06:00) and
/// belongs to the day it starts. One schedule is the company default, for employees without an assignment.
/// </summary>
public class WorkSchedule : TenantEntity
{
    private WorkSchedule() { }

    public WorkSchedule(string name, TimeOnly start, TimeOnly end, int breakMinutes, WorkWeek days, int graceMinutes, int overtimeMinimumMinutes)
    {
        Update(name, start, end, breakMinutes, days, graceMinutes, overtimeMinimumMinutes);
        IsActive = true;
    }

    public string Name { get; private set; } = null!;
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    /// <summary>Unpaid break inside the shift, taken off worked time.</summary>
    public int BreakMinutes { get; private set; }

    public int WorkingDays { get; private set; }
    public WorkWeek Week => new(WorkingDays);

    /// <summary>Arriving within this many minutes of the start isn't late.</summary>
    public int GraceMinutes { get; private set; }

    /// <summary>Extra time on a working day counts as overtime only from this many minutes (smaller amounts are ignored).</summary>
    public int OvertimeMinimumMinutes { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public bool Overnight => EndTime <= StartTime;

    /// <summary>Expected working minutes in a day: the shift's length less its break.</summary>
    public int ScheduledMinutes => (int)((Overnight ? EndTime.ToTimeSpan() + TimeSpan.FromDays(1) : EndTime.ToTimeSpan()) - StartTime.ToTimeSpan()).TotalMinutes - BreakMinutes;

    public void Update(string name, TimeOnly start, TimeOnly end, int breakMinutes, WorkWeek days, int graceMinutes, int overtimeMinimumMinutes)
    {
        Name = Guard.Required(name, "Name");
        if (start == end) throw new DomainException("The shift starts and ends at the same time.");
        if (breakMinutes is < 0 or > 240) throw new DomainException("The break must be between 0 and 240 minutes.");
        if (graceMinutes is < 0 or > 120) throw new DomainException("The grace period must be between 0 and 120 minutes.");
        if (overtimeMinimumMinutes is < 0 or > 240) throw new DomainException("The overtime minimum must be between 0 and 240 minutes.");
        StartTime = start;
        EndTime = end;
        BreakMinutes = breakMinutes;
        WorkingDays = WorkWeek.Of(days.Days).Mask;
        GraceMinutes = graceMinutes;
        OvertimeMinimumMinutes = overtimeMinimumMinutes;
        if (ScheduledMinutes <= 0) throw new DomainException("The break is as long as the shift.");
    }

    public void SetDefault(bool isDefault)
    {
        if (isDefault && !IsActive) throw new DomainException("Only an active schedule can be the default.");
        IsDefault = isDefault;
    }

    public void SetActive(bool active)
    {
        if (!active && IsDefault) throw new DomainException($"{Name} is the default schedule; make another one the default first.");
        IsActive = active;
    }
}

/// <summary>Which schedule an employee works from a date on (until their next assignment).</summary>
public class ScheduleAssignment : TenantEntity
{
    private ScheduleAssignment() { }

    public ScheduleAssignment(Guid employeeId, Guid scheduleId, DateOnly effectiveFrom, string recordedBy, DateTimeOffset recordedAt)
    {
        EmployeeId = employeeId;
        ScheduleId = scheduleId;
        EffectiveFrom = effectiveFrom;
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public Guid EmployeeId { get; private set; }
    public Guid ScheduleId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public string RecordedBy { get; private set; } = null!;
    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>An employee's user number on the company's biometric devices, to match imported punches.</summary>
public class AttendanceEnrollment : TenantEntity
{
    private AttendanceEnrollment() { }

    public AttendanceEnrollment(Guid employeeId, string deviceUserId)
    {
        EmployeeId = employeeId;
        SetDeviceUserId(deviceUserId);
    }

    public Guid EmployeeId { get; private set; }
    public string DeviceUserId { get; private set; } = null!;

    public void SetDeviceUserId(string value) => DeviceUserId = Guard.Required(value, "Device user ID").TrimStart('0') is { Length: > 0 } v ? v : "0";
}

public enum ClockDirection { In, Out, Unknown }
public enum ClockSource { Web, Import, Manual }

/// <summary>
/// One punch: someone clocking in or out, on the web, on a biometric device (imported) or entered by HR. Stored in
/// UTC; days are worked out in the company's local time. Corrections are made by adding manual punches with a note
/// (and removing wrong ones), so the audit trail shows what changed.
/// </summary>
public class ClockEvent : TenantEntity
{
    private ClockEvent() { }

    public ClockEvent(Guid employeeId, DateTimeOffset time, ClockDirection direction, ClockSource source, string recordedBy,
        string? note = null, double? latitude = null, double? longitude = null, double? accuracyMeters = null)
    {
        if (source == ClockSource.Manual && Guard.Optional(note) == null) throw new DomainException("Say why the time is entered by hand.");
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180) throw new DomainException("That location isn't valid.");
        EmployeeId = employeeId;
        Time = time.ToUniversalTime();
        Direction = direction;
        Source = source;
        RecordedBy = recordedBy;
        Note = Guard.Optional(note);
        Latitude = latitude;
        Longitude = longitude;
        AccuracyMeters = accuracyMeters;
    }

    public Guid EmployeeId { get; private set; }
    public DateTimeOffset Time { get; private set; }
    public ClockDirection Direction { get; private set; }
    public ClockSource Source { get; private set; }
    public string RecordedBy { get; private set; } = null!;
    public string? Note { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }
}
