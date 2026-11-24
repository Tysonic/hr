using Microsoft.EntityFrameworkCore;
using TyHr.Domain;
using TyHr.Application.Common;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Company;

/// <summary>The signed-in user's own company: its details and settings.</summary>
public class CompanyService(IAppDbContext db, ITenantContext tenantContext)
{
    public async Task<CompanyDto> GetAsync(CancellationToken ct)
    {
        var (tenant, profile) = await LoadAsync(track: false, ct);
        return CompanyDto.From(tenant, profile);
    }

    public async Task<CompanyDto> UpdateAsync(UpdateCompanyRequest request, CancellationToken ct)
    {
        var (tenant, profile) = await LoadAsync(track: true, ct);
        tenant.Rename(request.Name);
        profile.Update(request.LegalName, request.TradingName, request.TaxNumber, request.SocialSecurityNumber,
            request.Email, request.Phone, request.PhysicalAddress, request.PostalAddress, request.FinancialYearStartMonth);
        await db.SaveChangesAsync(ct);
        return CompanyDto.From(tenant, profile);
    }

    private async Task<(Tenant, CompanyProfile)> LoadAsync(bool track, CancellationToken ct)
    {
        var slug = tenantContext.Tenant ?? throw new DomainException("This account is not linked to a company.");
        var tenants = track ? db.Tenants : db.Tenants.AsNoTracking();
        var profiles = track ? db.CompanyProfiles : db.CompanyProfiles.AsNoTracking();
        var tenant = await tenants.SingleOrDefaultAsync(t => t.Slug == slug, ct)
            ?? throw new NotFoundException($"There is no company '{slug}'.");
        // The query filter limits this to the request's company.
        var profile = await profiles.SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException($"{tenant.Name} has no company profile.");
        return (tenant, profile);
    }
}
