using TyHr.Domain.Documents;

namespace TyHr.Application.Documents;

/// <summary><see cref="DaysToExpiry"/>: negative once expired; null without an expiry date.</summary>
public record DocumentDto(Guid Id, Guid EmployeeId, string Title, DocumentCategory Category, string FileName, string ContentType,
    long SizeBytes, DateOnly? IssuedOn, DateOnly? ExpiresOn, int? DaysToExpiry, string? Notes, string UploadedBy, DateTimeOffset UploadedAt);

public record DocumentDetails(string Title, DocumentCategory Category, DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Notes);

public record DocumentDownload(Stream Content, string FileName, string ContentType);
