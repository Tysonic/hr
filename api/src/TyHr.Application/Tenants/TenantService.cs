using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Tenants;

/// <summary>Platform admin work: signing up companies and suspending or reactivating them.</summary>
public class TenantService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IIdentityProvisioner identity,
    ICurrentUser user,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken ct)
    {
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        return tenants.Select(TenantDto.From).ToList();
    }

    public async Task<TenantDto> GetAsync(string slug, CancellationToken ct) => TenantDto.From(await FindAsync(slug, ct));

    /// <summary>
    /// Creates the company, its profile, and its first HR admin's sign-in (who is emailed a link to set a password).
    /// All or nothing: if the admin can't be created in Keycloak, the company isn't created either.
    /// </summary>
    public async Task<TenantDto> CreateAsync(CreateTenantRequest request, CancellationToken ct)
    {
        var country = CountryPacks.Get(request.CountryCode);
        var tenant = new Tenant(request.Slug, request.Name, country, request.AdminEmail, user.UserName, clock.GetUtcNow());
        if (string.IsNullOrWhiteSpace(request.AdminFirstName) || string.IsNullOrWhiteSpace(request.AdminLastName))
            throw new DomainException("The HR admin's first and last name are required.");
        if (await db.Tenants.AnyAsync(t => t.Slug == tenant.Slug, ct))
            throw new DomainException($"The company code '{tenant.Slug}' is already taken.");

        return await db.InTransactionAsync(async () =>
        {
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);

            // The profile is tenant-owned: save it as the new tenant so it is stamped (and audited) as theirs.
            var previous = tenantContext.Tenant;
            tenantContext.Tenant = tenant.Slug;
            try
            {
                db.CompanyProfiles.Add(new CompanyProfile(tenant.Name, tenant.AdminEmail));
                await db.SaveChangesAsync(ct);
                await Leave.LeaveDefaults.SeedAsync(db, country, clock.GetUtcNow().Year, ct);
                await Attendance.AttendanceDefaults.SeedAsync(db, ct);
                await Payroll.PayrollDefaults.SeedAsync(db, ct);
            }
            finally
            {
                tenantContext.Tenant = previous;
            }

            // Last, so a Keycloak failure rolls the company back; there is nothing to undo in Keycloak.
            await identity.InviteUserAsync(tenant.Slug, tenant.AdminEmail, request.AdminFirstName.Trim(),
                request.AdminLastName.Trim(), Roles.HrAdmin, ct);
            return TenantDto.From(tenant);
        }, ct);
    }

    public async Task<TenantDto> SuspendAsync(string slug, CancellationToken ct)
    {
        var tenant = await FindAsync(slug, ct, track: true);
        tenant.Suspend();
        await db.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
    }

    public async Task<TenantDto> ReactivateAsync(string slug, CancellationToken ct)
    {
        var tenant = await FindAsync(slug, ct, track: true);
        tenant.Reactivate();
        await db.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
    }

    private async Task<Tenant> FindAsync(string slug, CancellationToken ct, bool track = false)
    {
        var normalized = TenantSlug.Normalize(slug);
        var query = track ? db.Tenants : db.Tenants.AsNoTracking();
        return await query.SingleOrDefaultAsync(t => t.Slug == normalized, ct)
            ?? throw new NotFoundException($"There is no company '{normalized}'.");
    }
}
