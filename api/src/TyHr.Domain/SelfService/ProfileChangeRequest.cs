using TyHr.Domain.Auditing;

namespace TyHr.Domain.SelfService;

public enum ProfileChangeKind { Personal, Payment, NextOfKin }

public enum ProfileChangeStatus { Pending, Approved, Rejected, Withdrawn }

/// <summary>
/// An employee's request to change their own record, applied only when HR (or payroll, for payment details) approves.
/// <see cref="Before"/> and <see cref="Proposed"/> are JSON snapshots of the section as it was and as asked for; on
/// approval only the fields the employee changed are applied, so edits HR made in the meantime are kept. Both are
/// stored encrypted, as they can hold ID and account numbers, which also keeps them out of the audit trail.
/// </summary>
public class ProfileChangeRequest : TenantEntity
{
    private ProfileChangeRequest() { }

    public ProfileChangeRequest(Guid employeeId, ProfileChangeKind kind, Guid? nextOfKinId, string before, string proposed, string? reason,
        string requestedBy, DateTimeOffset at)
    {
        EmployeeId = employeeId;
        Kind = kind;
        NextOfKinId = nextOfKinId;
        Before = before;
        Proposed = proposed;
        Reason = Guard.Optional(reason);
        RequestedBy = requestedBy;
        RequestedAt = at;
        Status = ProfileChangeStatus.Pending;
    }

    public Guid EmployeeId { get; private set; }
    public ProfileChangeKind Kind { get; private set; }

    /// <summary>For <see cref="ProfileChangeKind.NextOfKin"/>: the contact being changed, or null for a new one.</summary>
    public Guid? NextOfKinId { get; private set; }

    [Encrypted]
    public string Before { get; private set; } = null!;

    [Encrypted]
    public string Proposed { get; private set; } = null!;

    public string? Reason { get; private set; }
    public ProfileChangeStatus Status { get; private set; }
    public string RequestedBy { get; private set; } = null!;
    public DateTimeOffset RequestedAt { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionNote { get; private set; }

    public void Approve(string by, DateTimeOffset at, string? note) => Decide(ProfileChangeStatus.Approved, by, at, note);

    public void Reject(string by, DateTimeOffset at, string? note)
    {
        if (Guard.Optional(note) == null) throw new DomainException("Say why the change is declined, so the employee knows what to fix.");
        Decide(ProfileChangeStatus.Rejected, by, at, note);
    }

    public void Withdraw(string by, DateTimeOffset at) => Decide(ProfileChangeStatus.Withdrawn, by, at, null);

    private void Decide(ProfileChangeStatus status, string by, DateTimeOffset at, string? note)
    {
        if (Status != ProfileChangeStatus.Pending) throw new DomainException("This request has already been dealt with.");
        Status = status;
        DecidedBy = by;
        DecidedAt = at;
        DecisionNote = Guard.Optional(note);
    }
}
