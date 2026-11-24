using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Documents;

namespace TyHr.Application.Documents;

/// <summary>
/// Employees' documents. The file is written to the store before the record is saved and removed again if saving
/// fails, so a record never points at a missing file; deleting removes the record first, then the file.
/// </summary>
public class DocumentService(IAppDbContext db, ITenantContext tenantContext, IFileStore files, ICurrentUser user, TimeProvider clock)
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<DocumentDto>> ListAsync(Guid employeeId, CancellationToken ct)
    {
        await EnsureEmployeeAsync(employeeId, ct);
        var documents = await db.EmployeeDocuments.AsNoTracking().Where(d => d.EmployeeId == employeeId)
            .OrderBy(d => d.Category).ThenByDescending(d => d.UploadedAt).ToListAsync(ct);
        return documents.Select(ToDto).ToList();
    }

    public async Task<DocumentDto> UploadAsync(Guid employeeId, DocumentDetails details, string fileName, long size, Stream content,
        CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        await EnsureEmployeeAsync(employeeId, ct);

        // Read the start to check the file really is what its name says, then store all of it.
        var buffered = new MemoryStream();
        await content.CopyToAsync(buffered, ct);
        if (buffered.Length > EmployeeDocument.MaxBytes) throw new DomainException($"Files can be at most {EmployeeDocument.MaxBytes / 1024 / 1024} MB.");
        var contentType = FileTypes.Check(fileName, buffered.GetBuffer().AsSpan(0, (int)Math.Min(16, buffered.Length)));

        var document = new EmployeeDocument(employeeId, details.Title, details.Category, fileName, contentType, buffered.Length,
            details.IssuedOn, details.ExpiresOn, details.Notes, user.UserName, clock.GetUtcNow());
        buffered.Position = 0;
        await files.SaveAsync(tenant, document.StorageKey, buffered, ct);
        try
        {
            db.EmployeeDocuments.Add(document);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await files.DeleteAsync(tenant, document.StorageKey, CancellationToken.None);
            throw;
        }
        return ToDto(document);
    }

    public async Task<DocumentDto> UpdateAsync(Guid employeeId, Guid documentId, DocumentDetails details, CancellationToken ct)
    {
        var document = await FindAsync(employeeId, documentId, ct);
        document.Update(details.Title, details.Category, details.IssuedOn, details.ExpiresOn, details.Notes);
        await db.SaveChangesAsync(ct);
        return ToDto(document);
    }

    public async Task DeleteAsync(Guid employeeId, Guid documentId, CancellationToken ct)
    {
        var document = await FindAsync(employeeId, documentId, ct);
        db.EmployeeDocuments.Remove(document);
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(tenantContext.RequireTenant(), document.StorageKey, CancellationToken.None);
    }

    public async Task<DocumentDownload> DownloadAsync(Guid employeeId, Guid documentId, CancellationToken ct)
    {
        var document = await FindAsync(employeeId, documentId, ct);
        var content = await files.OpenReadAsync(tenantContext.RequireTenant(), document.StorageKey, ct);
        return new DocumentDownload(content, document.FileName, document.ContentType);
    }

    private DocumentDto ToDto(EmployeeDocument d) => new(d.Id, d.EmployeeId, d.Title, d.Category, d.FileName, d.ContentType, d.SizeBytes,
        d.IssuedOn, d.ExpiresOn, d.ExpiresOn is { } e ? e.DayNumber - Today.DayNumber : null, d.Notes, d.UploadedBy, d.UploadedAt);

    private async Task EnsureEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct)) throw new NotFoundException("There is no such employee.");
    }

    private async Task<EmployeeDocument> FindAsync(Guid employeeId, Guid documentId, CancellationToken ct) =>
        await db.EmployeeDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.EmployeeId == employeeId, ct)
        ?? throw new NotFoundException("There is no such document.");
}
