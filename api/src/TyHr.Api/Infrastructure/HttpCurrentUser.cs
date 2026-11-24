using TyHr.Application.Common;

namespace TyHr.Api.Infrastructure;

/// <summary>The signed-in user (Keycloak preferred_username, email, TyHR roles); "system" outside a request.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserName =>
        accessor.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name } ? name : "system";

    public string? Email => accessor.HttpContext?.User.FindFirst("email")?.Value?.ToLowerInvariant();

    public bool IsInRole(string role) => accessor.HttpContext?.User.IsInRole(role) == true;
}
