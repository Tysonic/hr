using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Auditing;
using TyHr.Application.Common;

namespace TyHr.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = Permissions.AuditView)]
public class AuditController(AuditService audit) : ControllerBase
{
    /// <summary>The audit trail, newest first: the user's company's, or the platform's for a platform admin.</summary>
    [HttpGet]
    public Task<PagedResult<AuditEntryDto>> List(int page = 1, int pageSize = 50, string? entityType = null, string? entityId = null,
        CancellationToken ct = default) => audit.ListAsync(page, pageSize, entityType, entityId, ct);
}
