using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;

namespace TyHr.Infrastructure.Persistence;

/// <summary>
/// Document numbers per company (PREFIX-0001), counted in the "Sequences" table keyed by (Tenant, Prefix)
/// (ported from MyBiz's DocumentNumbers). Taken inside the caller's transaction: the counter row stays locked until
/// it ends, so two saves can't get the same number, and a rolled-back save gives its number back.
/// </summary>
public class PostgresDocumentNumbers(AppDbContext db) : IDocumentNumbers
{
    public async Task<string> NextAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var tenant = db.CurrentTenant ?? throw new InvalidOperationException("Document numbers need a company (no tenant on this request).");
        prefix = prefix.Trim().ToUpperInvariant();
        var next = (await db.Database.SqlQuery<int>($"""
            INSERT INTO "Sequences" ("Tenant", "Prefix", "Last") VALUES ({tenant}, {prefix}, 1)
            ON CONFLICT ("Tenant", "Prefix") DO UPDATE SET "Last" = "Sequences"."Last" + 1
            RETURNING "Last" AS "Value"
            """).ToListAsync(cancellationToken)).Single();
        return $"{prefix}-{next:0000}";
    }
}
