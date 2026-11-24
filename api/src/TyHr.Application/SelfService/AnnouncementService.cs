using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.SelfService;

namespace TyHr.Application.SelfService;

/// <summary>HR's notices to everyone in the company.</summary>
public class AnnouncementService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, TimeProvider clock)
{
    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    /// <summary>Current announcements (pinned first, newest first); with <paramref name="all"/>, expired ones too, for HR.</summary>
    public async Task<IReadOnlyList<AnnouncementDto>> ListAsync(bool all, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var today = await TodayAsync(ct);
        var query = db.Announcements.AsNoTracking();
        if (!all || !user.Can(Permissions.AnnouncementsManage)) query = query.Where(a => a.ExpiresOn == null || a.ExpiresOn >= today);
        var items = await query.OrderByDescending(a => a.Pinned).ThenByDescending(a => a.PublishedAt).Take(100).ToListAsync(ct);
        return items.Select(a => ToDto(a, today)).ToList();
    }

    public async Task<AnnouncementDto> CreateAsync(SaveAnnouncementRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var today = await TodayAsync(ct);
        var a = new Announcement(r.Title, r.Body, r.Pinned, r.ExpiresOn, user.UserName, clock.GetUtcNow(), today);
        db.Announcements.Add(a);
        await db.SaveChangesAsync(ct);
        return ToDto(a, today);
    }

    public async Task<AnnouncementDto> UpdateAsync(Guid id, SaveAnnouncementRequest r, CancellationToken ct)
    {
        var a = await FindAsync(id, ct);
        var today = await TodayAsync(ct);
        a.Update(r.Title, r.Body, r.Pinned, r.ExpiresOn, today);
        await db.SaveChangesAsync(ct);
        return ToDto(a, today);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        db.Announcements.Remove(await FindAsync(id, ct));
        await db.SaveChangesAsync(ct);
    }

    private async Task<Announcement> FindAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("There is no such announcement.");
    }

    private static AnnouncementDto ToDto(Announcement a, DateOnly today) =>
        new(a.Id, a.Title, a.Body, a.Pinned, a.ExpiresOn, a.PublishedBy, a.PublishedAt, a.IsCurrentOn(today));
}
