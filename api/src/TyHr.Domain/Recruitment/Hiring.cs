using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Payroll;

namespace TyHr.Domain.Recruitment;

public enum InterviewStatus { Scheduled, Completed, Cancelled }

/// <summary>
/// An interview for an application, with a panel of employees who each send a scorecard against the same criteria.
/// Times are stored in UTC.
/// </summary>
public class Interview : TenantEntity
{
    public static readonly string[] DefaultCriteria = ["Skills for the job", "Experience", "Communication", "Fit with the team"];

    private Interview() { }

    public Interview(Guid applicationId, InterviewDetails d, string createdBy, DateTimeOffset now)
    {
        ApplicationId = applicationId;
        CreatedBy = createdBy;
        Status = InterviewStatus.Scheduled;
        Update(d, now);
    }

    public Guid ApplicationId { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public int DurationMinutes { get; private set; }
    public string? Location { get; private set; }
    public List<Guid> PanelIds { get; private set; } = [];
    public List<string> Criteria { get; private set; } = [];
    public InterviewStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;

    public void Update(InterviewDetails d, DateTimeOffset now)
    {
        if (Status != InterviewStatus.Scheduled) throw new DomainException("Only a scheduled interview can be changed.");
        if (d.StartsAt < now.AddMinutes(-5)) throw new DomainException("The interview time has passed; pick a time from now on.");
        if (d.DurationMinutes is < 10 or > 480) throw new DomainException("An interview lasts between 10 minutes and 8 hours.");
        var panel = d.PanelIds.Distinct().ToList();
        if (panel.Count == 0) throw new DomainException("Add at least one interviewer.");
        if (panel.Count > 10) throw new DomainException("Keep the panel to 10 people.");
        var criteria = d.Criteria.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (criteria.Count == 0) throw new DomainException("Add at least one thing to score.");
        if (criteria.Count > 12 || criteria.Any(c => c.Length > 100)) throw new DomainException("Use at most 12 criteria of up to 100 characters.");
        StartsAt = d.StartsAt.ToUniversalTime();
        DurationMinutes = d.DurationMinutes;
        Location = Guard.Optional(d.Location);
        PanelIds = panel;
        Criteria = criteria;
    }

    public void Complete()
    {
        if (Status == InterviewStatus.Cancelled) throw new DomainException("This interview was cancelled.");
        Status = InterviewStatus.Completed;
    }

    public void Cancel()
    {
        if (Status != InterviewStatus.Scheduled) throw new DomainException("Only a scheduled interview can be cancelled.");
        Status = InterviewStatus.Cancelled;
    }
}

public record InterviewDetails(DateTimeOffset StartsAt, int DurationMinutes, string? Location, IReadOnlyList<Guid> PanelIds, IReadOnlyList<string> Criteria);

public enum Recommendation { StrongNo, No, Yes, StrongYes }

public record CriterionScore(string Criterion, int Score, string? Comment);

/// <summary>One interviewer's marks for an interview: a 1–5 score per criterion and an overall recommendation.</summary>
public class Scorecard : TenantEntity
{
    private Scorecard() { }

    public Scorecard(Guid interviewId, Guid interviewerId, string interviewerName, Interview interview, IReadOnlyList<CriterionScore> scores,
        Recommendation recommendation, string? comments, DateTimeOffset at)
    {
        InterviewId = interviewId;
        InterviewerId = interviewerId;
        InterviewerName = interviewerName;
        Update(interview, scores, recommendation, comments, at);
    }

    public Guid InterviewId { get; private set; }
    public Guid InterviewerId { get; private set; }
    public string InterviewerName { get; private set; } = null!;
    public List<CriterionScore> Scores { get; private set; } = [];
    public Recommendation Recommendation { get; private set; }
    public string? Comments { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }

    public decimal Average => Scores.Count == 0 ? 0 : Math.Round(Scores.Average(s => (decimal)s.Score), 1);

    public void Update(Interview interview, IReadOnlyList<CriterionScore> scores, Recommendation recommendation, string? comments, DateTimeOffset at)
    {
        if (interview.Status == InterviewStatus.Cancelled) throw new DomainException("This interview was cancelled.");
        var byCriterion = scores.ToDictionary(s => s.Criterion.Trim(), StringComparer.OrdinalIgnoreCase);
        var missing = interview.Criteria.Where(c => !byCriterion.ContainsKey(c)).ToList();
        if (missing.Count > 0) throw new DomainException($"Score every criterion: {string.Join(", ", missing)}.");
        if (scores.Any(s => s.Score is < 1 or > 5)) throw new DomainException("Scores are 1 to 5.");
        Scores = interview.Criteria.Select(c => byCriterion[c] with { Criterion = c, Comment = Guard.Optional(byCriterion[c].Comment) }).ToList();
        Recommendation = recommendation;
        Comments = Guard.Optional(comments);
        if (Comments?.Length > 5_000) throw new DomainException("Keep comments under 5,000 characters.");
        SubmittedAt = at;
    }
}

public enum OfferStatus { Draft, Sent, Accepted, Declined, Withdrawn }

/// <summary>A job offer to a candidate; its letter is the company's template filled in with these terms.</summary>
public class JobOffer : TenantEntity
{
    private JobOffer() { }

    public JobOffer(Guid applicationId, OfferTerms t, CountryPack country, string createdBy, DateTimeOffset at, DateOnly today)
    {
        ApplicationId = applicationId;
        CreatedBy = createdBy;
        CreatedAt = at;
        Status = OfferStatus.Draft;
        Update(t, country, today);
    }

    public Guid ApplicationId { get; private set; }
    public string JobTitle { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public decimal MonthlySalary { get; private set; }
    public ContractType ContractType { get; private set; }
    public DateOnly? ContractEndDate { get; private set; }
    public int ProbationMonths { get; private set; }
    public DateOnly RespondBy { get; private set; }
    public string? ExtraTerms { get; private set; }
    public OfferStatus Status { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }
    public string? ResponseNote { get; private set; }

    public void Update(OfferTerms t, CountryPack country, DateOnly today)
    {
        if (Status != OfferStatus.Draft) throw new DomainException("Only a draft offer can be changed; withdraw it and make a new one.");
        JobTitle = Guard.Required(t.JobTitle, "Job title");
        Amounts.Check(t.MonthlySalary, country, allowZero: false);
        if (t.StartDate < today) throw new DomainException("The start date has passed.");
        if (t.RespondBy < today) throw new DomainException("The reply-by date has passed.");
        if (t.RespondBy > t.StartDate) throw new DomainException("Ask for a reply before the start date.");
        if (t.ProbationMonths is < 0 or > 12) throw new DomainException("Probation is between 0 and 12 months.");
        if (t.ContractType == ContractType.Permanent && t.ContractEndDate != null) throw new DomainException("A permanent contract has no end date.");
        if (t.ContractType != ContractType.Permanent && t.ContractEndDate == null) throw new DomainException("Give the contract's end date.");
        if (t.ContractEndDate <= t.StartDate) throw new DomainException("The contract ends before it starts.");
        StartDate = t.StartDate;
        MonthlySalary = t.MonthlySalary;
        ContractType = t.ContractType;
        ContractEndDate = t.ContractEndDate;
        ProbationMonths = t.ProbationMonths;
        RespondBy = t.RespondBy;
        ExtraTerms = Guard.Optional(t.ExtraTerms);
        if (ExtraTerms?.Length > 10_000) throw new DomainException("Keep the extra terms under 10,000 characters.");
    }

    public void Sent(DateTimeOffset at)
    {
        if (Status != OfferStatus.Draft) throw new DomainException("This offer has already been sent.");
        Status = OfferStatus.Sent;
        SentAt = at;
    }

    public void Respond(bool accepted, string? note, DateTimeOffset at)
    {
        if (Status != OfferStatus.Sent) throw new DomainException("Record the candidate's answer once the offer has been sent.");
        Status = accepted ? OfferStatus.Accepted : OfferStatus.Declined;
        ResponseNote = Guard.Optional(note);
        RespondedAt = at;
    }

    public void Withdraw(string? note, DateTimeOffset at)
    {
        if (Status is not (OfferStatus.Draft or OfferStatus.Sent)) throw new DomainException("This offer can no longer be withdrawn.");
        Status = OfferStatus.Withdrawn;
        ResponseNote = Guard.Optional(note);
        RespondedAt = at;
    }

    public bool IsLive => Status is OfferStatus.Draft or OfferStatus.Sent or OfferStatus.Accepted;
}

public record OfferTerms(string JobTitle, DateOnly StartDate, decimal MonthlySalary, ContractType ContractType, DateOnly? ContractEndDate,
    int ProbationMonths, DateOnly RespondBy, string? ExtraTerms);

/// <summary>
/// The company's offer letter wording. Placeholders in double braces are filled from the offer, e.g.
/// {{CandidateName}}; <see cref="Placeholders"/> lists them.
/// </summary>
public class OfferLetterTemplate : TenantEntity
{
    public static readonly string[] Placeholders =
        ["CandidateName", "FirstName", "JobTitle", "StartDate", "Salary", "ContractType", "ContractEndDate", "ProbationMonths", "RespondBy", "CompanyName"];

    public const string Default = """
        Dear {{FirstName}},

        We are pleased to offer you the position of {{JobTitle}} at {{CompanyName}}, starting on {{StartDate}}.

        Your gross salary will be {{Salary}} a month, paid monthly in arrears, less the deductions required by law. Your employment is {{ContractType}}{{ContractEndDate}}, and the first {{ProbationMonths}} months are a probation period.

        Please confirm your acceptance by signing and returning a copy of this letter by {{RespondBy}}. This offer lapses if we do not hear from you by then.

        We look forward to welcoming you.
        """;

    private OfferLetterTemplate() { }

    public OfferLetterTemplate(string body) => Update(body);

    public string Body { get; private set; } = null!;

    public void Update(string body)
    {
        Body = Guard.Required(body, "Letter");
        if (Body.Length > 20_000) throw new DomainException("Keep the letter under 20,000 characters.");
        var unknown = System.Text.RegularExpressions.Regex.Matches(Body, @"\{\{\s*(\w+)\s*\}\}").Select(m => m.Groups[1].Value)
            .Where(p => !Placeholders.Contains(p)).Distinct().ToList();
        if (unknown.Count > 0) throw new DomainException($"Unknown placeholder {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}. Use: {string.Join(", ", Placeholders.Select(p => "{{" + p + "}}"))}.");
    }
}
