namespace TyHr.Domain.Onboarding;

/// <summary>Who does an onboarding task. HR can tick off any task; the others are done by the person they resolve to.</summary>
public enum TaskOwner { Hr, It, Manager, NewHire }

/// <summary>A task in a checklist template, due <see cref="DueDays"/> days from the start date (negative is before it).</summary>
public record ChecklistItem(string Title, TaskOwner Owner, int DueDays, string? Description);

/// <summary>A reusable onboarding checklist. One template can be the default, used when a hire starts onboarding without a choice.</summary>
public class OnboardingTemplate : TenantEntity
{
    public static readonly ChecklistItem[] Standard =
    [
        new("Prepare the employment contract for signing", TaskOwner.Hr, -5, null),
        new("Collect ID, tax and social security numbers and bank details", TaskOwner.Hr, 0, null),
        new("Set up email and system accounts", TaskOwner.It, -2, null),
        new("Get a laptop and phone ready", TaskOwner.It, -2, null),
        new("Plan the first week and introduce the team", TaskOwner.Manager, 0, null),
        new("Agree objectives for probation", TaskOwner.Manager, 14, null),
        new("Read the staff handbook and policies", TaskOwner.NewHire, 7, null),
        new("Check your personal details in TyHR", TaskOwner.NewHire, 7, null),
    ];

    private OnboardingTemplate() { }

    public OnboardingTemplate(string name, IReadOnlyList<ChecklistItem> items, bool isDefault) => Update(name, items, isDefault);

    public string Name { get; private set; } = null!;
    public List<ChecklistItem> Items { get; private set; } = [];
    public bool IsDefault { get; private set; }

    public void Update(string name, IReadOnlyList<ChecklistItem> items, bool isDefault)
    {
        Name = Guard.Required(name, "Name");
        if (items.Count == 0) throw new DomainException("Add at least one task.");
        if (items.Count > 50) throw new DomainException("Keep a checklist to 50 tasks.");
        var cleaned = items.Select(i => i with { Title = Guard.Required(i.Title, "Task"), Description = Guard.Optional(i.Description) }).ToList();
        if (cleaned.Any(i => i.Title.Length > 200)) throw new DomainException("Keep task names under 200 characters.");
        if (cleaned.Any(i => i.Description?.Length > 1000)) throw new DomainException("Keep task descriptions under 1,000 characters.");
        if (cleaned.Any(i => i.DueDays is < -90 or > 365)) throw new DomainException("Tasks are due between 90 days before and a year after the start date.");
        Items = cleaned;
        IsDefault = isDefault;
    }

    public void ClearDefault() => IsDefault = false;
}

/// <summary>
/// One onboarding task for a new starter. <see cref="AssigneeId"/> is the employee who does it (their manager, an IT
/// contact or the new hire), or null for HR's own tasks.
/// </summary>
public class OnboardingTask : TenantEntity
{
    private OnboardingTask() { }

    public OnboardingTask(Guid employeeId, ChecklistItem item, DateOnly startDate, Guid? assigneeId)
    {
        EmployeeId = employeeId;
        Title = Guard.Required(item.Title, "Task");
        Description = Guard.Optional(item.Description);
        Owner = item.Owner;
        DueOn = startDate.AddDays(item.DueDays);
        AssigneeId = assigneeId;
    }

    public Guid EmployeeId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TaskOwner Owner { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CompletedBy { get; private set; }
    public string? Note { get; private set; }

    public bool IsDone => CompletedAt != null;

    public void Complete(string by, string? note, DateTimeOffset at)
    {
        if (IsDone) throw new DomainException("This task is already done.");
        CompletedAt = at;
        CompletedBy = by;
        Note = Guard.Optional(note);
        if (Note?.Length > 1000) throw new DomainException("Keep the note under 1,000 characters.");
    }

    public void Reopen()
    {
        if (!IsDone) throw new DomainException("This task isn't done.");
        CompletedAt = null;
        CompletedBy = null;
        Note = null;
    }

    public void Reassign(Guid? assigneeId) => AssigneeId = assigneeId;
}

public enum ProbationOutcome { Confirm, Extend, NotConfirm }
public enum ProbationReviewStatus { AwaitingHr, Confirmed, Extended, NotConfirmed }

/// <summary>
/// The end-of-probation review of a contract: the line manager recommends, then HR decides. Confirming sets the
/// contract's confirmation date; extending moves its probation end, after which a new review is due.
/// </summary>
public class ProbationReview : TenantEntity
{
    private ProbationReview() { }

    public ProbationReview(Guid employeeId, Guid contractId, DateOnly probationEndDate, ProbationOutcome recommendation, DateOnly? extendTo,
        string comments, string by, DateTimeOffset at)
    {
        EmployeeId = employeeId;
        ContractId = contractId;
        ProbationEndDate = probationEndDate;
        Status = ProbationReviewStatus.AwaitingHr;
        Recommend(recommendation, extendTo, comments, by, at);
    }

    public Guid EmployeeId { get; private set; }
    public Guid ContractId { get; private set; }
    /// <summary>The probation end date this review was for.</summary>
    public DateOnly ProbationEndDate { get; private set; }
    public ProbationOutcome Recommendation { get; private set; }
    public DateOnly? RecommendedExtendTo { get; private set; }
    public string Comments { get; private set; } = null!;
    public string RecommendedBy { get; private set; } = null!;
    public DateTimeOffset RecommendedAt { get; private set; }
    public ProbationReviewStatus Status { get; private set; }
    public DateOnly? ExtendedTo { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    public void Recommend(ProbationOutcome recommendation, DateOnly? extendTo, string comments, string by, DateTimeOffset at)
    {
        if (Status != ProbationReviewStatus.AwaitingHr) throw new DomainException("HR has already decided on this probation.");
        if (recommendation == ProbationOutcome.Extend && (extendTo == null || extendTo <= ProbationEndDate))
            throw new DomainException("Give a new probation end date after the current one.");
        Recommendation = recommendation;
        RecommendedExtendTo = recommendation == ProbationOutcome.Extend ? extendTo : null;
        Comments = Guard.Required(comments, "Comments on their performance");
        if (Comments.Length > 5000) throw new DomainException("Keep comments under 5,000 characters.");
        RecommendedBy = by;
        RecommendedAt = at;
    }

    public void Decide(ProbationOutcome outcome, DateOnly? extendTo, string? note, string by, DateTimeOffset at)
    {
        if (Status != ProbationReviewStatus.AwaitingHr) throw new DomainException("This probation has already been decided.");
        if (outcome == ProbationOutcome.NotConfirm && Guard.Optional(note) == null) throw new DomainException("Say why they aren't being confirmed.");
        Status = outcome switch
        {
            ProbationOutcome.Confirm => ProbationReviewStatus.Confirmed,
            ProbationOutcome.Extend => ProbationReviewStatus.Extended,
            _ => ProbationReviewStatus.NotConfirmed,
        };
        ExtendedTo = outcome == ProbationOutcome.Extend ? extendTo : null;
        DecisionNote = Guard.Optional(note);
        DecidedBy = by;
        DecidedAt = at;
    }
}
