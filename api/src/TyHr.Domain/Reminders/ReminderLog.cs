using TyHr.Domain.Auditing;

namespace TyHr.Domain.Reminders;

public enum ReminderKind { DocumentExpiry, ContractEnd, ProbationEnd }

/// <summary>
/// That a reminder about one item at one milestone (30 days before, 7 days before, the day itself) has been sent, so
/// the daily run never sends it twice. Changing the item's date starts its milestones again (the date is part of the key).
/// </summary>
[NoAudit]
public class ReminderLog : TenantEntity
{
    private ReminderLog() { }

    public ReminderLog(ReminderKind kind, Guid itemId, DateOnly dueOn, int daysBefore, DateTimeOffset sentAt)
    {
        Kind = kind;
        ItemId = itemId;
        DueOn = dueOn;
        DaysBefore = daysBefore;
        SentAt = sentAt;
    }

    public ReminderKind Kind { get; private set; }
    public Guid ItemId { get; private set; }
    public DateOnly DueOn { get; private set; }
    public int DaysBefore { get; private set; }
    public DateTimeOffset SentAt { get; private set; }
}
