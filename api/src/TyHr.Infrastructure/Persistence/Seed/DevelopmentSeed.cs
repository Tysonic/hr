using TyHr.Application.Payroll;
using TyHr.Application.Attendance;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Leave;
using TyHr.Domain.Countries;
using TyHr.Domain.Tenants;

namespace TyHr.Infrastructure.Persistence.Seed;

/// <summary>
/// Development only: a "demo" company (Uganda) for the demo users that deploy/keycloak/setup.sh --dev-users creates
/// with tenant=demo. Creating it here, not through TenantService, avoids inviting an HR admin through Keycloak.
/// </summary>
public static class DevelopmentSeed
{
    public const string DemoTenant = "demo";

    public static async Task RunAsync(AppDbContext db, ITenantContext tenantContext, TimeProvider clock, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(t => t.Slug == DemoTenant, ct))
        {
            // Companies created before leave, attendance or payroll existed: give the demo their set-up once.
            tenantContext.Tenant = DemoTenant;
            try
            {
                if (!await db.LeaveTypes.AnyAsync(ct)) await LeaveDefaults.SeedAsync(db, CountryPacks.Uganda, clock.GetUtcNow().Year, ct);
                if (!await db.WorkSchedules.AnyAsync(ct)) await AttendanceDefaults.SeedAsync(db, ct);
                if (!await db.PayComponents.AnyAsync(ct)) await PayrollDefaults.SeedAsync(db, ct);
            }
            finally
            {
                tenantContext.Tenant = null;
            }
            return;
        }

        db.Tenants.Add(new Tenant(DemoTenant, "Demo Company Ltd", CountryPacks.Uganda, "hradmin1@tyhr.test", "seed", clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        tenantContext.Tenant = DemoTenant;
        try
        {
            db.CompanyProfiles.Add(new CompanyProfile("Demo Company Ltd", "hr@demo.tyhr.test"));
            await db.SaveChangesAsync(ct);
            await LeaveDefaults.SeedAsync(db, CountryPacks.Uganda, clock.GetUtcNow().Year, ct);
            await AttendanceDefaults.SeedAsync(db, ct);
            await PayrollDefaults.SeedAsync(db, ct);
        }
        finally
        {
            tenantContext.Tenant = null;
        }
    }
}
