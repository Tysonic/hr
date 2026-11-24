using TyHr.Domain.Countries;

namespace TyHr.Domain.Tenants;

public enum TenantStatus { Active, Suspended }

/// <summary>
/// A customer company on the platform. Platform-level (not tenant-owned): only platform admins list and manage
/// these. The company's own details live in <see cref="CompanyProfile"/>, which its HR admins edit.
/// </summary>
public class Tenant
{
    private Tenant() { }

    public Tenant(string slug, string name, CountryPack country, string adminEmail, string createdBy, DateTimeOffset now)
    {
        Slug = TenantSlug.Normalize(slug);
        if (!TenantSlug.IsValid(Slug)) throw new DomainException(TenantSlug.Requirements);
        Name = Guard.Required(name, "Company name");
        CountryCode = country.Code;
        AdminEmail = Guard.Required(adminEmail, "Admin email").ToLowerInvariant();
        Status = TenantStatus.Active;
        CreatedAt = now;
        CreatedBy = createdBy;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();
    public string Slug { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    /// <summary>ISO 3166-1 alpha-2 code of the country pack (statutory rules, currency, holidays). Fixed once created.</summary>
    public string CountryCode { get; private set; } = null!;

    public string AdminEmail { get; private set; } = null!;
    public TenantStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;

    public CountryPack Country => CountryPacks.Get(CountryCode);

    public void Suspend()
    {
        if (Status == TenantStatus.Suspended) throw new DomainException($"{Name} is already suspended.");
        Status = TenantStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status == TenantStatus.Active) throw new DomainException($"{Name} is already active.");
        Status = TenantStatus.Active;
    }

    public void Rename(string name) => Name = Guard.Required(name, "Company name");
}
