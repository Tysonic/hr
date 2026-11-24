using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TyHr.Application.Common;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Api.Infrastructure;

/// <summary>
/// Sets the request's company (ITenantContext) from the access token's "tenant" claim, after authentication.
/// Fails closed: a claim naming an unknown or suspended company gets a 403, so a request never runs as a company
/// that can't use the system (except a platform admin, who carries on with no company). No claim leaves the tenant
/// null; endpoints that need a company
/// carry <see cref="RequireTenantAttribute"/>.
/// </summary>
public class TenantContextMiddleware(RequestDelegate next)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, AppDbContext db, IMemoryCache cache)
    {
        var claim = context.User.Identity?.IsAuthenticated == true ? context.User.FindFirst(TenantSlug.ClaimType)?.Value : null;
        if (!string.IsNullOrWhiteSpace(claim))
        {
            var slug = TenantSlug.Normalize(claim);
            var status = await cache.GetOrCreateAsync($"tenant-status:{slug}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                return db.Tenants.AsNoTracking().Where(t => t.Slug == slug).Select(t => (TenantStatus?)t.Status).SingleOrDefaultAsync();
            });

            // Platform admins carry the operator's own company as their tenant (shared realm), which needn't be a
            // TyHR customer; they work across companies, so they go on without a company context instead.
            if (status != TenantStatus.Active && context.User.IsInRole(Roles.PlatformAdmin))
            {
                await next(context);
                return;
            }
            if (status != TenantStatus.Active)
            {
                await Results.Problem(statusCode: 403, title: "Not allowed",
                        detail: status == null ? "Your company is not registered on TyHR." : "Your company's TyHR account is suspended.")
                    .ExecuteAsync(context);
                return;
            }
            tenantContext.Tenant = slug;
        }

        await next(context);
    }
}

/// <summary>For endpoints that act on the user's own company: 403 when the user isn't linked to one.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAttribute : Attribute, IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().Tenant;
        if (tenant != null) return next();
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = 403, Title = "Not allowed", Detail = "This account is not linked to a company.",
        }) { StatusCode = 403 };
        return Task.CompletedTask;
    }
}
