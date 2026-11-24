using TyHr.Domain.Employees;
using TyHr.Domain.Recruitment;

namespace TyHr.Application.Recruitment;

public record PanelistDto(Guid EmployeeId, string Name, bool Submitted);

public record ScorecardDto(Guid Id, Guid InterviewerId, string InterviewerName, IReadOnlyList<CriterionScore> Scores, Recommendation Recommendation,
    string? Comments, decimal Average, DateTimeOffset SubmittedAt);

/// <summary>
/// An interview. <see cref="Scorecards"/> holds everyone's for HR, and for a panellist only once they've sent their own,
/// so nobody is swayed by the others' marks first.
/// </summary>
public record InterviewDto(Guid Id, Guid ApplicationId, string CandidateName, Guid PostingId, string JobTitle, DateTimeOffset StartsAt,
    DateTime LocalStart, int DurationMinutes, string? Location, InterviewStatus Status, IReadOnlyList<PanelistDto> Panel, IReadOnlyList<string> Criteria,
    ScorecardDto? MyScorecard, IReadOnlyList<ScorecardDto> Scorecards, bool OnPanel, string? CvFileName, string? CoverLetter);

/// <summary><see cref="LocalStart"/> is in the company's local time.</summary>
public record ScheduleInterviewRequest(DateTime LocalStart, int DurationMinutes, string? Location, IReadOnlyList<Guid> PanelIds,
    IReadOnlyList<string> Criteria, bool EmailCandidate);

public record SubmitScorecardRequest(IReadOnlyList<CriterionScore> Scores, Recommendation Recommendation, string? Comments);

public record OfferDto(Guid Id, Guid ApplicationId, string CandidateName, string CandidateEmail, string JobTitle, DateOnly StartDate, decimal MonthlySalary,
    ContractType ContractType, DateOnly? ContractEndDate, int ProbationMonths, DateOnly RespondBy, string? ExtraTerms, OfferStatus Status,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? RespondedAt, string? ResponseNote, IReadOnlyList<string> Warnings);

public record SendOfferRequest(bool EmailCandidate);
public record OfferResponseRequest(bool Accepted, string? Note);
public record WithdrawOfferRequest(string? Note);

public record OfferTemplateDto(string Body, bool IsDefault, IReadOnlyList<string> Placeholders);
public record SaveOfferTemplateRequest(string Body);

/// <summary>What HR adds when making the candidate an employee; the rest comes from the application, offer and requisition.</summary>
/// <remarks>With <see cref="Onboarding"/>, onboarding starts once the employee is created.</remarks>
public record HireRequest(Gender Gender, DateOnly? DateOfBirth, string? Nationality, string? WorkEmail, Guid? ManagerId,
    Onboarding.StartOnboardingRequest? Onboarding = null);

public record HireResult(Guid EmployeeId, string EmployeeNumber, bool SalaryRecorded, bool RequisitionFilled, int OnboardingTasks, IReadOnlyList<string> Warnings);

/// <summary>What an offer letter PDF shows.</summary>
public record OfferLetterDocument(string CompanyName, string? CompanyAddress, string? CompanyEmail, string? CompanyPhone, DateOnly Date,
    string CandidateName, string CandidateEmail, string? CandidatePhone, string JobTitle, string Body, string SignedBy);

public interface IOfferLetterRenderer
{
    byte[] Render(OfferLetterDocument document);
}
