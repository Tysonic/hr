using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Organization;

namespace TyHr.Api.Controllers;

/// <summary>The company's structure: everyone in the company may read it; HR changes it.</summary>
[ApiController]
[Route("api/org")]
[RequireTenant]
[Authorize(Policy = Permissions.OrgView)]
public class OrgController(OrgService org) : ControllerBase
{
    [HttpGet("chart")]
    public Task<IReadOnlyList<OrgChartNode>> Chart(CancellationToken ct) => org.OrgChartAsync(ct);

    [HttpGet("branches")]
    public Task<IReadOnlyList<BranchDto>> Branches(CancellationToken ct) => org.ListBranchesAsync(ct);

    [HttpPost("branches"), Authorize(Policy = Permissions.OrgManage)]
    public Task<BranchDto> CreateBranch(SaveBranchRequest r, CancellationToken ct) => org.CreateBranchAsync(r, ct);

    [HttpPut("branches/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public Task<BranchDto> UpdateBranch(Guid id, SaveBranchRequest r, CancellationToken ct) => org.UpdateBranchAsync(id, r, ct);

    [HttpPost("branches/{id:guid}/activate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<BranchDto> ActivateBranch(Guid id, CancellationToken ct) => org.SetBranchActiveAsync(id, true, ct);

    [HttpPost("branches/{id:guid}/deactivate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<BranchDto> DeactivateBranch(Guid id, CancellationToken ct) => org.SetBranchActiveAsync(id, false, ct);

    [HttpDelete("branches/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public async Task<IActionResult> DeleteBranch(Guid id, CancellationToken ct) { await org.DeleteBranchAsync(id, ct); return NoContent(); }

    [HttpGet("departments")]
    public Task<IReadOnlyList<DepartmentDto>> Departments(CancellationToken ct) => org.ListDepartmentsAsync(ct);

    [HttpPost("departments"), Authorize(Policy = Permissions.OrgManage)]
    public Task<DepartmentDto> CreateDepartment(SaveDepartmentRequest r, CancellationToken ct) => org.CreateDepartmentAsync(r, ct);

    [HttpPut("departments/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public Task<DepartmentDto> UpdateDepartment(Guid id, SaveDepartmentRequest r, CancellationToken ct) => org.UpdateDepartmentAsync(id, r, ct);

    [HttpPost("departments/{id:guid}/activate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<DepartmentDto> ActivateDepartment(Guid id, CancellationToken ct) => org.SetDepartmentActiveAsync(id, true, ct);

    [HttpPost("departments/{id:guid}/deactivate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<DepartmentDto> DeactivateDepartment(Guid id, CancellationToken ct) => org.SetDepartmentActiveAsync(id, false, ct);

    [HttpDelete("departments/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public async Task<IActionResult> DeleteDepartment(Guid id, CancellationToken ct) { await org.DeleteDepartmentAsync(id, ct); return NoContent(); }

    [HttpGet("grades")]
    public Task<IReadOnlyList<JobGradeDto>> Grades(CancellationToken ct) => org.ListJobGradesAsync(ct);

    [HttpPost("grades"), Authorize(Policy = Permissions.OrgManage)]
    public Task<JobGradeDto> CreateGrade(SaveJobGradeRequest r, CancellationToken ct) => org.CreateJobGradeAsync(r, ct);

    [HttpPut("grades/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public Task<JobGradeDto> UpdateGrade(Guid id, SaveJobGradeRequest r, CancellationToken ct) => org.UpdateJobGradeAsync(id, r, ct);

    [HttpPost("grades/{id:guid}/activate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<JobGradeDto> ActivateGrade(Guid id, CancellationToken ct) => org.SetJobGradeActiveAsync(id, true, ct);

    [HttpPost("grades/{id:guid}/deactivate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<JobGradeDto> DeactivateGrade(Guid id, CancellationToken ct) => org.SetJobGradeActiveAsync(id, false, ct);

    [HttpDelete("grades/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public async Task<IActionResult> DeleteGrade(Guid id, CancellationToken ct) { await org.DeleteJobGradeAsync(id, ct); return NoContent(); }

    [HttpGet("positions")]
    public Task<IReadOnlyList<PositionDto>> Positions(CancellationToken ct) => org.ListPositionsAsync(ct);

    [HttpPost("positions"), Authorize(Policy = Permissions.OrgManage)]
    public Task<PositionDto> CreatePosition(SavePositionRequest r, CancellationToken ct) => org.CreatePositionAsync(r, ct);

    [HttpPut("positions/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public Task<PositionDto> UpdatePosition(Guid id, SavePositionRequest r, CancellationToken ct) => org.UpdatePositionAsync(id, r, ct);

    [HttpPost("positions/{id:guid}/activate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<PositionDto> ActivatePosition(Guid id, CancellationToken ct) => org.SetPositionActiveAsync(id, true, ct);

    [HttpPost("positions/{id:guid}/deactivate"), Authorize(Policy = Permissions.OrgManage)]
    public Task<PositionDto> DeactivatePosition(Guid id, CancellationToken ct) => org.SetPositionActiveAsync(id, false, ct);

    [HttpDelete("positions/{id:guid}"), Authorize(Policy = Permissions.OrgManage)]
    public async Task<IActionResult> DeletePosition(Guid id, CancellationToken ct) { await org.DeletePositionAsync(id, ct); return NoContent(); }
}
