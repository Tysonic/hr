using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Application.Reminders;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Api.Infrastructure;

public class ReminderOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Hour of the day (UTC) for the daily run; 5 UTC is 8am in Kampala and Nairobi.</summary>
    public int HourUtc { get; set; } = 5;
}

/// <summary>
/// Once a day, sends each active company its expiry reminders. Runs per company in its own scope with the tenant set,
/// so the usual company filters apply; one company's failure (say its mail is refused) doesn't stop the others.
/// Sending is idempotent, so a run after a restart or a missed day only sends what is still unsent.
/// </summary>
public class ReminderJob(IServiceScopeFactory scopes, IOptions<ReminderOptions> options, TimeProvider clock, ILogger<ReminderJob> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var next = new DateTimeOffset(now.UtcDateTime.Date.AddHours(options.Value.HourUtc), TimeSpan.Zero);
            if (next <= now) next = next.AddDays(1);
            try
            {
                await Task.Delay(next - now, clock, stoppingToken);
                await RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        List<string> tenants;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenants = await db.Tenants.AsNoTracking().Where(t => t.Status == TenantStatus.Active).Select(t => t.Slug).ToListAsync(ct);
        }
        foreach (var tenant in tenants)
        {
            try
            {
                using var scope = scopes.CreateScope();
                scope.ServiceProvider.GetRequiredService<ITenantContext>().Tenant = tenant;
                var result = await scope.ServiceProvider.GetRequiredService<ExpiryService>().SendRemindersAsync(ct);
                if (result.ItemsReminded > 0) logger.LogInformation("Sent {Count} expiry reminders for {Tenant}.", result.ItemsReminded, tenant);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Expiry reminders for {Tenant} failed; they will be retried on the next run.", tenant);
            }
        }
    }
}
