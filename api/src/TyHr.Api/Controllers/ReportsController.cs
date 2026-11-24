using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Reports;

namespace TyHr.Api.Controllers;

/// <summary>Company reports for HR and payroll; pay figures are checked in the service.</summary>
[ApiController]
[Route("api/reports")]
[RequireTenant]
[Authorize(Policy = Permissions.ReportsView)]
public class ReportsController(ReportsService reports) : ControllerBase
{
    [HttpGet("headcount")]
    public Task<HeadcountReport> Headcount(CancellationToken ct) => reports.HeadcountAsync(ct);

    [HttpGet("turnover")]
    public Task<TurnoverReport> Turnover(CancellationToken ct) => reports.TurnoverAsync(ct);

    [HttpGet("leave-liability")]
    public Task<LeaveLiabilityReport> LeaveLiability(CancellationToken ct) => reports.LeaveLiabilityAsync(ct);

    [HttpGet("payroll-cost"), Authorize(Policy = Permissions.PayrollView)]
    public Task<PayrollCostReport> PayrollCost([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct) => reports.PayrollCostAsync(year, month, ct);

    /// <summary>Every report the user may see, one sheet each.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var file = await reports.ExportAsync(ct);
        return File(file.Content, Spreadsheet.ContentType, file.FileName);
    }
}
