using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Tenants;

namespace TyHr.Api.Controllers;

[ApiController]
[Route("api/platform/tenants")]
[Authorize(Policy = Permissions.PlatformTenants)]
public class PlatformTenantsController(TenantService tenants) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<TenantDto>> List(CancellationToken ct) => tenants.ListAsync(ct);

    [HttpGet("{slug}")]
    public Task<TenantDto> Get(string slug, CancellationToken ct) => tenants.GetAsync(slug, ct);

    /// <summary>Signs up a company and invites its first HR admin by email.</summary>
    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(CreateTenantRequest request, CancellationToken ct)
    {
        var tenant = await tenants.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { slug = tenant.Slug }, tenant);
    }

    [HttpPost("{slug}/suspend")]
    public Task<TenantDto> Suspend(string slug, CancellationToken ct) => tenants.SuspendAsync(slug, ct);

    [HttpPost("{slug}/reactivate")]
    public Task<TenantDto> Reactivate(string slug, CancellationToken ct) => tenants.ReactivateAsync(slug, ct);
}
