using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Exits;

namespace TyHr.Application.Exits;

public record FormerEmployeeDto(Guid EmployeeId, string EmployeeNumber, string Name, string? DepartmentName, DateOnly HireDate, DateOnly? LastWorkingDay,
    ExitType? ExitType, bool? EligibleForRehire, DateOnly? KeepUntil, bool Due, DateTimeOffset? AnonymisedAt);

public record ArchiveDto(int RetentionYears, IReadOnlyList<FormerEmployeeDto> People, bool CanRemove);
public record RetentionRequest(int Years);

/// <summary>
/// Former employees and the retention policy: their records stay, read-only, for the company's retention period after
/// their last working day; after that an HR admin removes their personal data, keeping the employment and pay history
/// under the employee number. Removal can't be undone.
/// </summary>
public class ArchiveService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, IFileStore files, TimeProvider clock,
    ILogger<ArchiveService> logger)
{
    public async Task<ArchiveDto> ListAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var profile = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var today = await TodayAsync(ct);
        var people = await db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Exited).ToListAsync(ct);
        var ids = people.Select(p => p.Id).ToList();
        var exits = (await db.EmployeeExits.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Status == ExitStatus.Completed).ToListAsync(ct))
            .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.MaxBy(x => x.LastWorkingDay)!);
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var rows = people.Select(e =>
        {
            var x = exits.GetValueOrDefault(e.Id);
            var until = x?.LastWorkingDay.AddYears(profile.RetentionYears);
            return new FormerEmployeeDto(e.Id, e.EmployeeNumber, e.FullName, e.DepartmentId is { } d ? depts.GetValueOrDefault(d) : null, e.HireDate,
                x?.LastWorkingDay, x?.Type, x?.EligibleForRehire, until, e.AnonymisedAt == null && until < today, e.AnonymisedAt);
        }).OrderByDescending(r => r.Due).ThenByDescending(r => r.LastWorkingDay).ToList();
        return new ArchiveDto(profile.RetentionYears, rows, user.Can(Permissions.RecordsRetention));
    }

    public async Task<ArchiveDto> SetRetentionAsync(int years, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        CheckAllowed();
        var profile = await db.CompanyProfiles.SingleAsync(ct);
        profile.SetRetention(years);
        await db.SaveChangesAsync(ct);
        return await ListAsync(ct);
    }

    /// <summary>
    /// Removes a former employee's personal data once their retention period has passed: identity, contacts and payment
    /// details on the record and payslips, next of kin, documents (and their files), change requests, the exit
    /// interview and free-text exit notes, and the values the audit trail recorded for these.
    /// </summary>
    public async Task<ArchiveDto> AnonymiseAsync(Guid employeeId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        CheckAllowed();
        var row = (await ListAsync(ct)).People.SingleOrDefault(p => p.EmployeeId == employeeId) ?? throw new NotFoundException("There is no such former employee.");
        if (row.AnonymisedAt != null) throw new DomainException("This person's personal data has already been removed.");
        if (row.KeepUntil == null) throw new DomainException("There's no completed exit for this person, so their retention period can't be worked out.");
        if (!row.Due) throw new DomainException($"Their records are kept until {row.KeepUntil:d MMMM yyyy}.");

        var employee = await db.Employees.SingleAsync(e => e.Id == employeeId, ct);
        var name = $"Former employee {employee.EmployeeNumber}";
        var keys = new List<string>();
        var touched = new List<string> { employeeId.ToString() };
        await db.InTransactionAsync(async () =>
        {
            var kin = await db.NextOfKin.Where(k => k.EmployeeId == employeeId).ToListAsync(ct);
            var documents = await db.EmployeeDocuments.Where(d => d.EmployeeId == employeeId).ToListAsync(ct);
            var changes = await db.ProfileChangeRequests.Where(c => c.EmployeeId == employeeId).ToListAsync(ct);
            var exits = await db.EmployeeExits.Where(x => x.EmployeeId == employeeId).ToListAsync(ct);
            var interviews = await db.ExitInterviews.Where(i => i.EmployeeId == employeeId).ToListAsync(ct);
            touched.AddRange(kin.Select(k => k.Id.ToString()).Concat(documents.Select(d => d.Id.ToString())).Concat(changes.Select(c => c.Id.ToString()))
                .Concat(exits.Select(x => x.Id.ToString())).Concat(interviews.Select(i => i.Id.ToString())));
            keys.AddRange(documents.Select(d => d.StorageKey));
            db.NextOfKin.RemoveRange(kin);
            db.EmployeeDocuments.RemoveRange(documents);
            db.ProfileChangeRequests.RemoveRange(changes);
            db.ExitInterviews.RemoveRange(interviews);
            foreach (var x in exits) x.Anonymise();
            foreach (var slip in await db.Payslips.Where(p => p.EmployeeId == employeeId).ToListAsync(ct)) slip.Anonymise(name);
            employee.Anonymise(clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            // The audit trail keeps who changed what and when, but not the values: those included the data just removed.
            // The audit trail isn't filtered by company like other tables, so limit it to this one.
            var tenant = tenantContext.Tenant;
            foreach (var entry in await db.AuditEntries.Where(a => a.Tenant == tenant && touched.Contains(a.EntityId)).ToListAsync(ct)) entry.Redact();
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        foreach (var key in keys)
        {
            try { await files.DeleteAsync(tenantContext.Tenant!, key, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning(e, "Could not delete a removed employee's file."); }
        }
        return await ListAsync(ct);
    }

    private void CheckAllowed()
    {
        if (!user.Can(Permissions.RecordsRetention)) throw new DomainException("Only an HR admin can do this.");
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);
}
