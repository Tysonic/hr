using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Reminders;

namespace TyHr.Api.Controllers;

/// <summary>Documents, contracts and probations expiring or ending soon, and the reminder emails about them.</summary>
[ApiController]
[Route("api/expiries")]
[RequireTenant]
[Authorize(Policy = Permissions.ExpiriesView)]
public class ExpiriesController(ExpiryService expiries) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ExpiringItem>> List([FromQuery] int withinDays = 60, CancellationToken ct = default) => expiries.ListAsync(withinDays, ct);

    /// <summary>Sends any reminders now instead of waiting for the daily run (nothing is sent twice).</summary>
    [HttpPost("send-reminders"), Authorize(Policy = Permissions.CompanySettings)]
    public Task<ReminderRunResult> SendReminders(CancellationToken ct) => expiries.SendRemindersAsync(ct);
}
