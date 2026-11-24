namespace TyHr.Domain.Performance;

public enum CycleStatus { Draft, GoalSetting, Review, Closed }

/// <summary>
/// A performance review period, e.g. "2026 annual review". Launching it makes an <see cref="Appraisal"/> for each
/// employee in scope. It then moves from goal setting to review, and is closed when done. Ratings use a five-point
/// scale whose labels the cycle keeps, so changing them later doesn't change past results.
/// </summary>
public class ReviewCycle : TenantEntity
{
    public static readonly string[] DefaultScale = ["Unsatisfactory", "Needs improvement", "Meets expectations", "Exceeds expectations", "Outstanding"];

    private ReviewCycle() { }

    public ReviewCycle(CycleDetails d, string createdBy, DateTimeOffset at)
    {
        CreatedBy = createdBy;
        CreatedAt = at;
        Status = CycleStatus.Draft;
        Update(d);
    }

    public string Name { get; private set; } = null!;
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public bool SelfReview { get; private set; }
    /// <summary>Whether reviewers may ask colleagues for 360° feedback.</summary>
    public bool PeerFeedback { get; private set; }
    /// <summary>Whether HR calibrates ratings before results go to employees.</summary>
    public bool Calibrate { get; private set; }
    public DateTimeOffset? ResultsReleasedAt { get; private set; }
    public DateOnly? GoalsDue { get; private set; }
    public DateOnly? ReviewsDue { get; private set; }
    public List<string> Scale { get; private set; } = [];
    /// <summary>Departments in scope; empty means everyone.</summary>
    public List<Guid> DepartmentIds { get; private set; } = [];
    public CycleStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LaunchedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public void Update(CycleDetails d)
    {
        if (Status == CycleStatus.Closed) throw new DomainException("This review cycle is closed.");
        Name = Guard.Required(d.Name, "Name");
        if (d.PeriodEnd <= d.PeriodStart) throw new DomainException("The period ends before it starts.");
        if (d.GoalsDue is { } g && d.ReviewsDue is { } r && r < g) throw new DomainException("Reviews can't be due before goals.");
        var scale = (d.Scale is { Count: > 0 } ? d.Scale : DefaultScale).Select(s => s.Trim()).ToList();
        if (scale.Count != 5 || scale.Any(s => s.Length is 0 or > 50)) throw new DomainException("The rating scale needs five labels of up to 50 characters.");
        if (Status != CycleStatus.Draft && (!d.DepartmentIds.ToHashSet().SetEquals(DepartmentIds) || d.SelfReview != SelfReview || d.Calibrate != Calibrate))
            throw new DomainException("Who is reviewed, and whether there's a self-review or calibration, can't change once the cycle has started.");
        PeerFeedback = d.PeerFeedback;
        Calibrate = d.Calibrate;
        PeriodStart = d.PeriodStart;
        PeriodEnd = d.PeriodEnd;
        SelfReview = d.SelfReview;
        GoalsDue = d.GoalsDue;
        ReviewsDue = d.ReviewsDue;
        Scale = scale;
        DepartmentIds = d.DepartmentIds.Distinct().ToList();
    }

    public void Launch(DateTimeOffset at)
    {
        if (Status != CycleStatus.Draft) throw new DomainException("This cycle has already started.");
        Status = CycleStatus.GoalSetting;
        LaunchedAt = at;
    }

    public void StartReviews()
    {
        if (Status != CycleStatus.GoalSetting) throw new DomainException("Reviews start after goal setting.");
        Status = CycleStatus.Review;
    }

    /// <summary>Whether a finished manager review waits for calibration rather than going straight to the employee.</summary>
    public bool HoldsResults => Calibrate && ResultsReleasedAt == null;

    public void ReleaseResults(DateTimeOffset at)
    {
        if (!Calibrate) throw new DomainException("This cycle has no calibration; results go to employees as each review is finished.");
        if (Status != CycleStatus.Review) throw new DomainException("Release results while reviews are open.");
        if (ResultsReleasedAt != null) throw new DomainException("Results have already been released.");
        ResultsReleasedAt = at;
    }

    public void Close(DateTimeOffset at)
    {
        if (Status != CycleStatus.Review) throw new DomainException("Close a cycle once its reviews have started.");
        Status = CycleStatus.Closed;
        ClosedAt = at;
    }
}

public record CycleDetails(string Name, DateOnly PeriodStart, DateOnly PeriodEnd, bool SelfReview, DateOnly? GoalsDue, DateOnly? ReviewsDue,
    IReadOnlyList<string>? Scale, IReadOnlyList<Guid> DepartmentIds, bool PeerFeedback = false, bool Calibrate = false);

public enum AppraisalStatus { GoalSetting, SelfReview, ManagerReview, Calibration, Completed }

/// <summary>
/// One employee's review in a cycle. Goals are drafted by the employee or their manager and agreed by the manager
/// (weights total 100). In the review phase the employee rates themselves (if the cycle has a self-review), then the
/// manager rates each goal; the overall rating is the weighted average of the manager's ratings. The employee then
/// acknowledges it. <see cref="ManagerId"/> is the line manager when the cycle launched (HR can change it); without
/// one, HR does the manager's part.
/// </summary>
public class Appraisal : TenantEntity
{
    private Appraisal() { }

    public Appraisal(Guid cycleId, Guid employeeId, Guid? managerId)
    {
        CycleId = cycleId;
        EmployeeId = employeeId;
        ManagerId = managerId;
        Status = AppraisalStatus.GoalSetting;
    }

    public Guid CycleId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid? ManagerId { get; private set; }
    public AppraisalStatus Status { get; private set; }
    public DateTimeOffset? GoalsSubmittedAt { get; private set; }
    public DateTimeOffset? GoalsAgreedAt { get; private set; }
    public string? GoalsAgreedBy { get; private set; }
    public string? SelfComment { get; private set; }
    public DateTimeOffset? SelfSubmittedAt { get; private set; }
    public string? ManagerComment { get; private set; }
    public DateTimeOffset? ManagerSubmittedAt { get; private set; }
    public string? ManagerSubmittedBy { get; private set; }
    /// <summary>The weighted average of the manager's ratings, 1.0–5.0.</summary>
    public decimal? OverallRating { get; private set; }
    /// <summary>The rating that counts: the overall rating, or HR's calibrated one.</summary>
    public decimal? FinalRating { get; private set; }
    public string? CalibrationNote { get; private set; }
    public string? CalibratedBy { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? EmployeeComment { get; private set; }

    public bool GoalsAgreed => GoalsAgreedAt != null;

    public void ChangeManager(Guid? managerId)
    {
        if (managerId == EmployeeId) throw new DomainException("An employee can't review themselves as their manager.");
        if (Status == AppraisalStatus.Completed) throw new DomainException("This review is complete.");
        ManagerId = managerId;
    }

    public void CheckGoalsEditable()
    {
        if (Status != AppraisalStatus.GoalSetting || GoalsAgreed) throw new DomainException("The goals have been agreed and can't be changed.");
    }

    public void SubmitGoals(IReadOnlyList<Goal> goals, DateTimeOffset at)
    {
        CheckGoalsEditable();
        CheckWeights(goals);
        GoalsSubmittedAt = at;
    }

    /// <summary>The manager agrees the goals. If the cycle is already in review, the appraisal moves straight on to it.</summary>
    public void AgreeGoals(IReadOnlyList<Goal> goals, string by, DateTimeOffset at, ReviewCycle cycle)
    {
        CheckGoalsEditable();
        CheckWeights(goals);
        GoalsAgreedAt = at;
        GoalsAgreedBy = by;
        if (cycle.Status == CycleStatus.Review) StartReview(cycle);
    }

    /// <summary>Sends agreed goals back to drafting, e.g. when the job changed.</summary>
    public void ReopenGoals()
    {
        if (Status != AppraisalStatus.GoalSetting && Status != AppraisalStatus.SelfReview || SelfSubmittedAt != null)
            throw new DomainException("Goals can only be reopened before anyone has rated them.");
        Status = AppraisalStatus.GoalSetting;
        GoalsAgreedAt = null;
        GoalsAgreedBy = null;
        GoalsSubmittedAt = null;
    }

    public void StartReview(ReviewCycle cycle)
    {
        if (Status != AppraisalStatus.GoalSetting || !GoalsAgreed) return;
        Status = cycle.SelfReview ? AppraisalStatus.SelfReview : AppraisalStatus.ManagerReview;
    }

    public void SubmitSelf(IReadOnlyList<Goal> goals, string? comment, DateTimeOffset at)
    {
        if (Status != AppraisalStatus.SelfReview) throw new DomainException("Self-review isn't open.");
        if (goals.Any(g => g.SelfRating == null)) throw new DomainException("Rate yourself on every goal.");
        SelfComment = Guard.Optional(comment);
        if (SelfComment?.Length > 5000) throw new DomainException("Keep comments under 5,000 characters.");
        SelfSubmittedAt = at;
        Status = AppraisalStatus.ManagerReview;
    }

    public void SubmitManager(IReadOnlyList<Goal> goals, string? comment, string by, DateTimeOffset at, ReviewCycle cycle)
    {
        if (Status != AppraisalStatus.ManagerReview) throw new DomainException("This review isn't waiting for the manager.");
        if (goals.Any(g => g.ManagerRating == null)) throw new DomainException("Rate every goal.");
        ManagerComment = Guard.Required(comment, "Overall comments");
        if (ManagerComment.Length > 5000) throw new DomainException("Keep comments under 5,000 characters.");
        OverallRating = Weighted(goals);
        FinalRating = OverallRating;
        ManagerSubmittedAt = at;
        ManagerSubmittedBy = by;
        Status = cycle.HoldsResults ? AppraisalStatus.Calibration : AppraisalStatus.Completed;
    }

    /// <summary>HR sets the final rating in calibration; moving it away from the manager's needs a reason.</summary>
    public void SetFinalRating(decimal rating, string? note, string by)
    {
        if (Status != AppraisalStatus.Calibration) throw new DomainException("Ratings are calibrated after the manager's review and before results are released.");
        if (rating is < 1 or > 5 || decimal.Round(rating, 1) != rating) throw new DomainException("The final rating is from 1.0 to 5.0, to one decimal place.");
        var reason = Guard.Optional(note);
        if (rating != OverallRating && reason == null) throw new DomainException("Say why the rating differs from the manager's.");
        FinalRating = rating;
        CalibrationNote = reason;
        CalibratedBy = by;
    }

    public void Release()
    {
        if (Status == AppraisalStatus.Calibration) Status = AppraisalStatus.Completed;
    }

    public void Acknowledge(string? comment, DateTimeOffset at)
    {
        if (Status != AppraisalStatus.Completed) throw new DomainException("Your review isn't finished yet.");
        if (AcknowledgedAt != null) throw new DomainException("You've already acknowledged this review.");
        EmployeeComment = Guard.Optional(comment);
        if (EmployeeComment?.Length > 5000) throw new DomainException("Keep comments under 5,000 characters.");
        AcknowledgedAt = at;
    }

    public static decimal Weighted(IReadOnlyList<Goal> goals) =>
        Math.Round(goals.Sum(g => g.Weight * (decimal)g.ManagerRating!.Value) / goals.Sum(g => g.Weight), 1, MidpointRounding.AwayFromZero);

    private static void CheckWeights(IReadOnlyList<Goal> goals)
    {
        if (goals.Count == 0) throw new DomainException("Add at least one goal.");
        var total = goals.Sum(g => g.Weight);
        if (total != 100) throw new DomainException($"Goal weights must add up to 100%; they add up to {total}%.");
    }
}

/// <summary>A goal or KPI in an appraisal, with its weight (percent) and the ratings given on it (1–5).</summary>
public class Goal : TenantEntity
{
    private Goal() { }

    public Goal(Guid appraisalId, GoalDetails d, int order)
    {
        AppraisalId = appraisalId;
        Update(d, order);
    }

    public Guid AppraisalId { get; private set; }
    public int Order { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Measure { get; private set; }
    public int Weight { get; private set; }
    public int? SelfRating { get; private set; }
    public string? SelfComment { get; private set; }
    public int? ManagerRating { get; private set; }
    public string? ManagerComment { get; private set; }

    public void Update(GoalDetails d, int order)
    {
        Order = order;
        Title = Guard.Required(d.Title, "Goal");
        if (Title.Length > 300) throw new DomainException("Keep the goal under 300 characters.");
        Measure = Guard.Optional(d.Measure);
        if (Measure?.Length > 1000) throw new DomainException("Keep the measure under 1,000 characters.");
        if (d.Weight is < 1 or > 100) throw new DomainException("A goal's weight is between 1% and 100%.");
        Weight = d.Weight;
    }

    public void RateSelf(int rating, string? comment)
    {
        Check(rating);
        SelfRating = rating;
        SelfComment = Guard.Optional(comment);
    }

    public void RateManager(int rating, string? comment)
    {
        Check(rating);
        ManagerRating = rating;
        ManagerComment = Guard.Optional(comment);
    }

    public void ClearRatings()
    {
        SelfRating = ManagerRating = null;
        SelfComment = ManagerComment = null;
    }

    private static void Check(int rating)
    {
        if (rating is < 1 or > 5) throw new DomainException("Ratings are 1 to 5.");
    }
}

public record GoalDetails(string Title, string? Measure, int Weight);

public enum FeedbackStatus { Requested, Submitted, Declined }

/// <summary>
/// 360° feedback a colleague gives on an appraisal, at the reviewer's or HR's request. Only the reviewer and HR read
/// it; the employee never sees who said what.
/// </summary>
public class PeerFeedback : TenantEntity
{
    private PeerFeedback() { }

    public PeerFeedback(Guid appraisalId, Guid reviewerId, string requestedBy, DateTimeOffset at)
    {
        AppraisalId = appraisalId;
        ReviewerId = reviewerId;
        RequestedBy = requestedBy;
        RequestedAt = at;
        Status = FeedbackStatus.Requested;
    }

    public Guid AppraisalId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public FeedbackStatus Status { get; private set; }
    public int? Rating { get; private set; }
    public string? Strengths { get; private set; }
    public string? Improvements { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public void Submit(int rating, string? strengths, string? improvements, DateTimeOffset at)
    {
        if (Status == FeedbackStatus.Declined) throw new DomainException("You declined this request.");
        if (rating is < 1 or > 5) throw new DomainException("Ratings are 1 to 5.");
        Strengths = Guard.Optional(strengths);
        Improvements = Guard.Optional(improvements);
        if (Strengths == null && Improvements == null) throw new DomainException("Say something about their strengths or what they could improve.");
        if (Strengths?.Length > 3000 || Improvements?.Length > 3000) throw new DomainException("Keep each answer under 3,000 characters.");
        Rating = rating;
        SubmittedAt = at;
        Status = FeedbackStatus.Submitted;
    }

    public void Decline(DateTimeOffset at)
    {
        if (Status != FeedbackStatus.Requested) throw new DomainException("This request has already been answered.");
        Status = FeedbackStatus.Declined;
        SubmittedAt = at;
    }
}

public enum PlanStatus { Active, Succeeded, NotSucceeded, Cancelled }
public enum PlanProgress { OnTrack, AtRisk, OffTrack }

public record PlanObjective(string Objective, string? Measure);

/// <summary>
/// A performance improvement plan: objectives with measures, the support offered, a period, regular check-ins and an
/// outcome. Owned by the employee's manager (or HR); the employee reads it and acknowledges it.
/// </summary>
public class ImprovementPlan : TenantEntity
{
    private ImprovementPlan() { }

    public ImprovementPlan(Guid employeeId, Guid? ownerId, Guid? appraisalId, PlanDetails d, string createdBy, DateTimeOffset at)
    {
        EmployeeId = employeeId;
        AppraisalId = appraisalId;
        CreatedBy = createdBy;
        CreatedAt = at;
        Status = PlanStatus.Active;
        ChangeOwner(ownerId);
        Update(d);
    }

    public Guid EmployeeId { get; private set; }
    public Guid? OwnerId { get; private set; }
    public Guid? AppraisalId { get; private set; }
    public string Reason { get; private set; } = null!;
    public List<PlanObjective> Objectives { get; private set; } = [];
    public string? Support { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public PlanStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public string? EmployeeComment { get; private set; }
    public string? Outcome { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public string? ClosedBy { get; private set; }

    public void Update(PlanDetails d)
    {
        CheckActive();
        Reason = Guard.Required(d.Reason, "Reason");
        if (Reason.Length > 5000) throw new DomainException("Keep the reason under 5,000 characters.");
        var objectives = d.Objectives.Select(o => new PlanObjective(Guard.Required(o.Objective, "Objective"), Guard.Optional(o.Measure))).ToList();
        if (objectives.Count == 0) throw new DomainException("Add at least one objective.");
        if (objectives.Count > 15 || objectives.Any(o => o.Objective.Length > 500 || o.Measure?.Length > 500))
            throw new DomainException("Use up to 15 objectives of up to 500 characters.");
        if (d.EndDate <= d.StartDate) throw new DomainException("The plan ends before it starts.");
        if (d.EndDate > d.StartDate.AddMonths(6)) throw new DomainException("A plan runs for at most six months.");
        Objectives = objectives;
        Support = Guard.Optional(d.Support);
        if (Support?.Length > 3000) throw new DomainException("Keep the support under 3,000 characters.");
        StartDate = d.StartDate;
        EndDate = d.EndDate;
    }

    public void ChangeOwner(Guid? ownerId)
    {
        if (ownerId == EmployeeId) throw new DomainException("Someone else must own their improvement plan.");
        OwnerId = ownerId;
    }

    public void Acknowledge(string? comment, DateTimeOffset at)
    {
        if (AcknowledgedAt != null) throw new DomainException("You've already acknowledged this plan.");
        EmployeeComment = Guard.Optional(comment);
        if (EmployeeComment?.Length > 3000) throw new DomainException("Keep your comment under 3,000 characters.");
        AcknowledgedAt = at;
    }

    public void Close(PlanStatus outcome, string? note, string by, DateTimeOffset at)
    {
        CheckActive();
        if (outcome == PlanStatus.Active) throw new DomainException("Choose how the plan ended.");
        Outcome = Guard.Required(note, "Outcome");
        Status = outcome;
        ClosedBy = by;
        ClosedAt = at;
    }

    public void CheckActive()
    {
        if (Status != PlanStatus.Active) throw new DomainException("This plan has ended.");
    }
}

public record PlanDetails(string Reason, IReadOnlyList<PlanObjective> Objectives, string? Support, DateOnly StartDate, DateOnly EndDate);

/// <summary>A check-in on an improvement plan: how it's going and what was discussed.</summary>
public class PlanCheckIn : TenantEntity
{
    private PlanCheckIn() { }

    public PlanCheckIn(Guid planId, DateOnly on, PlanProgress progress, string note, string by, DateTimeOffset at)
    {
        PlanId = planId;
        On = on;
        Progress = progress;
        Note = Guard.Required(note, "Notes");
        if (Note.Length > 5000) throw new DomainException("Keep notes under 5,000 characters.");
        By = by;
        At = at;
    }

    public Guid PlanId { get; private set; }
    public DateOnly On { get; private set; }
    public PlanProgress Progress { get; private set; }
    public string Note { get; private set; } = null!;
    public string By { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
}

public enum RecommendationStatus { Proposed, Applied, Skipped }

/// <summary>
/// A cycle's salary review: an increase guideline per rating (1–5, percent) and the date raises take effect. Its
/// recommendations become effective-dated salary changes when applied.
/// </summary>
public class SalaryReview : TenantEntity
{
    private SalaryReview() { }

    public SalaryReview(Guid cycleId, IReadOnlyList<decimal> percents, DateOnly effectiveDate)
    {
        CycleId = cycleId;
        Update(percents, effectiveDate);
    }

    public Guid CycleId { get; private set; }
    /// <summary>Increase in percent for ratings 1 to 5 (rounded to the nearest whole rating).</summary>
    public List<decimal> Percents { get; private set; } = [];
    public DateOnly EffectiveDate { get; private set; }

    public void Update(IReadOnlyList<decimal> percents, DateOnly effectiveDate)
    {
        if (percents.Count != 5) throw new DomainException("Give an increase for each of the five ratings.");
        if (percents.Any(p => p is < 0 or > 100)) throw new DomainException("Increases are between 0% and 100%.");
        Percents = percents.Select(p => decimal.Round(p, 2)).ToList();
        EffectiveDate = effectiveDate;
    }

    public decimal PercentFor(decimal rating) => Percents[Math.Clamp((int)Math.Round(rating, MidpointRounding.AwayFromZero), 1, 5) - 1];
}

/// <summary>One employee's recommended raise in a salary review. Pay data: only people who may see pay see it.</summary>
public class SalaryRecommendation : TenantEntity
{
    private SalaryRecommendation() { }

    public SalaryRecommendation(Guid reviewId, Guid employeeId, decimal rating, decimal currentSalary)
    {
        ReviewId = reviewId;
        EmployeeId = employeeId;
        Rating = rating;
        CurrentSalary = currentSalary;
        Status = RecommendationStatus.Proposed;
    }

    public Guid ReviewId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public decimal Rating { get; private set; }
    public decimal CurrentSalary { get; private set; }
    public decimal Percent { get; private set; }
    public decimal ProposedSalary { get; private set; }
    /// <summary>Set by hand rather than from the guideline, so a guideline change leaves it alone.</summary>
    public bool Adjusted { get; private set; }
    public string? Note { get; private set; }
    public RecommendationStatus Status { get; private set; }
    public DateTimeOffset? AppliedAt { get; private set; }
    public string? AppliedBy { get; private set; }

    public void FromGuideline(SalaryReview review, Countries.CountryPack country, decimal currentSalary)
    {
        CheckOpen();
        CurrentSalary = currentSalary;
        if (Adjusted) return;
        Percent = review.PercentFor(Rating);
        ProposedSalary = decimal.Round(CurrentSalary * (1 + Percent / 100), country.CurrencyMinorUnits, MidpointRounding.AwayFromZero);
    }

    public void Adjust(decimal proposed, string? note, Countries.CountryPack country)
    {
        CheckOpen();
        Payroll.Amounts.Check(proposed, country, allowZero: false);
        if (proposed < CurrentSalary) throw new DomainException("A salary review doesn't cut pay; record a reduction as a salary change instead.");
        ProposedSalary = proposed;
        Percent = CurrentSalary == 0 ? 0 : decimal.Round((proposed / CurrentSalary - 1) * 100, 2);
        Note = Guard.Optional(note);
        Adjusted = true;
        Status = RecommendationStatus.Proposed;
    }

    public void Skip(string? note)
    {
        CheckOpen();
        Note = Guard.Optional(note);
        Status = RecommendationStatus.Skipped;
    }

    public void Unskip()
    {
        if (Status == RecommendationStatus.Skipped) Status = RecommendationStatus.Proposed;
    }

    public void Applied(string by, DateTimeOffset at)
    {
        CheckOpen();
        Status = RecommendationStatus.Applied;
        AppliedBy = by;
        AppliedAt = at;
    }

    private void CheckOpen()
    {
        if (Status == RecommendationStatus.Applied) throw new DomainException("This raise has already been applied.");
    }
}
