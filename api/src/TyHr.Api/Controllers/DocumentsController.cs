using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Domain.Documents;

namespace TyHr.Api.Controllers;

/// <summary>An employee's documents. HR and payroll can read them; HR uploads, edits and removes them.</summary>
[ApiController]
[Route("api/employees/{employeeId:guid}/documents")]
[RequireTenant]
[Authorize(Policy = Permissions.DocumentsView)]
public class DocumentsController(DocumentService documents) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DocumentDto>> List(Guid employeeId, CancellationToken ct) => documents.ListAsync(employeeId, ct);

    /// <summary>multipart/form-data: file, title, category, issuedOn, expiresOn, notes.</summary>
    [HttpPost, Authorize(Policy = Permissions.DocumentsManage)]
    [RequestSizeLimit(EmployeeDocument.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = EmployeeDocument.MaxBytes + 64 * 1024)]
    public async Task<DocumentDto> Upload(Guid employeeId, [FromForm] UploadDocumentForm form, CancellationToken ct)
    {
        if (form.File is null) throw new Domain.DomainException("Choose a file to upload.");
        await using var content = form.File.OpenReadStream();
        return await documents.UploadAsync(employeeId, new DocumentDetails(form.Title, form.Category, form.IssuedOn, form.ExpiresOn, form.Notes),
            form.File.FileName, form.File.Length, content, ct);
    }

    [HttpPut("{documentId:guid}"), Authorize(Policy = Permissions.DocumentsManage)]
    public Task<DocumentDto> Update(Guid employeeId, Guid documentId, DocumentDetails r, CancellationToken ct) =>
        documents.UpdateAsync(employeeId, documentId, r, ct);

    [HttpDelete("{documentId:guid}"), Authorize(Policy = Permissions.DocumentsManage)]
    public async Task<IActionResult> Delete(Guid employeeId, Guid documentId, CancellationToken ct)
    {
        await documents.DeleteAsync(employeeId, documentId, ct);
        return NoContent();
    }

    /// <summary>Always sent as an attachment with the stored content type, never rendered inline on our origin.</summary>
    [HttpGet("{documentId:guid}/file")]
    public async Task<IActionResult> Download(Guid employeeId, Guid documentId, CancellationToken ct)
    {
        var file = await documents.DownloadAsync(employeeId, documentId, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }
}

public class UploadDocumentForm
{
    public IFormFile? File { get; set; }
    public string Title { get; set; } = "";
    public DocumentCategory Category { get; set; } = DocumentCategory.Other;
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Notes { get; set; }
}
