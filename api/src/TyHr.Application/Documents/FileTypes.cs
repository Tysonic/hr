using TyHr.Domain;

namespace TyHr.Application.Documents;

/// <summary>
/// The kinds of file employees' documents may be, checked by extension and by the file's first bytes, so a renamed
/// executable or HTML page isn't accepted. The content type sent for downloads comes from here, not from the upload.
/// </summary>
public static class FileTypes
{
    private static readonly byte[] Pdf = "%PDF"u8.ToArray();
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04]; // .docx and .xlsx are zip files

    private static readonly Dictionary<string, (string ContentType, byte[] Magic)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ("application/pdf", Pdf),
        [".png"] = ("image/png", Png),
        [".jpg"] = ("image/jpeg", Jpeg),
        [".jpeg"] = ("image/jpeg", Jpeg),
        [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", Zip),
        [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Zip),
    };

    public const string Accepted = "PDF, PNG, JPEG, Word (.docx) or Excel (.xlsx)";

    /// <summary>The content type to store and serve, or a DomainException when the file isn't an accepted kind.</summary>
    public static string Check(string fileName, ReadOnlySpan<byte> start)
    {
        if (!Allowed.TryGetValue(Path.GetExtension(fileName), out var type))
            throw new DomainException($"{Path.GetFileName(fileName)} isn't an accepted kind of file. Upload a {Accepted} file.");
        if (!start.StartsWith(type.Magic))
            throw new DomainException($"{Path.GetFileName(fileName)} doesn't look like a {Path.GetExtension(fileName).TrimStart('.').ToUpperInvariant()} file.");
        return type.ContentType;
    }
}
