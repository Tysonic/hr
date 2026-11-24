using Microsoft.EntityFrameworkCore;
using TyHr.Domain;
using TyHr.Domain.Countries;

namespace TyHr.Application.Common;

public static class CompanyContext
{
    /// <summary>The request's company slug, or a DomainException for a user with no company.</summary>
    public static string RequireTenant(this ITenantContext tenantContext) =>
        tenantContext.Tenant ?? throw new DomainException("This account is not linked to a company.");

    /// <summary>The country pack of the request's company (formats, currency, statutory rules).</summary>
    public static async Task<CountryPack> CompanyCountryAsync(this IAppDbContext db, ITenantContext tenantContext, CancellationToken ct)
    {
        var slug = tenantContext.RequireTenant();
        var code = await db.Tenants.AsNoTracking().Where(t => t.Slug == slug).Select(t => t.CountryCode).SingleAsync(ct);
        return CountryPacks.Get(code);
    }
}
