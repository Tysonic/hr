using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Auditing;

namespace TyHr.Tests;

public class AuditTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Changes_are_recorded_with_who_when_and_before_and_after()
    {
        var a = await fx.CreateTenantAsync();
        await using (var scope = fx.NewScope(a, user: "hradmin1"))
        {
            var profile = await scope.Db.CompanyProfiles.SingleAsync();
            profile.Update(profile.LegalName, null, "1000123456", null, profile.Email, "+256700000000", null, null, 7);
            await scope.Db.SaveChangesAsync();
        }

        await using var read = fx.NewScope(a);
        var entry = await read.Db.AuditEntries.Where(e => e.Tenant == a && e.Action == "updated").SingleAsync();
        Assert.Equal("hradmin1", entry.UserName);
        Assert.Equal("CompanyProfile", entry.EntityType);
        Assert.Equal(fx.Clock.GetUtcNow(), entry.At);

        var changes = JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(entry.Changes!)!;
        Assert.Equal(JsonValueKind.Null, changes["TaxNumber"][0].ValueKind);
        Assert.Equal("1000123456", changes["TaxNumber"][1].GetString());
        Assert.Equal(7, changes["FinancialYearStartMonth"][1].GetInt32());
        Assert.False(changes.ContainsKey("LegalName"));
    }

    [Fact]
    public async Task Company_creation_is_audited_under_the_platform_and_the_company()
    {
        var a = await fx.CreateTenantAsync();
        await using var read = fx.NewScope(null);
        Assert.True(await read.Db.AuditEntries.AnyAsync(e => e.Tenant == null && e.EntityType == "Tenant" && e.Action == "created"));
        Assert.True(await read.Db.AuditEntries.AnyAsync(e => e.Tenant == a && e.EntityType == "CompanyProfile" && e.Action == "created"));
    }

    [Fact]
    public async Task The_audit_trail_shows_a_company_only_its_own_entries()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();

        await using var scope = fx.NewScope(a);
        var page = await new AuditService(scope.Db, scope.TenantContext).ListAsync(1, 200, null, null, default);

        Assert.NotEmpty(page.Items);
        var ids = page.Items.Select(i => i.Id).ToList();
        Assert.False(await scope.Db.AuditEntries.AnyAsync(e => ids.Contains(e.Id) && e.Tenant != a));
        Assert.DoesNotContain(page.Items, i => i.EntityType == "Tenant");
        _ = b;
    }
}
