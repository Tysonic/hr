using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Domain.Leave;

namespace TyHr.Application.Leave;

/// <summary>
/// Emails about leave: a new request to the employee's line manager (or the company's HR email when they have none or
/// it isn't set), and decisions to the employee's work email. Best effort: the request is already saved, so a mail
/// failure is logged, not shown as an error.
/// </summary>
public class LeaveNotifier(IAppDbContext db, IEmailSender email, ILogger<LeaveNotifier> logger)
{
    public async Task RequestedAsync(LeaveRequestDto r, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == r.EmployeeId).Select(e => new { e.ManagerId }).SingleAsync(ct);
        var to = employee.ManagerId is { } managerId
            ? await db.Employees.AsNoTracking().Where(e => e.Id == managerId).Select(e => e.WorkEmail).SingleOrDefaultAsync(ct)
            : null;
        to ??= await db.CompanyProfiles.AsNoTracking().Select(p => p.Email).SingleAsync(ct);
        await SendAsync(to, $"Leave request from {r.EmployeeName}: {r.LeaveTypeName}, {Dates(r)}",
            $"{r.EmployeeName} ({r.EmployeeNumber}) asks for {r.LeaveTypeName.ToLowerInvariant()}: {Dates(r)} ({r.Days:0.#} day{(r.Days == 1 ? "" : "s")})."
            + (r.Reason != null ? $"\nReason: {r.Reason}" : "") + "\n\nApprove or decline it in TyHR under Leave > Approvals.", ct);
    }

    public async Task DecidedAsync(LeaveRequestDto r, CancellationToken ct)
    {
        var to = await db.Employees.AsNoTracking().Where(e => e.Id == r.EmployeeId).Select(e => e.WorkEmail).SingleAsync(ct);
        if (to == null) return;
        var what = r.Status switch
        {
            LeaveStatus.Approved => "approved",
            LeaveStatus.Rejected => "declined",
            _ => "cancelled",
        };
        await SendAsync(to, $"Your {r.LeaveTypeName.ToLowerInvariant()} for {Dates(r)} is {what}",
            $"Your {r.LeaveTypeName.ToLowerInvariant()} for {Dates(r)} ({r.Days:0.#} day{(r.Days == 1 ? "" : "s")}) was {what} by {r.DecidedBy}."
            + (r.DecisionNote != null ? $"\nNote: {r.DecisionNote}" : ""), ct);
    }

    private static string Dates(LeaveRequestDto r) =>
        r.StartDate == r.EndDate ? $"{r.StartDate:d MMM yyyy}{(r.HalfDay ? " (half day)" : "")}" : $"{r.StartDate:d MMM} to {r.EndDate:d MMM yyyy}";

    private async Task SendAsync(string to, string subject, string text, CancellationToken ct)
    {
        try
        {
            var html = string.Join("", text.Split('\n').Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>"));
            await email.SendAsync(new EmailMessage([to], subject, html, text), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not send the leave email \"{Subject}\".", subject);
        }
    }
}
