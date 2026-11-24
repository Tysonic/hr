namespace TyHr.Domain;

public static class Guard
{
    /// <summary>The trimmed value, or a DomainException naming the field when it is blank.</summary>
    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException($"{field} is required.") : value.Trim();

    /// <summary>The trimmed value, or null when blank.</summary>
    public static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
