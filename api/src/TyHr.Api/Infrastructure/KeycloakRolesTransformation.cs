using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Domain.Tenants;

namespace TyHr.Api.Infrastructure;

/// <summary>How TyHR reads roles from the shared Tysoft realm (deploy/keycloak/setup.sh).</summary>
public class RoleOptions
{
    /// <summary>The client holding TyHR's company roles: "resource_access": { "tyhr-api": { "roles": [...] } }.</summary>
    public string ApiClientId { get; set; } = "tyhr-api";

    /// <summary>The realm role of the people who run the Tysoft platform (Tycount's), which TyHR treats as platform-admin.</summary>
    public string PlatformAdminRealmRole { get; set; } = "PlatformAdmin";

    /// <summary>
    /// The operator's own company, as in Tycount's Platform:Tenant: the platform role only counts for users whose tenant
    /// is this one (or who have none). Empty means anywhere.
    /// </summary>
    public string? PlatformTenant { get; set; }
}

/// <summary>
/// Keycloak puts client roles in a JSON claim, "resource_access": { "tyhr-api": { "roles": [ ... ] } }, and realm roles
/// in "realm_access": { "roles": [ ... ] }. This adds TyHR's roles as role claims so [Authorize(Policy)] and IsInRole
/// work: each tyhr-api client role as itself, and the realm's platform role as platform-admin. Other apps' roles
/// (Tycount's Admin...) are ignored.
/// </summary>
public class KeycloakRolesTransformation(IOptions<RoleOptions> options) : IClaimsTransformation
{
    public const string RealmAccessClaim = "realm_access";
    public const string ResourceAccessClaim = "resource_access";

    private readonly RoleOptions _options = options.Value;

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity) return Task.FromResult(principal);

        var roles = ReadClientRoles(identity.FindFirst(ResourceAccessClaim)?.Value, _options.ApiClientId)
            .Where(Roles.Company.Contains)
            .ToList();
        if (ReadRealmRoles(identity.FindFirst(RealmAccessClaim)?.Value).Contains(_options.PlatformAdminRealmRole)
            && IsOperator(identity.FindFirst(TenantSlug.ClaimType)?.Value))
            roles.Add(Roles.PlatformAdmin);

        foreach (var role in roles)
            if (!identity.HasClaim(identity.RoleClaimType, role))
                identity.AddClaim(new Claim(identity.RoleClaimType, role));

        return Task.FromResult(principal);
    }

    private bool IsOperator(string? tenant) =>
        string.IsNullOrWhiteSpace(_options.PlatformTenant) || string.IsNullOrWhiteSpace(tenant)
        || TenantSlug.Normalize(tenant) == TenantSlug.Normalize(_options.PlatformTenant);

    /// <summary>The roles in a "realm_access" claim.</summary>
    public static IReadOnlyList<string> ReadRealmRoles(string? realmAccessJson) => ReadRoles(realmAccessJson, null);

    /// <summary>The roles for <paramref name="clientId"/> in a "resource_access" claim.</summary>
    public static IReadOnlyList<string> ReadClientRoles(string? resourceAccessJson, string clientId) =>
        ReadRoles(resourceAccessJson, clientId);

    private static IReadOnlyList<string> ReadRoles(string? json, string? clientId)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var holder = doc.RootElement;
            if (clientId != null && (holder.ValueKind != JsonValueKind.Object || !holder.TryGetProperty(clientId, out holder)))
                return [];
            if (holder.ValueKind != JsonValueKind.Object || !holder.TryGetProperty("roles", out var roles)
                || roles.ValueKind != JsonValueKind.Array) return [];
            return roles.EnumerateArray()
                .Where(r => r.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(r.GetString()))
                .Select(r => r.GetString()!)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
