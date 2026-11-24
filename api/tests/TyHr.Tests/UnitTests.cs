using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Tenants;

namespace TyHr.Tests;

public class UnitTests
{
    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-ug-2", true)]
    [InlineData("ab", true)]
    [InlineData("a", false)]
    [InlineData("acme-", false)]
    [InlineData("Acme", false)]
    [InlineData("api", false)]
    [InlineData("acme.ug", false)]
    public void Company_codes_must_be_valid_subdomains(string slug, bool valid) =>
        Assert.Equal(valid, TenantSlug.IsValid(slug));

    [Fact]
    public void Country_packs_round_to_their_currency()
    {
        Assert.Equal(1235m, CountryPacks.Uganda.Round(1234.5m));
        Assert.Equal(1234.57m, CountryPacks.Kenya.Round(1234.567m));
        Assert.False(CountryPacks.Uganda.IsValidAmount(10.5m));
        Assert.True(CountryPacks.Kenya.IsValidAmount(10.5m));
        Assert.Same(CountryPacks.Kenya, CountryPacks.Get("ke"));
        Assert.Throws<DomainException>(() => CountryPacks.Get("ZZ"));
    }

    [Fact]
    public void Keycloak_realm_roles_are_read_from_the_realm_access_claim()
    {
        Assert.Equal(["hr-admin", "employee"], KeycloakRolesTransformation.ReadRealmRoles("""{"roles":["hr-admin","employee","hr-admin"]}"""));
        Assert.Empty(KeycloakRolesTransformation.ReadRealmRoles("not json"));
        Assert.Empty(KeycloakRolesTransformation.ReadRealmRoles(null));
    }

    [Theory]
    // A tyhr-api client role counts; MyBiz's roles don't.
    [InlineData("""{"tyhr-api":{"roles":["hr-admin"]},"mybiz-web":{"roles":["employee"]}}""", """{"roles":["Admin"]}""", "demo", null, "hr-admin")]
    [InlineData("""{"mybiz-web":{"roles":["hr-admin"]}}""", """{"roles":["hr-admin"]}""", "demo", null, "")]
    // The realm's PlatformAdmin becomes platform-admin, only for the operator's company when one is set.
    [InlineData(null, """{"roles":["PlatformAdmin"]}""", "tysoft", null, "platform-admin")]
    [InlineData(null, """{"roles":["PlatformAdmin"]}""", "tysoft", "tysoft", "platform-admin")]
    [InlineData(null, """{"roles":["PlatformAdmin"]}""", "acme", "tysoft", "")]
    // Only TyHR's own company roles are taken from the client.
    [InlineData("""{"tyhr-api":{"roles":["platform-admin","employee"]}}""", null, "demo", null, "employee")]
    public async Task Tyhr_roles_come_from_the_tyhr_api_client_and_the_realm_platform_role(
        string? resourceAccess, string? realmAccess, string tenant, string? platformTenant, string expected)
    {
        var claims = new List<System.Security.Claims.Claim> { new("tenant", tenant) };
        if (resourceAccess != null) claims.Add(new("resource_access", resourceAccess));
        if (realmAccess != null) claims.Add(new("realm_access", realmAccess));
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "Bearer"));
        var transformation = new KeycloakRolesTransformation(
            Microsoft.Extensions.Options.Options.Create(new RoleOptions { PlatformTenant = platformTenant }));

        await transformation.TransformAsync(principal);

        Assert.Equal(expected, string.Join(',', Roles.All.Where(principal.IsInRole)));
    }

    [Fact]
    public void Every_permission_names_only_known_roles_and_tenants_are_platform_only()
    {
        Assert.All(Permissions.Policies.Values.SelectMany(r => r), role => Assert.Contains(role, Roles.All));
        Assert.Equal([Roles.PlatformAdmin], Permissions.Policies[Permissions.PlatformTenants]);
        Assert.DoesNotContain(Roles.PlatformAdmin, Permissions.Policies[Permissions.CompanySettings]);
    }
}
