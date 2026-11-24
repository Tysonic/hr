using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain.Employees;
using TyHr.Domain.Reminders;

namespace TyHr.Application.Reminders;

/// <summary>Something about to expire or end: a document, a contract, or a probation not yet confirmed.</summary>
public record ExpiringItem(ReminderKind Kind, Guid ItemId, Guid EmployeeId, string EmployeeNumber, string EmployeeName, string Title,
    DateOnly DueOn, int DaysLeft);

public record ReminderRunResult(int ItemsReminded, string? SentTo, bool MailConfigured);

/// <summary>
/// What is expiring or ending for the company's current employees, and the reminder emails about it. Reminders go to
/// the company's HR email (company profile) at 30 days before, 7 days before, and on the day; each milestone at most
/// once per item (<see cref="ReminderLog"/>), and only the most urgent one due, so a late first run doesn't send a stale
/// "30 days left". Items that expired more than <see cref="LookBackDays"/> ago are left alone.
/// </summary>
public class ExpiryService(IAppDbContext db, ITenantContext tenantContext, IEmailSender email, TimeProvider clock)
{
    public static readonly int[] Milestones = [30, 7, 0];
    public const int LookBackDays = 30;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>Items due from <see cref="LookBackDays"/> ago up to <paramref name="withinDays"/> ahead, soonest first.</summary>
    public async Task<IReadOnlyList<ExpiringItem>> ListAsync(int withinDays, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var today = Today;
        var from = today.AddDays(-LookBackDays);
        var to = today.AddDays(Math.Clamp(withinDays, 0, 366));
        var current = db.Employees.Where(e => e.Status != EmployeeStatus.Exited);

        var documents = await db.EmployeeDocuments.AsNoTracking()
            .Where(d => d.ExpiresOn >= from && d.ExpiresOn <= to)
            .Join(current, d => d.EmployeeId, e => e.Id, (d, e) => new { d.Id, EmployeeId = e.Id, e.EmployeeNumber, e.FirstName, e.LastName, d.Title, Due = d.ExpiresOn!.Value })
            .ToListAsync(ct);
        var contracts = await db.EmploymentContracts.AsNoTracking()
            .Where(c => c.EndDate >= from && c.EndDate <= to)
            .Join(current, c => c.EmployeeId, e => e.Id, (c, e) => new { c.Id, EmployeeId = e.Id, e.EmployeeNumber, e.FirstName, e.LastName, c.Type, Due = c.EndDate!.Value })
            .ToListAsync(ct);
        // A contract ending is only worth a reminder if nothing follows it.
        var followed = (await db.EmploymentContracts.AsNoTracking().Select(c => new { c.EmployeeId, c.StartDate }).ToListAsync(ct))
            .ToLookup(c => c.EmployeeId, c => c.StartDate);
        var probations = await db.EmploymentContracts.AsNoTracking()
            .Where(c => c.ConfirmedOn == null && c.ProbationEndDate >= from && c.ProbationEndDate <= to)
            .Join(current, c => c.EmployeeId, e => e.Id, (c, e) => new { c.Id, EmployeeId = e.Id, e.EmployeeNumber, e.FirstName, e.LastName, Due = c.ProbationEndDate!.Value })
            .ToListAsync(ct);

        return documents.Select(x => new ExpiringItem(ReminderKind.DocumentExpiry, x.Id, x.EmployeeId, x.EmployeeNumber, $"{x.FirstName} {x.LastName}", x.Title, x.Due, Days(x.Due)))
            .Concat(contracts.Where(x => !followed[x.EmployeeId].Any(start => start > x.Due))
                .Select(x => new ExpiringItem(ReminderKind.ContractEnd, x.Id, x.EmployeeId, x.EmployeeNumber, $"{x.FirstName} {x.LastName}",
                    $"{(x.Type == ContractType.FixedTerm ? "Fixed-term" : x.Type.ToString())} contract", x.Due, Days(x.Due))))
            .Concat(probations.Select(x => new ExpiringItem(ReminderKind.ProbationEnd, x.Id, x.EmployeeId, x.EmployeeNumber, $"{x.FirstName} {x.LastName}",
                "Probation", x.Due, Days(x.Due))))
            .OrderBy(i => i.DueOn).ThenBy(i => i.EmployeeName)
            .ToList();

        int Days(DateOnly due) => due.DayNumber - today.DayNumber;
    }

    /// <summary>Emails the company's HR address about items that reached a milestone and haven't been reminded at it.</summary>
    public async Task<ReminderRunResult> SendRemindersAsync(CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var items = await ListAsync(Milestones.Max(), ct);
        var sent = (await db.ReminderLogs.AsNoTracking().Select(r => new { r.Kind, r.ItemId, r.DueOn, r.DaysBefore }).ToListAsync(ct))
            .Select(r => (r.Kind, r.ItemId, r.DueOn, r.DaysBefore)).ToHashSet();

        var due = items
            .Select(i => (Item: i, Milestone: Milestones.Where(m => i.DaysLeft <= m).DefaultIfEmpty(-1).Min()))
            .Where(x => x.Milestone >= 0 && !sent.Contains((x.Item.Kind, x.Item.ItemId, x.Item.DueOn, x.Milestone)))
            .ToList();
        if (due.Count == 0) return new ReminderRunResult(0, null, true);

        var company = await db.Tenants.AsNoTracking().Where(t => t.Slug == tenant).Select(t => t.Name).SingleAsync(ct);
        var to = await db.CompanyProfiles.AsNoTracking().Select(p => p.Email).SingleAsync(ct);
        if (!await email.SendAsync(Compose(company, to, due.Select(d => d.Item).ToList()), ct))
            return new ReminderRunResult(0, null, false);

        var now = clock.GetUtcNow();
        foreach (var (item, milestone) in due)
            db.ReminderLogs.Add(new ReminderLog(item.Kind, item.ItemId, item.DueOn, milestone, now));
        await db.SaveChangesAsync(ct);
        await RemindManagersAsync(company, due.Select(d => d.Item).Where(i => i.Kind == ReminderKind.ProbationEnd).ToList(), ct);
        return new ReminderRunResult(due.Count, to, true);
    }

    /// <summary>Asks each line manager for a probation review of their reports in the batch (best effort, after HR's email).</summary>
    private async Task RemindManagersAsync(string company, IReadOnlyList<ExpiringItem> probations, CancellationToken ct)
    {
        if (probations.Count == 0) return;
        var ids = probations.Select(p => p.EmployeeId).ToList();
        var managers = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id) && e.ManagerId != null)
            .Join(db.Employees.AsNoTracking().Where(m => m.WorkEmail != null && m.Status != EmployeeStatus.Exited), e => e.ManagerId, m => m.Id,
                (e, m) => new { e.Id, m.WorkEmail, m.FirstName })
            .ToListAsync(ct);
        foreach (var group in managers.GroupBy(m => (m.WorkEmail!, m.FirstName)))
        {
            var mine = probations.Where(p => group.Any(g => g.Id == p.EmployeeId)).ToList();
            var lines = mine.Select(p => $"{p.EmployeeName}: probation ends {p.DueOn:d MMM yyyy}").ToList();
            var text = $"Hello {group.Key.Item2},\n\nPlease review the probation of:\n{string.Join("\n", lines.Select(l => "- " + l))}\n\n"
                       + "In TyHR, open Probation reviews to recommend confirming, extending or not confirming. HR makes the final decision.";
            var html = $"<p>Hello {WebUtility.HtmlEncode(group.Key.Item2)},</p><p>Please review the probation of:</p><ul>{string.Join("", lines.Select(l => $"<li>{WebUtility.HtmlEncode(l)}</li>"))}</ul>"
                       + "<p>In TyHR, open Probation reviews to recommend confirming, extending or not confirming. HR makes the final decision.</p>";
            try
            {
                await email.SendAsync(new EmailMessage([group.Key.Item1], $"{company}: probation review{(mine.Count == 1 ? "" : "s")} due", html, text), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // HR has already been told; a manager's copy is a courtesy.
                _ = e;
            }
        }
    }

    private static EmailMessage Compose(string company, string to, IReadOnlyList<ExpiringItem> items)
    {
        static string When(ExpiringItem i) => i.DaysLeft switch
        {
            < 0 => $"{-i.DaysLeft} day{(i.DaysLeft == -1 ? "" : "s")} ago",
            0 => "today",
            1 => "tomorrow",
            _ => $"in {i.DaysLeft} days",
        };
        static string What(ExpiringItem i) => i.Kind switch
        {
            ReminderKind.DocumentExpiry => $"{i.Title} expires",
            ReminderKind.ContractEnd => $"{i.Title} ends",
            _ => "Probation ends (not yet confirmed)",
        };

        var text = new StringBuilder($"{company}: {items.Count} {(items.Count == 1 ? "item needs" : "items need")} attention.\n\n");
        var html = new StringBuilder($"<p>{WebUtility.HtmlEncode(company)}: {items.Count} {(items.Count == 1 ? "item needs" : "items need")} attention.</p>"
            + "<table cellpadding=\"6\" style=\"border-collapse:collapse\"><tr><th align=\"left\">Employee</th><th align=\"left\">What</th><th align=\"left\">Date</th><th align=\"left\">When</th></tr>");
        foreach (var i in items)
        {
            text.AppendLine($"- {i.EmployeeName} ({i.EmployeeNumber}): {What(i)} {i.DueOn:d MMM yyyy} ({When(i)})");
            html.Append($"<tr><td>{WebUtility.HtmlEncode(i.EmployeeName)} ({WebUtility.HtmlEncode(i.EmployeeNumber)})</td><td>{WebUtility.HtmlEncode(What(i))}</td>"
                + $"<td>{i.DueOn:d MMM yyyy}</td><td>{When(i)}</td></tr>");
        }
        html.Append("</table><p>Open TyHR to renew documents, extend contracts or confirm employees.</p>");
        text.AppendLine("\nOpen TyHR to renew documents, extend contracts or confirm employees.");
        var subject = $"TyHR: {items.Count} expir{(items.Count == 1 ? "y" : "ies")} to deal with at {company}";
        return new EmailMessage([to], subject, html.ToString(), text.ToString());
    }
}
