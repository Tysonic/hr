using TyHr.Domain.Performance;

namespace TyHr.Application.Performance;

public record CycleSummaryDto(Guid Id, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, CycleStatus Status, bool SelfReview, DateOnly? GoalsDue,
    DateOnly? ReviewsDue, int Appraisals, int Completed);

public record AppraisalRowDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? DepartmentName, Guid? ManagerId,
    string? ManagerName, AppraisalStatus Status, bool GoalsAgreed, bool GoalsSubmitted, decimal? OverallRating, decimal? FinalRating, string? CalibrationNote,
    bool Acknowledged, int FeedbackRequested, int FeedbackReceived);

public record CycleDto(Guid Id, string Name, DateOnly PeriodStart, DateOnly PeriodEnd, CycleStatus Status, bool SelfReview, bool PeerFeedback, bool Calibrate,
    DateTimeOffset? ResultsReleasedAt, DateOnly? GoalsDue,
    DateOnly? ReviewsDue, IReadOnlyList<string> Scale, IReadOnlyList<Guid> DepartmentIds, string CreatedBy, DateTimeOffset? LaunchedAt,
    DateTimeOffset? ClosedAt, IReadOnlyList<AppraisalRowDto> Appraisals, IReadOnlyDictionary<int, int> Distribution);

public record CycleActionResult(CycleDto Cycle, IReadOnlyList<string> Warnings);

public record GoalDto(Guid Id, string Title, string? Measure, int Weight, int? SelfRating, string? SelfComment, int? ManagerRating, string? ManagerComment);

/// <summary>
/// An appraisal as the user may see it: self ratings show to the manager and HR once sent, and the manager's to the
/// employee once the review is complete.
/// </summary>
public record AppraisalDto(Guid Id, Guid CycleId, string CycleName, CycleStatus CycleStatus, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly? GoalsDue,
    DateOnly? ReviewsDue, IReadOnlyList<string> Scale, bool CycleHasSelfReview, Guid EmployeeId, string EmployeeName, string? PositionTitle, Guid? ManagerId,
    string? ManagerName, AppraisalStatus Status, IReadOnlyList<GoalDto> Goals, DateTimeOffset? GoalsSubmittedAt, DateTimeOffset? GoalsAgreedAt,
    string? SelfComment, DateTimeOffset? SelfSubmittedAt, string? ManagerComment, DateTimeOffset? ManagerSubmittedAt, decimal? OverallRating,
    decimal? FinalRating, string? CalibrationNote, DateTimeOffset? AcknowledgedAt, string? EmployeeComment, AppraisalRole Role, AppraisalPermissions Can,
    IReadOnlyList<FeedbackDto> Feedback, bool CyclePeerFeedback);

/// <summary>A colleague's feedback, for the reviewer and HR only.</summary>
public record FeedbackDto(Guid Id, Guid ReviewerId, string ReviewerName, FeedbackStatus Status, int? Rating, string? Strengths, string? Improvements,
    DateTimeOffset RequestedAt, DateTimeOffset? SubmittedAt);

public record RequestFeedbackRequest(IReadOnlyList<Guid> ReviewerIds);

/// <summary>A feedback request as the colleague asked sees it.</summary>
public record FeedbackRequestDto(Guid Id, Guid AppraisalId, string EmployeeName, string? PositionTitle, string CycleName, IReadOnlyList<string> Goals,
    IReadOnlyList<string> Scale, FeedbackStatus Status, int? Rating, string? Strengths, string? Improvements, bool Open, DateOnly? DueOn);

public record SubmitFeedbackRequest(int Rating, string? Strengths, string? Improvements);
public record FinalRatingRequest(decimal Rating, string? Note);

public enum AppraisalRole { Employee, Manager, Hr }

public record AppraisalPermissions(bool EditGoals, bool SubmitGoals, bool AgreeGoals, bool ReopenGoals, bool SelfReview, bool ManagerReview, bool Acknowledge,
    bool RequestFeedback, bool Calibrate);

/// <summary>Goals in order; <see cref="Id"/> keeps an existing goal, null adds one, and goals left out are removed.</summary>
public record SaveGoal(Guid? Id, string Title, string? Measure, int Weight);

public record GoalRating(Guid GoalId, int? Rating, string? Comment);

/// <summary>Ratings so far; with <see cref="Submit"/>, sends them on.</summary>
public record SaveReviewRequest(IReadOnlyList<GoalRating> Ratings, string? Comment, bool Submit);

public record AcknowledgeRequest(string? Comment);
public record ChangeManagerRequest(Guid? ManagerId);

public record MyAppraisalsDto(IReadOnlyList<AppraisalListItem> Mine, IReadOnlyList<AppraisalListItem> Team, IReadOnlyList<FeedbackRequestDto> Feedback,
    int MyPlans, int ActionsDue);

public record AppraisalListItem(Guid Id, string CycleName, CycleStatus CycleStatus, string EmployeeName, AppraisalStatus Status, bool GoalsAgreed,
    bool GoalsSubmitted, decimal? OverallRating, bool Acknowledged, DateOnly? DueOn, bool ActionDue);
