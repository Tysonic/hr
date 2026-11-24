using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Domain;

namespace TyHr.Infrastructure.Files;

public class StorageOptions
{
    /// <summary>Folder for uploaded files: a Docker volume in production (/files); hr/data/files in development. Relative paths are from the app folder.</summary>
    public string Path { get; set; } = "data/files";
}

/// <summary>
/// Files on local disk at {Path}/{tenant}/{key}. Tenants and keys are checked against a strict pattern (keys are made
/// by the system, never from file names), so nothing can step outside its company's folder. Writes go to a temporary
/// file first and are moved into place, so a half-written file is never served.
/// </summary>
public partial class LocalFileStore(IOptions<StorageOptions> options) : IFileStore
{
    private readonly string _root = System.IO.Path.GetFullPath(options.Value.Path);

    public async Task SaveAsync(string tenant, string key, Stream content, CancellationToken cancellationToken = default)
    {
        var path = Resolve(tenant, key);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await content.CopyToAsync(file, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public Task<Stream> OpenReadAsync(string tenant, string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(tenant, key);
        if (!File.Exists(path)) throw new NotFoundException("The file is missing from storage.");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task DeleteAsync(string tenant, string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(tenant, key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string tenant, string key)
    {
        if (!TenantPattern().IsMatch(tenant)) throw new ArgumentException($"'{tenant}' is not a valid tenant for file storage.", nameof(tenant));
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException($"'{key}' is not a valid storage key.", nameof(key));
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, tenant, key));
        if (!path.StartsWith(_root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("The storage key leaves the storage folder.", nameof(key));
        return path;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex TenantPattern();

    [GeneratedRegex("^[a-z0-9]+(/[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();
}
