using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TyHr.Application.Common;
using TyHr.Infrastructure.Security;

namespace TyHr.Infrastructure.Persistence;

/// <summary>For dotnet ef (migrations), which builds the model without a request or a real database.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(Environment.GetEnvironmentVariable("TYHR_DESIGN_CONNECTION") ?? "Host=localhost;Database=tyhr_design")
                .Options,
            new TenantContext(), new SystemUser(), TimeProvider.System,
            // Only shapes the model (column types); never encrypts real data.
            new SecretProtector(Convert.ToBase64String(new byte[32])));

    private sealed class SystemUser : ICurrentUser
    {
        public string UserName => "system";
        public string? Email => null;
        public bool IsInRole(string role) => false;
    }
}
