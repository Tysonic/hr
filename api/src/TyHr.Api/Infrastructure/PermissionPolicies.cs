using Microsoft.AspNetCore.Authorization;
using TyHr.Application.Common;

namespace TyHr.Api.Infrastructure;

/// <summary>One authorization policy per permission in <see cref="Permissions"/>.</summary>
public static class PermissionPolicies
{
    public static void AddPolicies(AuthorizationOptions options)
    {
        foreach (var (name, roles) in Permissions.Policies)
            options.AddPolicy(name, p => p.RequireAuthenticatedUser().RequireRole(roles));

        // Everything else (/api/me, /api/countries) needs a signed-in user holding at least one TyHR role.
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Roles.All).Build();
    }
}
