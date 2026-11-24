using Microsoft.EntityFrameworkCore;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

public class TenancyTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_company_only_sees_its_own_rows()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();

        await using var scope = fx.NewScope(a);
        var profiles = await scope.Db.CompanyProfiles.ToListAsync();

        Assert.Single(profiles);
        Assert.Equal(a, profiles[0].Tenant);
        Assert.DoesNotContain(profiles, p => p.Tenant == b);
    }

    [Fact]
    public async Task A_request_without_a_company_sees_no_company_rows()
    {
        await fx.CreateTenantAsync();
        await using var platform = fx.NewScope(null);
        Assert.Empty(await platform.Db.CompanyProfiles.ToListAsync());
    }

    [Fact]
    public async Task New_rows_are_stamped_with_the_requests_company()
    {
        var a = await fx.CreateTenantAsync();
        await using var scope = fx.NewScope(a);
        var profile = await scope.Db.CompanyProfiles.SingleAsync();
        Assert.Equal(a, profile.Tenant);
    }

    [Fact]
    public async Task A_company_row_cannot_be_saved_without_a_company()
    {
        await using var platform = fx.NewScope(null);
        platform.Db.CompanyProfiles.Add(new CompanyProfile("Orphan Ltd", "x@orphan.test"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Another_companys_row_cannot_be_changed_even_when_attached_directly()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();
        CompanyProfile bProfile;
        await using (var bScope = fx.NewScope(b))
            bProfile = await bScope.Db.CompanyProfiles.AsNoTracking().SingleAsync();

        await using var aScope = fx.NewScope(a);
        aScope.Db.Attach(bProfile);
        bProfile.Update("Hijacked", null, null, null, "evil@a.test", null, null, null, 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => aScope.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Document_numbers_count_separately_per_company()
    {
        var a = await fx.CreateTenantAsync();
        var b = await fx.CreateTenantAsync();

        await using var aScope = fx.NewScope(a);
        await using var bScope = fx.NewScope(b);
        var aNumbers = new PostgresDocumentNumbers(aScope.Db);
        var bNumbers = new PostgresDocumentNumbers(bScope.Db);

        Assert.Equal("EMP-0001", await aNumbers.NextAsync("emp"));
        Assert.Equal("EMP-0002", await aNumbers.NextAsync("EMP"));
        Assert.Equal("EMP-0001", await bNumbers.NextAsync("EMP"));
        Assert.Equal("PR-0001", await aNumbers.NextAsync("PR"));
    }

    [Fact]
    public async Task A_rolled_back_transaction_gives_its_number_back()
    {
        var a = await fx.CreateTenantAsync();
        await using var scope = fx.NewScope(a);
        var numbers = new PostgresDocumentNumbers(scope.Db);

        await using (await scope.Db.Database.BeginTransactionAsync())
            Assert.Equal("EMP-0001", await numbers.NextAsync("EMP"));
        // Disposed without commit: rolled back.

        Assert.Equal("EMP-0001", await numbers.NextAsync("EMP"));
    }
}
