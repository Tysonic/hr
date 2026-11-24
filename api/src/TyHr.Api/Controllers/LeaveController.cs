using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Leave;
using TyHr.Domain.Documents;

namespace TyHr.Api.Controllers;

/// <summary>Leave set-up: types, public holidays and the working week. Everyone in the company can read it; HR changes it.</summary>
[ApiController]
[Route("api/leave")]
[RequireTenant]
[Authorize(Policy = Permissions.LeaveView)]
public class LeaveSetupController(LeaveSetupService setup) : ControllerBase
{
    [HttpGet("types")]
    public Task<IReadOnlyList<LeaveTypeDto>> Types(CancellationToken ct) => setup.ListTypesAsync(ct);

    [HttpPost("types"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<LeaveTypeDto> CreateType(SaveLeaveTypeRequest r, CancellationToken ct) => setup.CreateTypeAsync(r, ct);

    [HttpPut("types/{id:guid}"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<LeaveTypeDto> UpdateType(Guid id, SaveLeaveTypeRequest r, CancellationToken ct) => setup.UpdateTypeAsync(id, r, ct);

    [HttpPost("types/{id:guid}/activate"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<LeaveTypeDto> Activate(Guid id, CancellationToken ct) => setup.SetTypeActiveAsync(id, true, ct);

    [HttpPost("types/{id:guid}/deactivate"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<LeaveTypeDto> Deactivate(Guid id, CancellationToken ct) => setup.SetTypeActiveAsync(id, false, ct);

    /// <summary>Adds the country's statutory leave types the company doesn't have yet.</summary>
    [HttpPost("types/country-defaults"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<IReadOnlyList<LeaveTypeDto>> AddCountryDefaults(CancellationToken ct) => setup.AddCountryDefaultsAsync(ct);

    [HttpGet("holidays")]
    public Task<IReadOnlyList<HolidayDto>> Holidays([FromQuery, BindRequired] int year, CancellationToken ct) => setup.ListHolidaysAsync(year, ct);

    [HttpPost("holidays"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<HolidayDto> AddHoliday(SaveHolidayRequest r, CancellationToken ct) => setup.AddHolidayAsync(r, ct);

    [HttpDelete("holidays/{id:guid}"), Authorize(Policy = Permissions.LeaveSetup)]
    public async Task<IActionResult> DeleteHoliday(Guid id, CancellationToken ct)
    {
        await setup.DeleteHolidayAsync(id, ct);
        return NoContent();
    }

    /// <summary>Adds the country's holidays for a year (fixed dates and Easter); Eid and gazetted days are added by hand.</summary>
    [HttpPost("holidays/generate"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<GenerateHolidaysResult> GenerateHolidays([FromQuery, BindRequired] int year, CancellationToken ct) => setup.GenerateHolidaysAsync(year, ct);

    [HttpGet("settings")]
    public Task<LeaveSettingsDto> Settings(CancellationToken ct) => setup.SettingsAsync(ct);

    [HttpPut("settings/work-week"), Authorize(Policy = Permissions.LeaveSetup)]
    public Task<LeaveSettingsDto> SaveWorkWeek(SaveWorkWeekRequest r, CancellationToken ct) => setup.SaveWorkWeekAsync(r, ct);
}

/// <summary>Leave requests, balances and the calendar. Who sees and decides what is enforced by <see cref="LeaveService"/>.</summary>
[ApiController]
[Route("api/leave")]
[RequireTenant]
[Authorize(Policy = Permissions.LeaveView)]
public class LeaveRequestsController(LeaveService leave) : ControllerBase
{
    /// <summary>scope: mine (default), approvals (pending ones the user may decide) or all (HR and payroll; managers: their team).</summary>
    [HttpGet("requests")]
    public Task<PagedResult<LeaveRequestDto>> List([FromQuery] LeaveRequestQuery query, CancellationToken ct) => leave.ListAsync(query, ct);

    [HttpGet("requests/{id:guid}")]
    public Task<LeaveRequestDto> Get(Guid id, CancellationToken ct) => leave.GetAsync(id, ct);

    [HttpPost("requests/preview")]
    public Task<LeavePreview> Preview(CreateLeaveRequest r, CancellationToken ct) => leave.PreviewAsync(r, ct);

    [HttpPost("requests")]
    public Task<LeaveRequestDto> Create(CreateLeaveRequest r, CancellationToken ct) => leave.CreateAsync(r, ct);

    [HttpPost("requests/{id:guid}/approve"), Authorize(Policy = Permissions.LeaveApprove)]
    public Task<LeaveRequestDto> Approve(Guid id, DecideLeaveRequest r, CancellationToken ct) => leave.ApproveAsync(id, r.Note, ct);

    [HttpPost("requests/{id:guid}/reject"), Authorize(Policy = Permissions.LeaveApprove)]
    public Task<LeaveRequestDto> Reject(Guid id, DecideLeaveRequest r, CancellationToken ct) => leave.RejectAsync(id, r.Note, ct);

    [HttpPost("requests/{id:guid}/cancel")]
    public Task<LeaveRequestDto> Cancel(Guid id, DecideLeaveRequest r, CancellationToken ct) => leave.CancelAsync(id, r.Note, ct);

    [HttpPost("requests/{id:guid}/document")]
    [RequestSizeLimit(EmployeeDocument.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = EmployeeDocument.MaxBytes + 64 * 1024)]
    public async Task<LeaveRequestDto> AttachDocument(Guid id, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new Domain.DomainException("Choose a file to attach.");
        await using var content = file.OpenReadStream();
        return await leave.AttachDocumentAsync(id, file.FileName, file.Length, content, ct);
    }

    [HttpGet("requests/{id:guid}/document")]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var file = await leave.DocumentAsync(id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>One employee's leave year (own leave without employeeId).</summary>
    [HttpGet("balances")]
    public Task<EmployeeLeaveDto> Balances([FromQuery] Guid? employeeId, [FromQuery] int? year, CancellationToken ct) =>
        leave.EmployeeLeaveAsync(employeeId, year, ct);

    [HttpPost("adjustments"), Authorize(Policy = Permissions.LeaveManage)]
    public Task<EmployeeLeaveDto> Adjust(AdjustLeaveRequest r, CancellationToken ct) => leave.AdjustAsync(r, ct);

    [HttpGet("calendar")]
    public Task<LeaveCalendarDto> Calendar([FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        leave.CalendarAsync(from, to, departmentId, ct);
}
