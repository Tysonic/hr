using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;

namespace TyHr.Domain.Recruitment;

public enum RequisitionStatus { Pending, Approved, Rejected, Closed }

/// <summary>
/// A request to hire: raised by a manager or HR, approved by an HR admin (never the person who raised it). Approved
/// requisitions can be advertised as job postings; closing one closes its postings to new applications.
/// </summary>
public class JobRequisition : TenantEntity
{
    private JobRequisition() { }

    public JobRequisition(string number, RequisitionDetails d, CountryPack country, string requestedBy, DateTimeOffset at)
    {
        Number = number;
        Update(d, country);
        RequestedBy = requestedBy;
        RequestedAt = at;
        Status = RequisitionStatus.Pending;
    }

    /// <summary>REQ-0001, per company.</summary>
    public string Number { get; private set; } = null!;

    public string Title { get; private set; } = null!;
    public Guid? DepartmentId { get; private set; }
    public Guid? PositionId { get; private set; }
    public Guid? BranchId { get; private set; }
    public int Openings { get; private set; }
    public ContractType EmploymentType { get; private set; }
    public decimal? SalaryMin { get; private set; }
    public decimal? SalaryMax { get; private set; }
    public DateOnly? TargetStartDate { get; private set; }
    public string Justification { get; private set; } = null!;
    public RequisitionStatus Status { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    public void Update(RequisitionDetails d, CountryPack country)
    {
        if (Status != RequisitionStatus.Pending) throw new DomainException("Only a requisition waiting for approval can be changed.");
        Title = Guard.Required(d.Title, "Job title");
        if (d.Openings is < 1 or > 500) throw new DomainException("Openings must be between 1 and 500.");
        if (d.SalaryMin is { } min) Amounts.Check(min, country, allowZero: false);
        if (d.SalaryMax is { } max) Amounts.Check(max, country, allowZero: false);
        if (d.SalaryMin > d.SalaryMax) throw new DomainException("The lowest salary is above the highest.");
        Justification = Guard.Required(d.Justification, "Reason for hiring");
        DepartmentId = d.DepartmentId;
        PositionId = d.PositionId;
        BranchId = d.BranchId;
        Openings = d.Openings;
        EmploymentType = d.EmploymentType;
        SalaryMin = d.SalaryMin;
        SalaryMax = d.SalaryMax;
        TargetStartDate = d.TargetStartDate;
    }

    public void Approve(string by, DateTimeOffset at, string? note)
    {
        if (by == RequestedBy) throw new DomainException("You raised this requisition, so another HR admin has to approve it.");
        Decide(RequisitionStatus.Approved, by, at, note);
    }

    public void Reject(string by, DateTimeOffset at, string? note)
    {
        if (Guard.Optional(note) == null) throw new DomainException("Say why the requisition is declined.");
        Decide(RequisitionStatus.Rejected, by, at, note);
    }

    public void Close(string by, DateTimeOffset at, string? note)
    {
        if (Status is not (RequisitionStatus.Approved or RequisitionStatus.Pending)) throw new DomainException("This requisition is already closed.");
        Status = RequisitionStatus.Closed;
        DecidedBy ??= by;
        DecidedAt ??= at;
        DecisionNote = Guard.Optional(note) ?? DecisionNote;
    }

    private void Decide(RequisitionStatus status, string by, DateTimeOffset at, string? note)
    {
        if (Status != RequisitionStatus.Pending) throw new DomainException("This requisition has already been decided.");
        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionNote = Guard.Optional(note);
    }
}

public record RequisitionDetails(string Title, Guid? DepartmentId, Guid? PositionId, Guid? BranchId, int Openings, ContractType EmploymentType,
    decimal? SalaryMin, decimal? SalaryMax, DateOnly? TargetStartDate, string Justification);

public enum PostingStatus { Draft, Open, Closed }

/// <summary>An advert for an approved requisition, shown on the company's public careers page while open.</summary>
public class JobPosting : TenantEntity
{
    private JobPosting() { }

    public JobPosting(Guid requisitionId, PostingDetails d, DateOnly today)
    {
        RequisitionId = requisitionId;
        Status = PostingStatus.Draft;
        Update(d, today);
    }

    public Guid RequisitionId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? Location { get; private set; }
    public DateOnly? ClosingDate { get; private set; }
    public bool ShowSalary { get; private set; }
    public PostingStatus Status { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public void Update(PostingDetails d, DateOnly today)
    {
        if (Status == PostingStatus.Closed) throw new DomainException("This posting is closed.");
        Title = Guard.Required(d.Title, "Title");
        if (Title.Length > 200) throw new DomainException("Keep the title under 200 characters.");
        Description = Guard.Required(d.Description, "Description");
        if (Description.Length > 20_000) throw new DomainException("Keep the description under 20,000 characters.");
        if (d.ClosingDate < today) throw new DomainException("The closing date has passed; pick today or later.");
        Location = Guard.Optional(d.Location);
        ClosingDate = d.ClosingDate;
        ShowSalary = d.ShowSalary;
    }

    public void Publish(DateTimeOffset at, DateOnly today)
    {
        if (Status == PostingStatus.Closed) throw new DomainException("A closed posting can't be reopened; post the job again.");
        if (ClosingDate < today) throw new DomainException("The closing date has passed; change it before publishing.");
        Status = PostingStatus.Open;
        PublishedAt ??= at;
    }

    public void Close() => Status = PostingStatus.Closed;

    /// <summary>Taking applications: open and not past its closing date.</summary>
    public bool IsAcceptingOn(DateOnly today) => Status == PostingStatus.Open && (ClosingDate == null || ClosingDate >= today);
}

public record PostingDetails(string Title, string Description, string? Location, DateOnly? ClosingDate, bool ShowSalary);

/// <summary>Where an application is in hiring. Hired is set by turning the candidate into an employee.</summary>
public enum ApplicationStage { Applied, Screening, Interview, Offer, Hired, Rejected, Withdrawn }

public enum ApplicationSource { CareersPage, AddedByHr }

/// <summary>
/// Someone's application for a posting, with their CV in the file store under a key the system makes up. Candidates
/// agree to their details being used for the application (data protection law in UG and KE), recorded as
/// <see cref="ConsentedAt"/>. One application per email per posting.
/// </summary>
public class JobApplication : TenantEntity
{
    public const long MaxCvBytes = 5 * 1024 * 1024;

    private JobApplication() { }

    public JobApplication(Guid postingId, CandidateDetails c, CountryPack country, ApplicationSource source, string? cvFileName, string? cvContentType,
        long cvSize, DateTimeOffset at)
    {
        PostingId = postingId;
        FirstName = Guard.Required(c.FirstName, "First name");
        LastName = Guard.Required(c.LastName, "Last name");
        Email = Guard.Required(c.Email, "Email").ToLowerInvariant();
        if (!Email.Contains('@') || Email.Contains(' ') || Email.Length > 254) throw new DomainException($"'{c.Email}' is not a valid email address.");
        Phone = Guard.Optional(c.Phone) is { } phone ? country.Formats.NormalizePhone(phone, country.PhonePrefix) : null;
        CoverLetter = Guard.Optional(c.CoverLetter);
        if (CoverLetter?.Length > 10_000) throw new DomainException("Keep the cover letter under 10,000 characters.");
        Source = source;
        if (cvFileName != null)
        {
            CvFileName = Path.GetFileName(cvFileName);
            CvContentType = cvContentType;
            CvSizeBytes = cvSize;
            CvStorageKey = $"recruitment/{postingId:N}/{Id:N}";
        }
        Stage = ApplicationStage.Applied;
        AppliedAt = at;
        ConsentedAt = source == ApplicationSource.CareersPage ? at : null;
    }

    public Guid PostingId { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? CoverLetter { get; private set; }
    public ApplicationSource Source { get; private set; }
    public string? CvFileName { get; private set; }
    public string? CvContentType { get; private set; }
    public long CvSizeBytes { get; private set; }
    public string? CvStorageKey { get; private set; }
    public ApplicationStage Stage { get; private set; }

    /// <summary>1–5 stars from screening; null until rated.</summary>
    public int? Rating { get; private set; }

    public DateTimeOffset AppliedAt { get; private set; }
    public DateTimeOffset? ConsentedAt { get; private set; }

    /// <summary>The employee record made from this application once hired.</summary>
    public Guid? EmployeeId { get; private set; }

    public string FullName => $"{FirstName} {LastName}";
    public bool IsActive => Stage is not (ApplicationStage.Hired or ApplicationStage.Rejected or ApplicationStage.Withdrawn);

    public void MoveTo(ApplicationStage stage)
    {
        if (stage == ApplicationStage.Hired) throw new DomainException("Hire a candidate by making them an employee.");
        if (Stage == ApplicationStage.Hired) throw new DomainException($"{FullName} has been hired; their application can't move.");
        if (Stage == stage) throw new DomainException($"{FullName} is already at that stage.");
        Stage = stage;
    }

    public void Rate(int? rating)
    {
        if (rating is < 1 or > 5) throw new DomainException("Ratings are 1 to 5 stars.");
        Rating = rating;
    }

    public void Hired(Guid employeeId)
    {
        if (!IsActive) throw new DomainException($"{FullName}'s application is closed ({Stage}).");
        Stage = ApplicationStage.Hired;
        EmployeeId = employeeId;
    }
}

public record CandidateDetails(string FirstName, string LastName, string Email, string? Phone, string? CoverLetter);

/// <summary>Something that happened to an application: a note, a stage change or a rating, in order.</summary>
[Auditing.NoAudit]
public class ApplicationEvent : TenantEntity
{
    private ApplicationEvent() { }

    public ApplicationEvent(Guid applicationId, string text, ApplicationStage? stage, string by, DateTimeOffset at)
    {
        ApplicationId = applicationId;
        Text = Guard.Required(text, "Note");
        if (Text.Length > 5_000) throw new DomainException("Keep notes under 5,000 characters.");
        Stage = stage;
        By = by;
        At = at;
    }

    public Guid ApplicationId { get; private set; }
    public string Text { get; private set; } = null!;
    public ApplicationStage? Stage { get; private set; }
    public string By { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
}
