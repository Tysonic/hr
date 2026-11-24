namespace TyHr.Domain.Auditing;

/// <summary>
/// One record created, changed or deleted, by whom and when, with the changed fields before and after (as JSON).
/// Written by the DbContext in the same save as the change itself. <see cref="Tenant"/> is null for platform
/// records (tenants). Secret and personal-sensitive fields are recorded as changed without their values.
/// </summary>
public class AuditEntry
{
    private AuditEntry() { }

    public AuditEntry(string? tenant, string userName, DateTimeOffset at, string entityType, string entityId, string action, string? changes)
    {
        Tenant = tenant;
        UserName = userName;
        At = at;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Changes = changes;
    }

    public long Id { get; private set; }
    public string? Tenant { get; private set; }
    public string UserName { get; private set; } = null!;
    public DateTimeOffset At { get; private set; }
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;

    /// <summary>"created", "updated" or "deleted".</summary>
    public string Action { get; private set; } = null!;

    /// <summary>JSON object: field name → [before, after]. Null for deletions.</summary>
    public string? Changes { get; private set; }

    /// <summary>Removes the recorded values (who and when stay) when the person's data is removed under the retention policy.</summary>
    public void Redact() => Changes = Changes == null ? null : "{\"Details\":[\"Removed under the retention policy\",null]}";
}

/// <summary>Keeps a property's values out of the audit trail (only that it changed): secrets and sensitive personal data.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

/// <summary>
/// Stored encrypted (AES-GCM, key from config), so the database alone doesn't give the value away: national IDs,
/// bank and mobile-money account numbers. Implies <see cref="NotAuditedAttribute"/>. Only string properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EncryptedAttribute : Attribute;

/// <summary>Leaves a whole entity type out of the audit trail (e.g. counters).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NoAuditAttribute : Attribute;
