namespace TyHr.Domain.Exits;

public enum ExitType { Resignation, Termination, Dismissal, Redundancy, EndOfContract, Retirement, Death }

/// <summary>Requested: a resignation waiting for HR. InProgress: notice and clearance. Completed: the employee has left.</summary>
public enum ExitStatus { Requested, InProgress, Completed, Withdrawn }

/// <summary>
/// An employee leaving: why, when notice was given, their last working day, clearance and the exit interview.
/// Completing it marks the employee as exited. At most one open exit per employee.
/// </summary>
public class EmployeeExit : TenantEntity
{
    private EmployeeExit() { }

    /// <summary>A resignation the employee hands in; HR accepts it.</summary>
    public static EmployeeExit Resign(Guid employeeId, DateOnly givenOn, DateOnly lastWorkingDay, string? reason, string by, DateTimeOffset at)
    {
        var e = new EmployeeExit { EmployeeId = employeeId, Type = ExitType.Resignation, Status = ExitStatus.Requested, RequestedBy = by, RequestedAt = at };
        e.Schedule(givenOn, lastWorkingDay, false);
        e.SetReason(reason);
        return e;
    }

    /// <summary>An exit HR records directly (including a resignation handed in on paper).</summary>
    public static EmployeeExit Record(Guid employeeId, ExitType type, DateOnly givenOn, DateOnly lastWorkingDay, bool payInLieu, string? reason, string by,
        DateTimeOffset at)
    {
        var e = new EmployeeExit { EmployeeId = employeeId, Type = type, Status = ExitStatus.InProgress, RequestedBy = by, RequestedAt = at, AcceptedBy = by, AcceptedAt = at };
        e.Schedule(givenOn, lastWorkingDay, payInLieu);
        e.SetReason(reason);
        if (type is ExitType.Termination or ExitType.Dismissal or ExitType.Redundancy && e.Reason == null)
            throw new DomainException("Record the reason for ending their employment.");
        return e;
    }

    public Guid EmployeeId { get; private set; }
    public ExitType Type { get; private set; }
    public string? Reason { get; private set; }
    public DateOnly NoticeGivenOn { get; private set; }
    public DateOnly LastWorkingDay { get; private set; }
    /// <summary>Notice not worked is paid instead (part of the final dues).</summary>
    public bool PayInLieu { get; private set; }
    public ExitStatus Status { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? AcceptedBy { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public bool? EligibleForRehire { get; private set; }
    public string? CompletionNote { get; private set; }
    public string? CompletedBy { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? WithdrawnBy { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }
    public string? WithdrawalNote { get; private set; }

    public bool IsOpen => Status is ExitStatus.Requested or ExitStatus.InProgress;

    /// <summary>Removes the free-text reasons and notes when personal data is removed.</summary>
    public void Anonymise() => (Reason, CompletionNote, WithdrawalNote) = (null, null, null);

    public void Accept(DateOnly lastWorkingDay, bool payInLieu, string by, DateTimeOffset at)
    {
        if (Status != ExitStatus.Requested) throw new DomainException("Only a resignation waiting for HR can be accepted.");
        Schedule(NoticeGivenOn, lastWorkingDay, payInLieu);
        Status = ExitStatus.InProgress;
        AcceptedBy = by;
        AcceptedAt = at;
    }

    public void Update(ExitType type, DateOnly givenOn, DateOnly lastWorkingDay, bool payInLieu, string? reason)
    {
        CheckOpen();
        if (Status == ExitStatus.Requested && type != ExitType.Resignation) throw new DomainException("Accept or withdraw the resignation first.");
        Type = type;
        Schedule(givenOn, lastWorkingDay, payInLieu);
        SetReason(reason);
    }

    public void Withdraw(string? note, string by, DateTimeOffset at)
    {
        CheckOpen();
        WithdrawalNote = Guard.Optional(note);
        if (WithdrawalNote?.Length > 2000) throw new DomainException("Keep the note under 2,000 characters.");
        Status = ExitStatus.Withdrawn;
        WithdrawnBy = by;
        WithdrawnAt = at;
    }

    public void Complete(bool eligibleForRehire, string? note, DateOnly today, string by, DateTimeOffset at)
    {
        if (Status != ExitStatus.InProgress) throw new DomainException(Status == ExitStatus.Requested ? "Accept the resignation first." : "This exit isn't open.");
        if (today < LastWorkingDay) throw new DomainException($"Complete the exit on or after their last working day ({LastWorkingDay:d MMM yyyy}).");
        EligibleForRehire = eligibleForRehire;
        CompletionNote = Guard.Optional(note);
        if (CompletionNote?.Length > 2000) throw new DomainException("Keep the note under 2,000 characters.");
        Status = ExitStatus.Completed;
        CompletedBy = by;
        CompletedAt = at;
    }

    private void Schedule(DateOnly givenOn, DateOnly lastWorkingDay, bool payInLieu)
    {
        if (lastWorkingDay < givenOn) throw new DomainException("The last working day can't be before notice was given.");
        if (lastWorkingDay > givenOn.AddMonths(12)) throw new DomainException("The last working day is more than a year after notice was given.");
        NoticeGivenOn = givenOn;
        LastWorkingDay = lastWorkingDay;
        PayInLieu = payInLieu;
    }

    private void SetReason(string? reason)
    {
        Reason = Guard.Optional(reason);
        if (Reason?.Length > 3000) throw new DomainException("Keep the reason under 3,000 characters.");
    }

    private void CheckOpen()
    {
        if (!IsOpen) throw new DomainException("This exit is closed.");
    }
}

/// <summary>Who clears an item. Finance items go to payroll; HR can tick off anything.</summary>
public enum ClearanceOwner { Hr, It, Manager, Finance, Employee }

/// <summary>A clearance step, due <see cref="DueDays"/> days from the last working day (negative is before it).</summary>
public record ClearanceStep(string Title, ClearanceOwner Owner, int DueDays, string? Description)
{
    public static readonly ClearanceStep[] Standard =
    [
        new("Write handover notes for your work", ClearanceOwner.Employee, -5, null),
        new("Confirm the handover is complete", ClearanceOwner.Manager, -1, null),
        new("Collect the laptop, phone and other company property", ClearanceOwner.Manager, 0, null),
        new("Disable email and system accounts", ClearanceOwner.It, 0, "On the evening of the last working day."),
        new("Collect the ID card, access cards and keys", ClearanceOwner.Hr, 0, null),
        new("Settle salary advances, loans and other balances", ClearanceOwner.Finance, -2, null),
        new("Stop medical cover and other benefits", ClearanceOwner.Hr, 0, null),
    ];
}

/// <summary>One clearance step for a leaver. <see cref="AssigneeId"/> is the employee who does it, or null for HR's and finance's.</summary>
public class ClearanceItem : TenantEntity
{
    private ClearanceItem() { }

    public ClearanceItem(Guid exitId, Guid employeeId, ClearanceStep step, DateOnly lastWorkingDay, Guid? assigneeId)
    {
        ExitId = exitId;
        EmployeeId = employeeId;
        Title = Guard.Required(step.Title, "Item");
        if (Title.Length > 200) throw new DomainException("Keep the item under 200 characters.");
        Description = Guard.Optional(step.Description);
        if (Description?.Length > 1000) throw new DomainException("Keep the description under 1,000 characters.");
        if (step.DueDays is < -90 or > 90) throw new DomainException("Items are due within 90 days of the last working day.");
        Owner = step.Owner;
        DueDays = step.DueDays;
        DueOn = lastWorkingDay.AddDays(step.DueDays);
        AssigneeId = assigneeId;
    }

    public Guid ExitId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public ClearanceOwner Owner { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public int DueDays { get; private set; }
    public DateOnly DueOn { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? CompletedBy { get; private set; }
    public string? Note { get; private set; }
    public bool IsDone => CompletedAt != null;

    public void Reschedule(DateOnly lastWorkingDay) => DueOn = lastWorkingDay.AddDays(DueDays);

    public void Complete(string by, string? note, DateTimeOffset at)
    {
        if (IsDone) throw new DomainException("This item is already cleared.");
        Note = Guard.Optional(note);
        if (Note?.Length > 1000) throw new DomainException("Keep the note under 1,000 characters.");
        CompletedAt = at;
        CompletedBy = by;
    }

    public void Reopen()
    {
        if (!IsDone) throw new DomainException("This item isn't cleared.");
        CompletedAt = null;
        CompletedBy = null;
        Note = null;
    }
}

public enum LeavingReason { Pay, CareerGrowth, Management, WorkEnvironment, WorkLifeBalance, Relocation, Studies, Personal, Retirement, Other }

/// <summary>The answers to an exit interview. Confidential to HR: the manager never sees it.</summary>
public record InterviewAnswers(LeavingReason? MainReason, int? RoleRating, int? ManagerRating, int? PayRating, int? GrowthRating, int? CultureRating,
    bool? WouldRecommend, bool? WouldReturn, string? WentWell, string? CouldImprove, string? Comments);

public class ExitInterview : TenantEntity
{
    private ExitInterview() { }

    public ExitInterview(Guid exitId, Guid employeeId)
    {
        ExitId = exitId;
        EmployeeId = employeeId;
    }

    public Guid ExitId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public LeavingReason? MainReason { get; private set; }
    public int? RoleRating { get; private set; }
    public int? ManagerRating { get; private set; }
    public int? PayRating { get; private set; }
    public int? GrowthRating { get; private set; }
    public int? CultureRating { get; private set; }
    public bool? WouldRecommend { get; private set; }
    public bool? WouldReturn { get; private set; }
    public string? WentWell { get; private set; }
    public string? CouldImprove { get; private set; }
    public string? Comments { get; private set; }
    /// <summary>Whether the leaver filled it in themselves, rather than HR recording a conversation.</summary>
    public bool ByEmployee { get; private set; }
    public string? SubmittedBy { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }

    public void Save(InterviewAnswers a, bool byEmployee, string by, DateTimeOffset at)
    {
        if (ByEmployee && !byEmployee) throw new DomainException("The leaver filled in this interview themselves, so it can't be changed.");
        int?[] ratings = [a.RoleRating, a.ManagerRating, a.PayRating, a.GrowthRating, a.CultureRating];
        if (ratings.Any(r => r is < 1 or > 5)) throw new DomainException("Ratings are 1 to 5.");
        string?[] texts = [Guard.Optional(a.WentWell), Guard.Optional(a.CouldImprove), Guard.Optional(a.Comments)];
        if (texts.Any(t => t?.Length > 3000)) throw new DomainException("Keep each answer under 3,000 characters.");
        if (a.MainReason == null) throw new DomainException("Choose the main reason for leaving.");
        MainReason = a.MainReason;
        (RoleRating, ManagerRating, PayRating, GrowthRating, CultureRating) = (a.RoleRating, a.ManagerRating, a.PayRating, a.GrowthRating, a.CultureRating);
        (WouldRecommend, WouldReturn) = (a.WouldRecommend, a.WouldReturn);
        (WentWell, CouldImprove, Comments) = (texts[0], texts[1], texts[2]);
        ByEmployee = byEmployee;
        SubmittedBy = by;
        SubmittedAt = at;
    }
}
