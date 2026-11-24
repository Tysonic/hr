using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TyHr.Domain;
using TyHr.Domain.Auditing;

namespace TyHr.Infrastructure.Auditing;

/// <summary>
/// Adds an <see cref="AuditEntry"/> for every record a save creates, changes or deletes (adapted from MyBiz's
/// AuditChangesInterceptor). Values of secrets and properties marked [NotAudited] are never kept, only that they
/// changed. Entries are added to the same save, so the trail and the change commit together.
/// </summary>
internal static partial class AuditTrail
{
    private const int MaxText = 200;
    private const string Hidden = "(hidden)";

    [GeneratedRegex("password|secret|token|apikey|api_key|privatekey|private_key|hash|salt", RegexOptions.IgnoreCase)]
    private static partial Regex SecretName();

    public static void Record(DbContext context, string? tenant, string userName, DateTimeOffset now)
    {
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(e => e.Entity is not AuditEntry && e.Metadata.ClrType.GetCustomAttribute<NoAuditAttribute>() == null)
            .ToList();

        foreach (var entry in entries)
        {
            var (action, fields) = entry.State switch
            {
                EntityState.Added => ("created", Fields(entry, p => true)),
                EntityState.Modified => ("updated", Fields(entry, p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue))),
                _ => ("deleted", null),
            };
            if (action == "updated" && fields!.Count == 0) continue;

            // A tenant-owned row is audited under its own company (it can only be the request's, see AppDbContext).
            var owner = entry.Entity is ITenantOwned owned ? owned.Tenant ?? tenant : tenant;
            context.Add(new AuditEntry(owner, userName, now, entry.Metadata.ClrType.Name, KeyOf(entry), action,
                fields == null ? null : JsonSerializer.Serialize(fields)));
        }
    }

    private static Dictionary<string, object?[]> Fields(EntityEntry entry, Func<PropertyEntry, bool> include)
    {
        var fields = new Dictionary<string, object?[]>();
        foreach (var property in entry.Properties)
        {
            if (property.Metadata.IsPrimaryKey() || property.Metadata.Name == nameof(ITenantOwned.Tenant) || !include(property)) continue;
            var name = property.Metadata.Name;
            var hidden = SecretName().IsMatch(name) || property.Metadata.PropertyInfo?.GetCustomAttribute<NotAuditedAttribute>() != null
                || property.Metadata.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>() != null;
            var before = entry.State == EntityState.Added ? null : property.OriginalValue;
            var after = property.CurrentValue;
            if (entry.State == EntityState.Added && after == null) continue;
            fields[name] = hidden ? [before == null ? null : Hidden, after == null ? null : Hidden] : [Format(before), Format(after)];
        }
        return fields;
    }

    private static string KeyOf(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key == null) return "?";
        var values = key.Properties.Select(p => entry.Property(p.Name)).ToList();
        if (values.Any(v => v.IsTemporary)) return "(new)";
        return string.Join("/", values.Select(v => v.CurrentValue?.ToString()));
    }

    private static object? Format(object? value) => value switch
    {
        null => null,
        string s => s.Length <= MaxText ? s : s[..MaxText] + "…",
        byte[] => "(binary)",
        DateTime d => d.ToString("o"),
        DateTimeOffset d => d.ToString("o"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        bool or int or long or short or decimal or double or float or Guid => value,
        Enum e => e.ToString(),
        _ => value.ToString() is { Length: > MaxText } t ? t[..MaxText] + "…" : value.ToString(),
    };
}
