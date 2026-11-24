using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;

namespace TyHr.Application.Auditing;

public record AuditEntryDto(long Id, DateTimeOffset At, string UserName, string EntityType, string EntityId, string Action, string? Changes);

/// <summary>
/// Reads the audit trail. Audit entries aren't behind the tenant query filter (platform entries have no tenant),
/// so every query here filters by the request's tenant explicitly: a company user sees only their company's
/// entries, a platform admin only platform entries.
/// </summary>
public class AuditService(IAppDbContext db, ITenantContext tenantContext)
{
    public async Task<PagedResult<AuditEntryDto>> ListAsync(int page, int pageSize, string? entityType, string? entityId, CancellationToken ct)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var tenant = tenantContext.Tenant;
        var query = db.AuditEntries.AsNoTracking().Where(e => e.Tenant == tenant);
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(e => e.EntityType == entityType);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(e => e.EntityId == entityId);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new AuditEntryDto(e.Id, e.At, e.UserName, e.EntityType, e.EntityId, e.Action, e.Changes))
            .ToListAsync(ct);
        return new PagedResult<AuditEntryDto>(items, page, pageSize, total);
    }
}
