using TyHr.Domain.Employees;
using TyHr.Domain.Leave;

namespace TyHr.Application.Leave;

public record LeaveTypeDto(Guid Id, string Code, string Name, LeaveAccrual Accrual, decimal? DaysPerYear, decimal? MaxDaysPerRequest,
    decimal CarryForwardMax, bool CountsCalendarDays, bool IsPaid, Gender? EligibleGender, int MinServiceMonths, int? DocumentRequiredAfterDays,
    string? Description, bool IsActive);

public record SaveLeaveTypeRequest(string Code, string Name, LeaveAccrual Accrual, decimal? DaysPerYear, decimal? MaxDaysPerRequest,
    decimal CarryForwardMax, bool CountsCalendarDays, bool IsPaid, Gender? EligibleGender, int MinServiceMonths, int? DocumentRequiredAfterDays,
    string? Description);

public record HolidayDto(Guid Id, DateOnly Date, string Name);
public record SaveHolidayRequest(DateOnly Date, string Name);

/// <summary>Holidays added from the country pack, and what HR still has to add by hand (Eid, gazetted days).</summary>
public record GenerateHolidaysResult(int Added, int AlreadyThere, string Note);

/// <summary><see cref="LeaveYearStartMonth"/> comes from the company profile's financial year.</summary>
public record LeaveSettingsDto(IReadOnlyList<DayOfWeek> WorkingDays, int LeaveYearStartMonth, int CurrentLeaveYear);
public record SaveWorkWeekRequest(IReadOnlyList<DayOfWeek> WorkingDays);

// Requests and balances

public record LeaveRequestDto(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, Guid LeaveTypeId, string LeaveTypeName,
    DateOnly StartDate, DateOnly EndDate, bool HalfDay, decimal Days, string? Reason, LeaveStatus Status, string RequestedBy,
    DateTimeOffset RequestedAt, string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionNote, Guid? DocumentId, bool NeedsDocument,
    bool CanDecide, bool CanCancel);

/// <summary>A request for one's own leave (no EmployeeId) or, for HR, someone else's; HR may record it as approved.</summary>
public record CreateLeaveRequest(Guid? EmployeeId, Guid LeaveTypeId, DateOnly StartDate, DateOnly EndDate, bool HalfDay, string? Reason,
    Guid? DocumentId = null, bool Approve = false);

public record DecideLeaveRequest(string? Note);

public record LeaveRequestQuery(string Scope = "mine", Guid? EmployeeId = null, LeaveStatus? Status = null, DateOnly? From = null,
    DateOnly? To = null, int Page = 1, int PageSize = 50);

/// <summary>What a request would come to, checked without saving: its days, the balance left, and any problems.</summary>
public record LeavePreview(decimal Days, decimal? AvailableAfter, bool NeedsDocument, IReadOnlyList<string> Problems);

/// <summary>A type's balance for the year (null <see cref="Balance"/> for types without one: only days used are counted).</summary>
public record LeaveTypeBalanceDto(Guid LeaveTypeId, string Code, string Name, LeaveAccrual Accrual, bool IsPaid, LeaveBalance? Balance,
    decimal UsedThisYear, DateOnly? EligibleFrom);

public record LeaveAdjustmentDto(Guid Id, Guid LeaveTypeId, string LeaveTypeName, int LeaveYear, decimal Days, string Reason,
    string RecordedBy, DateTimeOffset RecordedAt);

public record AdjustLeaveRequest(Guid EmployeeId, Guid LeaveTypeId, int LeaveYear, decimal Days, string Reason);

/// <summary>One employee's leave for a leave year: balances, adjustments and requests.</summary>
public record EmployeeLeaveDto(Guid EmployeeId, string EmployeeNumber, string EmployeeName, Gender Gender, DateOnly HireDate, int Year,
    DateOnly YearStart, DateOnly YearEnd, IReadOnlyList<LeaveTypeBalanceDto> Balances, IReadOnlyList<LeaveAdjustmentDto> Adjustments,
    IReadOnlyList<LeaveRequestDto> Requests, bool CanManage);

/// <summary>Leave on the calendar. <see cref="LeaveTypeName"/> is null where the viewer may only know someone is away.</summary>
public record LeaveCalendarEntry(Guid RequestId, Guid EmployeeId, string EmployeeName, string? DepartmentName, string? LeaveTypeName,
    DateOnly StartDate, DateOnly EndDate, bool HalfDay, LeaveStatus Status);

public record LeaveCalendarDto(DateOnly From, DateOnly To, IReadOnlyList<LeaveCalendarEntry> Entries, IReadOnlyList<HolidayDto> Holidays,
    IReadOnlyList<DayOfWeek> WorkingDays);
