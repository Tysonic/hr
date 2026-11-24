using TyHr.Domain.Onboarding;

namespace TyHr.Application.Onboarding;

public record OnboardingTemplateDto(Guid Id, string Name, IReadOnlyList<ChecklistItem> Items, bool IsDefault);
public record OnboardingSetupDto(IReadOnlyList<OnboardingTemplateDto> Templates, IReadOnlyList<ChecklistItem> Standard);
public record SaveOnboardingTemplateRequest(string Name, IReadOnlyList<ChecklistItem> Items, bool IsDefault);

/// <summary>Starts onboarding from a template (null: the default template, else TyHR's standard list).</summary>
public record StartOnboardingRequest(Guid? TemplateId, Guid? ItContactId);

public record AddOnboardingTaskRequest(ChecklistItem Item, Guid? AssigneeId);
public record CompleteTaskRequest(string? Note);
public record ReassignTaskRequest(Guid? AssigneeId);

public record OnboardingTaskDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, DateOnly StartDate, string Title, string? Description,
    TaskOwner Owner, Guid? AssigneeId, string? AssigneeName, DateOnly DueOn, bool IsDone, bool IsOverdue, DateTimeOffset? CompletedAt, string? CompletedBy,
    string? Note, bool CanComplete);

public record OnboardingStartResult(int Tasks, IReadOnlyList<string> Warnings);

/// <summary>A new starter with onboarding in progress (or finished within the last 30 days).</summary>
public record OnboardingProgressDto(Guid EmployeeId, string EmployeeName, string EmployeeNumber, DateOnly StartDate, string? ManagerName,
    int Done, int Total, int Overdue, DateOnly? NextDue);

public record ProbationReviewDto(Guid Id, ProbationOutcome Recommendation, DateOnly? RecommendedExtendTo, string Comments, string RecommendedBy,
    DateTimeOffset RecommendedAt, ProbationReviewStatus Status, DateOnly? ExtendedTo, string? DecidedBy, DateTimeOffset? DecidedAt, string? DecisionNote,
    DateOnly ProbationEndDate);

/// <summary>An unconfirmed probation ending soon (or already ended), with its review if one has been started.</summary>
public record ProbationDueDto(Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? PositionTitle, string? ManagerName, Guid ContractId,
    DateOnly StartDate, DateOnly ProbationEndDate, int DaysLeft, ProbationReviewDto? Review, IReadOnlyList<ProbationReviewDto> History,
    bool CanRecommend, bool CanDecide);

public record RecommendProbationRequest(ProbationOutcome Recommendation, DateOnly? ExtendTo, string Comments);
public record DecideProbationRequest(ProbationOutcome Outcome, DateOnly? ExtendTo, string? Note);
