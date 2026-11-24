using TyHr.Application.Countries;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Tenants;

public record TenantDto(Guid Id, string Slug, string Name, CountryDto Country, string AdminEmail, TenantStatus Status,
    DateTimeOffset CreatedAt, string CreatedBy)
{
    public static TenantDto From(Tenant t) =>
        new(t.Id, t.Slug, t.Name, CountryDto.From(t.Country), t.AdminEmail, t.Status, t.CreatedAt, t.CreatedBy);
}

/// <summary>A new customer company and the HR admin who will run it.</summary>
public record CreateTenantRequest(
    string Slug,
    string Name,
    string CountryCode,
    string AdminEmail,
    string AdminFirstName,
    string AdminLastName);
