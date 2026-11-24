using TyHr.Domain.Exits;

namespace TyHr.Application.Exits;

/// <summary>The statutory notice for someone given notice on a date, and the last working day it leads to.</summary>
public record NoticeDto(int Days, string Basis, string Source, int MonthsOfService, DateOnly SuggestedLastDay);

public record ClearanceItemDto(Guid Id, Guid ExitId, Guid EmployeeId, string EmployeeName, DateOnly LastWorkingDay, string Title, string? Description,
    ClearanceOwner Owner, Guid? AssigneeId, string? AssigneeName, DateOnly DueOn, bool IsDone, bool IsOverdue, DateTimeOffset? CompletedAt, string? CompletedBy,
    string? Note, bool CanComplete);

public record ExitInterviewDto(InterviewAnswers Answers, bool ByEmployee, string? SubmittedBy, DateTimeOffset? SubmittedAt);

public record ExitListItem(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? DepartmentName, ExitType Type, ExitStatus Status,
    DateOnly NoticeGivenOn, DateOnly LastWorkingDay, int Cleared, int Items, bool Interviewed);

public record ExitDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? PositionTitle, string? DepartmentName, string? ManagerName,
    DateOnly HireDate, ExitType Type, string? Reason, ExitStatus Status, DateOnly NoticeGivenOn, DateOnly LastWorkingDay, bool PayInLieu, NoticeDto Notice,
    int NoticeDaysGiven, bool ShortNotice, string RequestedBy, DateTimeOffset RequestedAt, string? AcceptedBy, DateTimeOffset? AcceptedAt,
    bool? EligibleForRehire, string? CompletionNote, string? CompletedBy, DateTimeOffset? CompletedAt, string? WithdrawnBy, DateTimeOffset? WithdrawnAt,
    string? WithdrawalNote, IReadOnlyList<ClearanceItemDto> Clearance, ExitInterviewDto? Interview, IReadOnlyList<string> Warnings, bool CanComplete);

/// <summary>The leaver's own view: no reason HR recorded, only their own clearance items, and their interview if they gave it.</summary>
public record MyExitDto(Guid Id, ExitType Type, ExitStatus Status, DateOnly NoticeGivenOn, DateOnly LastWorkingDay, string? Reason,
    IReadOnlyList<ClearanceItemDto> Clearance, ExitInterviewDto? Interview, bool CanWithdraw, bool CanInterview);

public record CreateExitRequest(Guid EmployeeId, ExitType Type, string? Reason, DateOnly NoticeGivenOn, DateOnly LastWorkingDay, bool PayInLieu, Guid? ItContactId);
public record UpdateExitRequest(ExitType Type, string? Reason, DateOnly NoticeGivenOn, DateOnly LastWorkingDay, bool PayInLieu);
public record ResignRequest(DateOnly LastWorkingDay, string? Reason);
public record AcceptResignationRequest(DateOnly LastWorkingDay, bool PayInLieu, Guid? ItContactId);
public record WithdrawExitRequest(string? Note);
public record CompleteExitRequest(bool EligibleForRehire, string? Note);
public record AddClearanceRequest(ClearanceStep Step, Guid? AssigneeId);
public record ClearItemRequest(string? Note);
public record ExitActionResult(ExitDto Exit, IReadOnlyList<string> Warnings);
