using System.Text.RegularExpressions;

namespace TyHr.Domain.Tenants;

/// <summary>
/// Rules for a company's tenant slug (from MyBiz's TenantName). The slug doubles as the company's subdomain
/// (&lt;slug&gt;.&lt;app domain&gt;) and is the "tenant" claim in its users' access tokens, so it must be a valid DNS
/// label and must not clash with hostnames the platform itself uses.
/// </summary>
public static partial class TenantSlug
{
    /// <summary>The access-token claim carrying the user's tenant (Keycloak user attribute "tenant").</summary>
    public const string ClaimType = "tenant";

    public const string Requirements =
        "The company code must be 2-63 lowercase letters, digits or hyphens, must not start or end with a hyphen, and must not be a reserved name.";

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "www", "api", "app", "admin", "auth", "login", "keycloak", "mail", "static", "assets", "cdn", "status",
        "platform", "careers", "hr", "s3", "minio",
    };

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])$")]
    private static partial Regex Pattern();

    /// <summary>Trims and lowercases the value (null becomes empty).</summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    public static bool IsValid(string normalized) => Pattern().IsMatch(normalized) && !Reserved.Contains(normalized);
}
