namespace TyHr.Domain.Leave;

public enum LeaveStatus { Pending, Approved, Rejected, Cancelled }

/// <summary>
/// A request for days off. <see cref="Days"/> is worked out when it is made (working days, or calendar days for some
/// types, without public holidays) and kept, so later holiday edits don't change what was approved.
/// </summary>
public class LeaveRequest : TenantEntity
{
    private LeaveRequest() { }

    public LeaveRequest(Guid employeeId, Guid leaveTypeId, DateOnly startDate, DateOnly endDate, bool halfDay, decimal days, string? reason,
        Guid? documentId, string requestedBy, DateTimeOffset requestedAt)
    {
        if (endDate < startDate) throw new DomainException("The leave ends before it starts.");
        if (halfDay && endDate != startDate) throw new DomainException("A half day is a single day: start and end on the same date.");
        if (days <= 0) throw new DomainException("Those dates are all weekends or public holidays, so no leave days are needed.");
        if (endDate.DayNumber - startDate.DayNumber > 366) throw new DomainException("Leave can't be longer than a year; split it into requests.");
        EmployeeId = employeeId;
        LeaveTypeId = leaveTypeId;
        StartDate = startDate;
        EndDate = endDate;
        HalfDay = halfDay;
        Days = days;
        Reason = Guard.Optional(reason);
        DocumentId = documentId;
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
        Status = LeaveStatus.Pending;
    }

    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public bool HalfDay { get; private set; }
    public decimal Days { get; private set; }
    public string? Reason { get; private set; }

    /// <summary>A supporting document (e.g. medical certificate) filed on the employee's record.</summary>
    public Guid? DocumentId { get; private set; }

    public LeaveStatus Status { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    /// <summary>Pending and approved requests hold days and block overlapping requests.</summary>
    public bool IsLive => Status is LeaveStatus.Pending or LeaveStatus.Approved;

    public void Approve(string by, DateTimeOffset at, string? note)
    {
        if (Status != LeaveStatus.Pending) throw new DomainException($"This request is already {Status.ToString().ToLowerInvariant()}.");
        Decide(LeaveStatus.Approved, by, at, note);
    }

    public void Reject(string by, DateTimeOffset at, string? note)
    {
        if (Status != LeaveStatus.Pending) throw new DomainException($"This request is already {Status.ToString().ToLowerInvariant()}.");
        if (Guard.Optional(note) == null) throw new DomainException("Say why the request is turned down.");
        Decide(LeaveStatus.Rejected, by, at, note);
    }

    /// <summary>Withdraws a pending request, or takes back approved leave (its days return to the balance).</summary>
    public void Cancel(string by, DateTimeOffset at, string? note)
    {
        if (!IsLive) throw new DomainException($"This request is already {Status.ToString().ToLowerInvariant()}.");
        Decide(LeaveStatus.Cancelled, by, at, note);
    }

    public void AttachDocument(Guid documentId) => DocumentId = documentId;

    private void Decide(LeaveStatus status, string by, DateTimeOffset at, string? note)
    {
        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionNote = Guard.Optional(note);
    }
}

/// <summary>
/// A manual change to a balance in one leave year: an opening balance when moving to TyHR, days granted or taken off
/// by agreement, or a correction. Positive adds days.
/// </summary>
public class LeaveAdjustment : TenantEntity
{
    private LeaveAdjustment() { }

    public LeaveAdjustment(Guid employeeId, Guid leaveTypeId, int leaveYear, decimal days, string reason, string recordedBy, DateTimeOffset recordedAt)
    {
        if (days == 0) throw new DomainException("An adjustment needs a number of days.");
        if (days % 0.5m != 0) throw new DomainException("Adjust in whole or half days.");
        EmployeeId = employeeId;
        LeaveTypeId = leaveTypeId;
        LeaveYear = leaveYear;
        Days = days;
        Reason = Guard.Required(reason, "Reason");
        RecordedBy = recordedBy;
        RecordedAt = recordedAt;
    }

    public Guid EmployeeId { get; private set; }
    public Guid LeaveTypeId { get; private set; }

    /// <summary>The leave year, named by the calendar year it starts in.</summary>
    public int LeaveYear { get; private set; }

    public decimal Days { get; private set; }
    public string Reason { get; private set; } = null!;
    public string RecordedBy { get; private set; } = null!;
    public DateTimeOffset RecordedAt { get; private set; }
}
