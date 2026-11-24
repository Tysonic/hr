using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Countries;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Api.Controllers;

public record MeCompany(string Slug, string Name, CountryDto Country);
public record MeDto(string UserName, string DisplayName, string? Email, IReadOnlyList<string> Roles, MeCompany? Company);

[ApiController]
[Route("api/me")]
public class MeController(ITenantContext tenantContext, AppDbContext db) : ControllerBase
{
    /// <summary>Who is signed in, their TyHR roles, and their company (null for platform admins).</summary>
    [HttpGet]
    public async Task<MeDto> Get(CancellationToken ct)
    {
        var name = User.Identity?.Name ?? "unknown";
        var display = User.FindFirst("name")?.Value
            ?? string.Join(' ', new[] { User.FindFirst("given_name")?.Value, User.FindFirst("family_name")?.Value }.Where(s => !string.IsNullOrEmpty(s)));
        var roles = Roles.All.Where(User.IsInRole).ToList();

        MeCompany? company = null;
        if (tenantContext.Tenant is { } slug)
        {
            var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Slug == slug, ct);
            company = new MeCompany(tenant.Slug, tenant.Name, CountryDto.From(tenant.Country));
        }
        return new MeDto(name, string.IsNullOrWhiteSpace(display) ? name : display, User.FindFirst("email")?.Value, roles, company);
    }
}

[ApiController]
[Route("api/countries")]
public class CountriesController : ControllerBase
{
    /// <summary>The available country packs (statutory rules, currency, formats).</summary>
    [HttpGet]
    public IReadOnlyList<CountryDto> List() => CountryDto.AllPacks();
}
