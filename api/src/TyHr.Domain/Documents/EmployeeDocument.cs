namespace TyHr.Domain.Documents;

public enum DocumentCategory { Identification, Contract, Certificate, WorkPermit, Medical, Other }

/// <summary>
/// A file kept on an employee's record (ID copy, signed contract, certificate, work permit...). The bytes live in the
/// file store under <see cref="StorageKey"/>, which the system makes up; the uploaded file name is only shown and used
/// for downloads. Documents with an <see cref="ExpiresOn"/> date are tracked for renewal reminders.
/// </summary>
public class EmployeeDocument : TenantEntity
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private EmployeeDocument() { }

    public EmployeeDocument(Guid employeeId, string title, DocumentCategory category, string fileName, string contentType, long sizeBytes,
        DateOnly? issuedOn, DateOnly? expiresOn, string? notes, string uploadedBy, DateTimeOffset uploadedAt)
    {
        if (sizeBytes <= 0) throw new DomainException("The file is empty.");
        if (sizeBytes > MaxBytes) throw new DomainException($"Files can be at most {MaxBytes / 1024 / 1024} MB.");
        EmployeeId = employeeId;
        FileName = Guard.Required(Path.GetFileName(fileName), "File name");
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
        StorageKey = $"employees/{employeeId:N}/{Id:N}";
        Update(title, category, issuedOn, expiresOn, notes);
    }

    public Guid EmployeeId { get; private set; }
    public string Title { get; private set; } = null!;
    public DocumentCategory Category { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    /// <summary>Where the bytes are in the file store, below the company's folder. Never derived from user input.</summary>
    public string StorageKey { get; private set; } = null!;

    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public string? Notes { get; private set; }
    public string UploadedBy { get; private set; } = null!;
    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>Changes the details; the file itself is replaced by uploading a new document.</summary>
    public void Update(string title, DocumentCategory category, DateOnly? issuedOn, DateOnly? expiresOn, string? notes)
    {
        if (issuedOn != null && expiresOn != null && expiresOn < issuedOn) throw new DomainException("The document expires before it was issued.");
        Title = Guard.Required(title, "Title");
        Category = category;
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Notes = Guard.Optional(notes);
    }
}
