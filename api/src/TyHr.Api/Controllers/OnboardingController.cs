using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Onboarding;

namespace TyHr.Api.Controllers;

/// <summary>Onboarding checklists and new starters' tasks (HR).</summary>
[ApiController]
[Route("api/onboarding")]
[RequireTenant]
[Authorize(Policy = Permissions.EmployeesManage)]
public class OnboardingController(OnboardingService onboarding) : ControllerBase
{
    [HttpGet("templates")]
    public Task<OnboardingSetupDto> Setup(CancellationToken ct) => onboarding.SetupAsync(ct);

    [HttpPost("templates")]
    public Task<OnboardingTemplateDto> Create(SaveOnboardingTemplateRequest r, CancellationToken ct) => onboarding.CreateTemplateAsync(r, ct);

    [HttpPut("templates/{id:guid}")]
    public Task<OnboardingTemplateDto> Update(Guid id, SaveOnboardingTemplateRequest r, CancellationToken ct) => onboarding.UpdateTemplateAsync(id, r, ct);

    [HttpDelete("templates/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await onboarding.DeleteTemplateAsync(id, ct);
        return NoContent();
    }

    [HttpGet]
    public Task<IReadOnlyList<OnboardingProgressDto>> Progress(CancellationToken ct) => onboarding.ProgressAsync(ct);

    [HttpPost("employees/{employeeId:guid}/start")]
    public Task<OnboardingStartResult> Start(Guid employeeId, StartOnboardingRequest r, CancellationToken ct) => onboarding.StartAsync(employeeId, r, ct);

    [HttpPost("employees/{employeeId:guid}/tasks")]
    public Task<OnboardingTaskDto> Add(Guid employeeId, AddOnboardingTaskRequest r, CancellationToken ct) => onboarding.AddTaskAsync(employeeId, r, ct);

    [HttpPut("tasks/{id:guid}/assignee")]
    public Task<OnboardingTaskDto> Reassign(Guid id, ReassignTaskRequest r, CancellationToken ct) => onboarding.ReassignAsync(id, r.AssigneeId, ct);

    [HttpDelete("tasks/{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid id, CancellationToken ct)
    {
        await onboarding.DeleteTaskAsync(id, ct);
        return NoContent();
    }
}

/// <summary>Tasks anyone may see or tick off when they're theirs (HR: any), and an employee's checklist for those who can open the record.</summary>
[ApiController]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class OnboardingTasksController(OnboardingService onboarding) : ControllerBase
{
    [HttpGet("api/me/tasks")]
    public Task<IReadOnlyList<OnboardingTaskDto>> Mine(CancellationToken ct) => onboarding.MyTasksAsync(ct);

    [HttpGet("api/employees/{employeeId:guid}/onboarding")]
    public Task<IReadOnlyList<OnboardingTaskDto>> ForEmployee(Guid employeeId, CancellationToken ct) => onboarding.TasksAsync(employeeId, ct);

    [HttpPost("api/onboarding/tasks/{id:guid}/complete")]
    public Task<OnboardingTaskDto> Complete(Guid id, CompleteTaskRequest r, CancellationToken ct) => onboarding.CompleteAsync(id, r.Note, ct);

    [HttpPost("api/onboarding/tasks/{id:guid}/reopen")]
    public Task<OnboardingTaskDto> Reopen(Guid id, CancellationToken ct) => onboarding.ReopenAsync(id, ct);
}

/// <summary>End-of-probation reviews: managers recommend (for their reporting tree), HR decides.</summary>
[ApiController]
[Route("api/probation")]
[RequireTenant]
[Authorize(Policy = Permissions.EmployeesView)]
public class ProbationController(ProbationService probation) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProbationDueDto>> Due(CancellationToken ct) => probation.DueAsync(ct);

    [HttpPost("{contractId:guid}/recommend")]
    public Task<ProbationDueDto> Recommend(Guid contractId, RecommendProbationRequest r, CancellationToken ct) => probation.RecommendAsync(contractId, r, ct);

    [HttpPost("{contractId:guid}/decide")]
    [Authorize(Policy = Permissions.EmployeesManage)]
    public Task<ProbationDueDto> Decide(Guid contractId, DecideProbationRequest r, CancellationToken ct) => probation.DecideAsync(contractId, r, ct);
}
