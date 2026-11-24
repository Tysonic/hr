using TyHr.Domain.Employees;
using TyHr.Domain.Recruitment;

namespace TyHr.Application.Recruitment;

public record RequisitionDto(Guid Id, string Number, string Title, Guid? DepartmentId, string? DepartmentName, Guid? PositionId, string? PositionTitle,
    Guid? BranchId, string? BranchName, int Openings, ContractType EmploymentType, decimal? SalaryMin, decimal? SalaryMax, DateOnly? TargetStartDate,
    string Justification, RequisitionStatus Status, string RequestedBy, DateTimeOffset RequestedAt, string? DecidedBy, DateTimeOffset? DecidedAt,
    string? DecisionNote, int Postings, int Hired, bool CanEdit, bool CanDecide, bool CanClose);

public record DecideRequest(string? Note);

public record PostingDto(Guid Id, Guid RequisitionId, string RequisitionNumber, string Title, string Description, string? Location,
    DateOnly? ClosingDate, bool ShowSalary, PostingStatus Status, DateTimeOffset? PublishedAt, bool AcceptingApplications,
    IReadOnlyDictionary<ApplicationStage, int> Stages, int Applications);

public record CreatePostingRequest(Guid RequisitionId, PostingDetails Details);

public record ApplicationSummaryDto(Guid Id, Guid PostingId, string Name, string Email, string? Phone, ApplicationStage Stage, int? Rating,
    ApplicationSource Source, DateTimeOffset AppliedAt, bool HasCv, Guid? EmployeeId);

public record ApplicationEventDto(string Text, ApplicationStage? Stage, string By, DateTimeOffset At);

public record ApplicationDto(Guid Id, Guid PostingId, string PostingTitle, string FirstName, string LastName, string Email, string? Phone,
    string? CoverLetter, ApplicationStage Stage, int? Rating, ApplicationSource Source, DateTimeOffset AppliedAt, DateTimeOffset? ConsentedAt,
    string? CvFileName, long CvSizeBytes, Guid? EmployeeId, IReadOnlyList<ApplicationEventDto> Events);

public record MoveApplicationRequest(ApplicationStage Stage, string? Note);
public record RateApplicationRequest(int? Rating);
public record AddNoteRequest(string Text);

public record CvDownload(Stream Content, string FileName, string ContentType);

// Public careers page: only what a candidate should see.

public record CareersDto(string CompanyName, IReadOnlyList<PublicPostingDto> Jobs);

public record PublicPostingDto(Guid Id, string Title, string Description, string? Location, string? DepartmentName, ContractType EmploymentType,
    DateOnly? ClosingDate, string? Salary, DateTimeOffset? PublishedAt);
