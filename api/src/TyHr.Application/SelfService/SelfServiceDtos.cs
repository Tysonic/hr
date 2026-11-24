using TyHr.Application.Employees;
using TyHr.Domain.Attendance;
using TyHr.Domain.Employees;
using TyHr.Domain.SelfService;

namespace TyHr.Application.SelfService;

/// <summary>One field a change request changes. <see cref="Current"/> differs from <see cref="Before"/> when HR changed it since.</summary>
public record ChangeField(string Label, string? Before, string? Proposed, string? Current);

public record ProfileChangeDto(Guid Id, Guid EmployeeId, string EmployeeNumber, string EmployeeName, ProfileChangeKind Kind, string What,
    ProfileChangeStatus Status, string? Reason, string RequestedBy, DateTimeOffset RequestedAt, string? DecidedBy, DateTimeOffset? DecidedAt,
    string? DecisionNote, IReadOnlyList<ChangeField> Changes, bool CanDecide, bool CanWithdraw);

public record RequestPersonalChange(PersonalDetails Details, string? Reason);
public record RequestPaymentChange(PaymentDetails Details, string? Reason);
public record RequestNextOfKinChange(Guid? NextOfKinId, SaveNextOfKinRequest Details, string? Reason);
public record DecideProfileChange(string? Note);

public record AnnouncementDto(Guid Id, string Title, string Body, bool Pinned, DateOnly? ExpiresOn, string PublishedBy, DateTimeOffset PublishedAt, bool Current);
public record SaveAnnouncementRequest(string Title, string Body, bool Pinned, DateOnly? ExpiresOn);

/// <summary>What waits for the signed-in user to decide.</summary>
public record InboxDto(int LeaveRequests, int ProfileChanges, int PayrollApprovals, int Requisitions, int Probations, int Resignations, int Total);

public record TeamLeave(DateOnly StartDate, DateOnly EndDate, string LeaveTypeName, bool Pending);

public record TeamMemberDto(Guid Id, string EmployeeNumber, string Name, string? PositionTitle, string? DepartmentName, bool DirectReport,
    EmployeeStatus Status, TimesheetDay? Today, IReadOnlyList<TeamLeave> UpcomingLeave);
