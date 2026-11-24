using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Company;
using TyHr.Application.Tenants;
using TyHr.Domain;
using TyHr.Domain.Tenants;

namespace TyHr.Tests;

public class TenantServiceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static CreateTenantRequest Request(string slug, string country = "UG") =>
        new(slug, "Acme Uganda", country, "Jane.Admin@Acme.test", "Jane", "Admin");

    private TenantService Service(Scope scope, FakeProvisioner identity) =>
        new(scope.Db, scope.TenantContext, identity, scope.User, fx.Clock);

    [Fact]
    public async Task Creating_a_company_creates_its_profile_and_invites_its_hr_admin()
    {
        var identity = new FakeProvisioner();
        await using (var platform = fx.NewScope(null, "platform1"))
        {
            var created = await Service(platform, identity).CreateAsync(Request(" Acme-UG "), default);
            Assert.Equal("acme-ug", created.Slug);
            Assert.Equal("UGX", created.Country.CurrencyCode);
            Assert.Equal("jane.admin@acme.test", created.AdminEmail);
            Assert.Equal("platform1", created.CreatedBy);
        }

        Assert.Equal([("acme-ug", "jane.admin@acme.test", Roles.HrAdmin)], identity.Invited);

        await using var company = fx.NewScope("acme-ug");
        var dto = await new CompanyService(company.Db, company.TenantContext).GetAsync(default);
        Assert.Equal("Acme Uganda", dto.LegalName);
        Assert.Equal("UG", dto.Country.Code);
    }

    [Fact]
    public async Task A_taken_company_code_is_refused()
    {
        var slug = await fx.CreateTenantAsync();
        await using var platform = fx.NewScope(null);
        var ex = await Assert.ThrowsAsync<DomainException>(() => Service(platform, new FakeProvisioner()).CreateAsync(Request(slug), default));
        Assert.Contains("already taken", ex.Message);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("-acme")]
    [InlineData("acme_ug")]
    [InlineData("www")]
    public async Task An_invalid_company_code_is_refused(string slug)
    {
        await using var platform = fx.NewScope(null);
        await Assert.ThrowsAsync<DomainException>(() => Service(platform, new FakeProvisioner()).CreateAsync(Request(slug), default));
    }

    [Fact]
    public async Task An_unknown_country_is_refused()
    {
        await using var platform = fx.NewScope(null);
        await Assert.ThrowsAsync<DomainException>(() => Service(platform, new FakeProvisioner()).CreateAsync(Request("acme-zz", "ZZ"), default));
    }

    [Fact]
    public async Task If_the_admin_cannot_be_invited_the_company_is_not_created()
    {
        var identity = new FakeProvisioner { FailWith = new InvalidOperationException("Keycloak is down") };
        await using (var platform = fx.NewScope(null))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(platform, identity).CreateAsync(Request("acme-down"), default));

        await using var check = fx.NewScope("acme-down");
        Assert.False(await check.Db.Tenants.AnyAsync(t => t.Slug == "acme-down"));
        Assert.False(await check.Db.CompanyProfiles.AnyAsync());
    }

    [Fact]
    public async Task A_company_can_be_suspended_and_reactivated()
    {
        var slug = await fx.CreateTenantAsync();
        await using var platform = fx.NewScope(null);
        var service = Service(platform, new FakeProvisioner());

        Assert.Equal(TenantStatus.Suspended, (await service.SuspendAsync(slug, default)).Status);
        await Assert.ThrowsAsync<DomainException>(() => service.SuspendAsync(slug, default));
        Assert.Equal(TenantStatus.Active, (await service.ReactivateAsync(slug, default)).Status);
    }

    [Fact]
    public async Task An_hr_admin_can_update_their_company_details()
    {
        var slug = await fx.CreateTenantAsync();
        await using var scope = fx.NewScope(slug);
        var service = new CompanyService(scope.Db, scope.TenantContext);
        var updated = await service.UpdateAsync(new UpdateCompanyRequest("Renamed Co", "Renamed Co Ltd", "Renamed",
            "1000999999", "nssf-1", "HR@Renamed.test", "+256700000001", "Plot 1, Kampala", null, 7), default);

        Assert.Equal("Renamed Co", updated.Name);
        Assert.Equal("NSSF-1", updated.SocialSecurityNumber);
        Assert.Equal("hr@renamed.test", updated.Email);
        await Assert.ThrowsAsync<DomainException>(() => service.UpdateAsync(new UpdateCompanyRequest("X", "X", null, null, null,
            "x@x.test", null, null, null, 13), default));
    }
}
