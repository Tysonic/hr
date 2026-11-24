using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Attendance;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;

namespace TyHr.Api.Controllers;

/// <summary>
/// Clocking, timesheets, schedules, device imports and attendance reports. Everyone clocks and sees their own
/// timesheet; who else's they see is enforced by <see cref="AttendanceService"/>.
/// </summary>
[ApiController]
[Route("api/attendance")]
[RequireTenant]
[Authorize(Policy = Permissions.AttendanceClock)]
public class AttendanceController(AttendanceService attendance) : ControllerBase
{
    [HttpGet("me")]
    public Task<MyClockDto> MyClock(CancellationToken ct) => attendance.MyClockAsync(ct);

    [HttpPost("clock")]
    public Task<MyClockDto> Clock(ClockRequest r, CancellationToken ct) => attendance.ClockAsync(r, ct);

    /// <summary>One person's days (their own when no employee is given).</summary>
    [HttpGet("timesheet")]
    public Task<TimesheetDto> Timesheet([FromQuery] Guid? employeeId, [FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to, CancellationToken ct) =>
        attendance.TimesheetAsync(employeeId, from, to, ct);

    [HttpGet("summary"), Authorize(Policy = Permissions.AttendanceView)]
    public Task<IReadOnlyList<AttendanceSummaryRow>> Summary([FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        attendance.SummaryAsync(from, to, departmentId, ct);

    [HttpGet("summary/export"), Authorize(Policy = Permissions.AttendanceView)]
    public async Task<IActionResult> SummaryExport([FromQuery, BindRequired] DateOnly from, [FromQuery, BindRequired] DateOnly to, [FromQuery] Guid? departmentId, CancellationToken ct)
    {
        var file = await attendance.SummaryExportAsync(from, to, departmentId, ct);
        return File(file.Content, Spreadsheet.ContentType, file.FileName);
    }

    [HttpGet("day"), Authorize(Policy = Permissions.AttendanceView)]
    public Task<IReadOnlyList<DayBoardRow>> Day([FromQuery, BindRequired] DateOnly date, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        attendance.DayAsync(date, departmentId, ct);

    [HttpPost("punches"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<PunchDto> AddPunch(AddPunchRequest r, CancellationToken ct) => attendance.AddPunchAsync(r, ct);

    [HttpDelete("punches/{id:guid}"), Authorize(Policy = Permissions.AttendanceManage)]
    public async Task<IActionResult> DeletePunch(Guid id, CancellationToken ct)
    {
        await attendance.DeletePunchAsync(id, ct);
        return NoContent();
    }

    /// <summary>Checks a device export (commit=false) or saves its new punches (commit=true).</summary>
    [HttpPost("import"), Authorize(Policy = Permissions.AttendanceManage)]
    [RequestSizeLimit(AttendanceService.MaxImportBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = AttendanceService.MaxImportBytes + 64 * 1024)]
    public async Task<PunchImportResult> Import(IFormFile? file, [FromQuery] bool commit, [FromQuery] string? idColumn, [FromQuery] string? dateTimeColumn,
        [FromQuery] string? dateColumn, [FromQuery] string? timeColumn, [FromQuery] string? directionColumn, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new DomainException("Choose the file exported from the device.");
        if (file.Length > AttendanceService.MaxImportBytes) throw new DomainException("The file is larger than 5 MB; export a shorter period.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".txt" or ".tsv" or ".dat")) throw new DomainException("Upload the device's CSV or text export.");
        var columns = (idColumn ?? dateTimeColumn ?? dateColumn ?? timeColumn ?? directionColumn) is null
            ? null : new PunchColumns(idColumn, dateTimeColumn, dateColumn, timeColumn, directionColumn);
        await using var stream = file.OpenReadStream();
        return await attendance.ImportAsync(stream, columns, commit, ct);
    }

    // Schedules and set-up

    [HttpGet("schedules"), Authorize(Policy = Permissions.AttendanceView)]
    public Task<IReadOnlyList<ScheduleDto>> Schedules(CancellationToken ct) => attendance.ListSchedulesAsync(ct);

    [HttpPost("schedules"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<ScheduleDto> CreateSchedule(SaveScheduleRequest r, CancellationToken ct) => attendance.CreateScheduleAsync(r, ct);

    [HttpPut("schedules/{id:guid}"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<ScheduleDto> UpdateSchedule(Guid id, SaveScheduleRequest r, CancellationToken ct) => attendance.UpdateScheduleAsync(id, r, ct);

    [HttpPost("schedules/{id:guid}/default"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<ScheduleDto> MakeDefault(Guid id, CancellationToken ct) => attendance.MakeDefaultAsync(id, ct);

    [HttpPost("schedules/{id:guid}/activate"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<ScheduleDto> Activate(Guid id, CancellationToken ct) => attendance.SetScheduleActiveAsync(id, true, ct);

    [HttpPost("schedules/{id:guid}/deactivate"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<ScheduleDto> Deactivate(Guid id, CancellationToken ct) => attendance.SetScheduleActiveAsync(id, false, ct);

    [HttpPost("assignments"), Authorize(Policy = Permissions.AttendanceManage)]
    public async Task<object> Assign(AssignScheduleRequest r, CancellationToken ct) => new { assigned = await attendance.AssignAsync(r, ct) };

    [HttpGet("employees/{employeeId:guid}"), Authorize(Policy = Permissions.AttendanceView)]
    public Task<EmployeeAttendanceDto> EmployeeSetup(Guid employeeId, CancellationToken ct) => attendance.EmployeeSetupAsync(employeeId, ct);

    [HttpPut("employees/{employeeId:guid}/device-user"), Authorize(Policy = Permissions.AttendanceManage)]
    public Task<EmployeeAttendanceDto> SetDeviceUser(Guid employeeId, SetDeviceUserRequest r, CancellationToken ct) =>
        attendance.SetDeviceUserAsync(employeeId, r.DeviceUserId, ct);
}
