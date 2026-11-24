using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Exits;
using TyHr.Domain.Exits;

namespace TyHr.Api.Controllers;

/// <summary>Exits for HR: resignations to accept, exits to record, clearance, interviews and completion.</summary>
[ApiController]
[Route("api/exits")]
[RequireTenant]
[Authorize(Policy = Permissions.ExitsManage)]
public class ExitsController(ExitService exits) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ExitListItem>> List(bool closed = false, CancellationToken ct = default) => exits.ListAsync(closed, ct);

    [HttpGet("{id:guid}")]
    public Task<ExitDto> Get(Guid id, CancellationToken ct) => exits.GetAsync(id, ct);

    [HttpGet("notice")]
    public Task<NoticeDto> Notice(Guid employeeId, DateOnly givenOn, CancellationToken ct) => exits.NoticeAsync(employeeId, givenOn, ct);

    [HttpGet("employee/{employeeId:guid}")]
    public Task<IReadOnlyList<ExitDto>> ForEmployee(Guid employeeId, CancellationToken ct) => exits.ForEmployeeAsync(employeeId, ct);

    [HttpPost]
    public Task<ExitActionResult> Create(CreateExitRequest r, CancellationToken ct) => exits.CreateAsync(r, ct);

    [HttpPut("{id:guid}")]
    public Task<ExitDto> Update(Guid id, UpdateExitRequest r, CancellationToken ct) => exits.UpdateAsync(id, r, ct);

    [HttpPost("{id:guid}/accept")]
    public Task<ExitActionResult> Accept(Guid id, AcceptResignationRequest r, CancellationToken ct) => exits.AcceptAsync(id, r, ct);

    [HttpPost("{id:guid}/withdraw")]
    public Task<ExitDto> Withdraw(Guid id, WithdrawExitRequest r, CancellationToken ct) => exits.WithdrawAsync(id, r.Note, ct);

    [HttpPost("{id:guid}/complete")]
    public Task<ExitActionResult> Complete(Guid id, CompleteExitRequest r, CancellationToken ct) => exits.CompleteAsync(id, r, ct);

    [HttpPut("{id:guid}/interview")]
    public Task<ExitDto> Interview(Guid id, InterviewAnswers r, CancellationToken ct) => exits.InterviewAsync(id, r, ct);

    [HttpPost("{id:guid}/clearance")]
    public Task<ExitDto> AddItem(Guid id, AddClearanceRequest r, CancellationToken ct) => exits.AddItemAsync(id, r, ct);

    [HttpDelete("clearance/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid itemId, CancellationToken ct)
    {
        await exits.DeleteItemAsync(itemId, ct);
        return NoContent();
    }
}

/// <summary>Former employees and the retention policy (changing it and removing data: HR admins, checked in the service).</summary>
[ApiController]
[Route("api/former-employees")]
[RequireTenant]
[Authorize(Policy = Permissions.ExitsManage)]
public class ArchiveController(ArchiveService archive) : ControllerBase
{
    [HttpGet]
    public Task<ArchiveDto> List(CancellationToken ct) => archive.ListAsync(ct);

    [HttpPut("retention")]
    public Task<ArchiveDto> Retention(RetentionRequest r, CancellationToken ct) => archive.SetRetentionAsync(r.Years, ct);

    [HttpPost("{employeeId:guid}/anonymise")]
    public Task<ArchiveDto> Anonymise(Guid employeeId, CancellationToken ct) => archive.AnonymiseAsync(employeeId, ct);
}

/// <summary>The certificate of service for a completed exit.</summary>
[ApiController]
[Route("api/exits/{id:guid}/certificate")]
[RequireTenant]
[Authorize(Policy = Permissions.ExitsManage)]
public class CertificateController(CertificateService certificates) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var file = await certificates.RenderAsync(id, ct);
        return File(file.Content, "application/pdf", file.FileName);
    }
}

/// <summary>A leaver's final dues (people who may see pay); sending them to a payroll needs payroll preparation too.</summary>
[ApiController]
[Route("api/exits/{id:guid}/final-dues")]
[RequireTenant]
[Authorize(Policy = Permissions.EmployeesPay)]
public class FinalDuesController(FinalDuesService dues) : ControllerBase
{
    [HttpGet]
    public Task<FinalDuesDto> Get(Guid id, CancellationToken ct) => dues.GetAsync(id, ct);

    [HttpPut]
    public Task<FinalDuesDto> Save(Guid id, SaveDuesRequest r, CancellationToken ct) => dues.SaveAsync(id, r.Lines, ct);

    [HttpPost("recalculate")]
    public Task<FinalDuesDto> Recalculate(Guid id, CancellationToken ct) => dues.RecalculateAsync(id, ct);

    [HttpPost("payroll")]
    public Task<FinalDuesDto> SendToPayroll(Guid id, CancellationToken ct) => dues.SendToPayrollAsync(id, ct);
}

/// <summary>The leaver's own exit, and clearance items given to the signed-in user.</summary>
[ApiController]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class MyExitController(ExitService exits) : ControllerBase
{
    [HttpGet("api/me/exit")]
    public async Task<ActionResult<MyExitDto>> Mine(CancellationToken ct) => await exits.MineAsync(ct) is { } x ? x : NoContent();

    [HttpPost("api/me/resignation")]
    public Task<MyExitDto> Resign(ResignRequest r, CancellationToken ct) => exits.ResignAsync(r, ct);

    [HttpPost("api/me/exit/{id:guid}/withdraw")]
    public Task<ExitDto> Withdraw(Guid id, WithdrawExitRequest r, CancellationToken ct) => exits.WithdrawAsync(id, r.Note, ct);

    [HttpPut("api/me/exit/interview")]
    public Task<MyExitDto> Interview(InterviewAnswers r, CancellationToken ct) => exits.MyInterviewAsync(r, ct);

    [HttpGet("api/me/clearance")]
    public Task<IReadOnlyList<ClearanceItemDto>> Clearance(CancellationToken ct) => exits.MyItemsAsync(ct);

    [HttpPost("api/me/clearance/{id:guid}/clear")]
    public Task<ClearanceItemDto> Clear(Guid id, ClearItemRequest r, CancellationToken ct) => exits.ClearAsync(id, r.Note, ct);

    [HttpPost("api/me/clearance/{id:guid}/reopen")]
    public Task<ClearanceItemDto> Reopen(Guid id, CancellationToken ct) => exits.ReopenItemAsync(id, ct);
}
