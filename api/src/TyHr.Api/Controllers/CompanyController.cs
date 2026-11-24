using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Company;

namespace TyHr.Api.Controllers;

[ApiController]
[Route("api/company")]
[RequireTenant]
public class CompanyController(CompanyService company) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Permissions.CompanyView)]
    public Task<CompanyDto> Get(CancellationToken ct) => company.GetAsync(ct);

    [HttpPut]
    [Authorize(Policy = Permissions.CompanySettings)]
    public Task<CompanyDto> Update(UpdateCompanyRequest request, CancellationToken ct) => company.UpdateAsync(request, ct);
}
