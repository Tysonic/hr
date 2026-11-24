using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Application.Employees;
using TyHr.Application.SelfService;
using TyHr.Domain.SelfService;

namespace TyHr.Api.Controllers;

/// <summary>The signed-in employee's own record, documents and change requests.</summary>
[ApiController]
[Route("api/me")]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class MyProfileController(EmployeeService employees, ProfileChangeService changes) : ControllerBase
{
    [HttpGet("profile")]
    public Task<EmployeeDto> Profile(CancellationToken ct) => employees.MineAsync(ct);

    [HttpGet("documents")]
    public async Task<IReadOnlyList<DocumentDto>> Documents([FromServices] DocumentService documents, CancellationToken ct) =>
        await documents.ListAsync((await employees.MineAsync(ct)).Id, ct);

    [HttpGet("documents/{documentId:guid}")]
    public async Task<IActionResult> Document(Guid documentId, [FromServices] DocumentService documents, CancellationToken ct)
    {
        var d = await documents.DownloadAsync((await employees.MineAsync(ct)).Id, documentId, ct);
        return File(d.Content, d.ContentType, d.FileName);
    }

    [HttpGet("changes")]
    public Task<IReadOnlyList<ProfileChangeDto>> Changes(CancellationToken ct) => changes.MineAsync(ct);

    [HttpPost("changes/personal")]
    public Task<ProfileChangeDto> Personal(RequestPersonalChange r, CancellationToken ct) => changes.RequestPersonalAsync(r, ct);

    [HttpPost("changes/payment")]
    public Task<ProfileChangeDto> Payment(RequestPaymentChange r, CancellationToken ct) => changes.RequestPaymentAsync(r, ct);

    [HttpPost("changes/next-of-kin")]
    public Task<ProfileChangeDto> NextOfKin(RequestNextOfKinChange r, CancellationToken ct) => changes.RequestNextOfKinAsync(r, ct);

    [HttpPost("changes/{id:guid}/withdraw")]
    public Task<ProfileChangeDto> Withdraw(Guid id, CancellationToken ct) => changes.WithdrawAsync(id, ct);

    [HttpGet("team")]
    public Task<IReadOnlyList<TeamMemberDto>> Team([FromServices] TeamService team, CancellationToken ct) => team.MyTeamAsync(ct);

    [HttpGet("inbox")]
    public Task<InboxDto> Inbox([FromServices] TeamService team, CancellationToken ct) => team.InboxAsync(ct);
}

/// <summary>Deciding employees' change requests.</summary>
[ApiController]
[Route("api/profile-changes")]
[RequireTenant]
[Authorize(Policy = Permissions.ProfileChangesDecide)]
public class ProfileChangesController(ProfileChangeService changes) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProfileChangeDto>> List([FromQuery] ProfileChangeStatus? status, CancellationToken ct) => changes.ListAsync(status, ct);

    [HttpGet("{id:guid}")]
    public Task<ProfileChangeDto> Get(Guid id, CancellationToken ct) => changes.GetAsync(id, ct);

    [HttpPost("{id:guid}/approve")]
    public Task<ProfileChangeDto> Approve(Guid id, DecideProfileChange r, CancellationToken ct) => changes.ApproveAsync(id, r.Note, ct);

    [HttpPost("{id:guid}/reject")]
    public Task<ProfileChangeDto> Reject(Guid id, DecideProfileChange r, CancellationToken ct) => changes.RejectAsync(id, r.Note, ct);
}

[ApiController]
[Route("api/announcements")]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class AnnouncementsController(AnnouncementService announcements) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AnnouncementDto>> List([FromQuery] bool all, CancellationToken ct) => announcements.ListAsync(all, ct);

    [HttpPost, Authorize(Policy = Permissions.AnnouncementsManage)]
    public Task<AnnouncementDto> Create(SaveAnnouncementRequest r, CancellationToken ct) => announcements.CreateAsync(r, ct);

    [HttpPut("{id:guid}"), Authorize(Policy = Permissions.AnnouncementsManage)]
    public Task<AnnouncementDto> Update(Guid id, SaveAnnouncementRequest r, CancellationToken ct) => announcements.UpdateAsync(id, r, ct);

    [HttpDelete("{id:guid}"), Authorize(Policy = Permissions.AnnouncementsManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await announcements.DeleteAsync(id, ct);
        return NoContent();
    }
}
