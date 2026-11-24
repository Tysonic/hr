using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TyHr.Application.Common;
using TyHr.Domain.Countries;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Persistence;
using TyHr.Infrastructure.Security;

namespace TyHr.Tests;

/// <summary>A throwaway PostgreSQL in Docker with the real migrations applied, shared by a test class.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero));

    public static readonly string SecretsKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    public static readonly SecretProtector Secrets = new(SecretsKey);

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var scope = NewScope(tenant: null);
        await scope.Db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>A fresh context acting for <paramref name="tenant"/> (null = platform), like one HTTP request.</summary>
    public Scope NewScope(string? tenant, string user = "tester", string[]? roles = null, string? email = null)
    {
        var tenantContext = new TenantContext { Tenant = tenant };
        var currentUser = new TestUser { UserName = user, Email = email, Roles = [.. roles ?? [Roles.HrAdmin]] };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_container.GetConnectionString()).Options,
            tenantContext, currentUser, Clock, Secrets);
        return new Scope(db, tenantContext, currentUser);
    }

    /// <summary>Creates a company and its profile directly (no Keycloak), returning its slug.</summary>
    public async Task<string> CreateTenantAsync(string? slug = null)
    {
        slug ??= "co-" + Guid.NewGuid().ToString("N")[..8];
        await using (var platform = NewScope(null))
        {
            platform.Db.Tenants.Add(new Tenant(slug, $"Company {slug}", CountryPacks.Uganda, $"admin@{slug}.test", "tester", Clock.GetUtcNow()));
            await platform.Db.SaveChangesAsync();
        }
        await using (var company = NewScope(slug))
        {
            company.Db.CompanyProfiles.Add(new CompanyProfile($"Company {slug} Ltd", $"hr@{slug}.test"));
            await company.Db.SaveChangesAsync();
        }
        return slug;
    }
}

public sealed class Scope(AppDbContext db, TenantContext tenantContext, TestUser user) : IAsyncDisposable
{
    public AppDbContext Db { get; } = db;
    public TenantContext TenantContext { get; } = tenantContext;
    public TestUser User { get; } = user;

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

public class TestUser : ICurrentUser
{
    public required string UserName { get; set; }
    public string? Email { get; set; }
    public HashSet<string> Roles { get; set; } = [TyHr.Application.Common.Roles.HrAdmin];
    public bool IsInRole(string role) => Roles.Contains(role);
}

public class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Records invitations instead of calling Keycloak; can be told to fail.</summary>
public class FakeProvisioner : IIdentityProvisioner
{
    public List<(string Tenant, string Email, string Role)> Invited { get; } = [];
    public Exception? FailWith { get; set; }

    public Task<string> InviteUserAsync(string tenant, string email, string firstName, string lastName, string role, CancellationToken cancellationToken = default)
    {
        if (FailWith != null) throw FailWith;
        Invited.Add((tenant, email, role));
        return Task.FromResult(Guid.NewGuid().ToString());
    }
}
