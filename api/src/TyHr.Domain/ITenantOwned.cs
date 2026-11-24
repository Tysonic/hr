namespace TyHr.Domain;

/// <summary>
/// A row that belongs to one company. The DbContext only shows it to that company (a global query filter on
/// <see cref="Tenant"/>) and stamps the request's tenant on new rows, so services never set it themselves.
/// </summary>
public interface ITenantOwned
{
    string Tenant { get; }
}

/// <summary>Base for tenant-owned entities: a time-ordered Guid key and the owning tenant.</summary>
public abstract class TenantEntity : ITenantOwned
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    /// <summary>Set by the DbContext when the row is first saved.</summary>
    public string Tenant { get; private set; } = null!;
}
