using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Recruitment;
using TyHr.Domain.Recruitment;

namespace TyHr.Api.Controllers;

[ApiController]
[Route("api/recruitment/requisitions")]
[RequireTenant]
[Authorize(Policy = Permissions.RecruitmentRequest)]
public class RequisitionsController(RecruitmentService recruitment) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RequisitionDto>> List([FromQuery] RequisitionStatus? status, CancellationToken ct) => recruitment.RequisitionsAsync(status, ct);

    [HttpGet("{id:guid}")]
    public Task<RequisitionDto> Get(Guid id, CancellationToken ct) => recruitment.RequisitionAsync(id, ct);

    [HttpPost]
    public Task<RequisitionDto> Create(RequisitionDetails r, CancellationToken ct) => recruitment.CreateRequisitionAsync(r, ct);

    [HttpPut("{id:guid}")]
    public Task<RequisitionDto> Update(Guid id, RequisitionDetails r, CancellationToken ct) => recruitment.UpdateRequisitionAsync(id, r, ct);

    [HttpPost("{id:guid}/approve"), Authorize(Policy = Permissions.RecruitmentApprove)]
    public Task<RequisitionDto> Approve(Guid id, DecideRequest r, CancellationToken ct) => recruitment.ApproveRequisitionAsync(id, r.Note, ct);

    [HttpPost("{id:guid}/reject"), Authorize(Policy = Permissions.RecruitmentApprove)]
    public Task<RequisitionDto> Reject(Guid id, DecideRequest r, CancellationToken ct) => recruitment.RejectRequisitionAsync(id, r.Note, ct);

    [HttpPost("{id:guid}/close")]
    public Task<RequisitionDto> Close(Guid id, DecideRequest r, CancellationToken ct) => recruitment.CloseRequisitionAsync(id, r.Note, ct);
}

[ApiController]
[Route("api/recruitment")]
[RequireTenant]
[Authorize(Policy = Permissions.RecruitmentManage)]
public class JobPostingsController(RecruitmentService recruitment) : ControllerBase
{
    [HttpGet("postings")]
    public Task<IReadOnlyList<PostingDto>> Postings(CancellationToken ct) => recruitment.PostingsAsync(ct);

    [HttpGet("postings/{id:guid}")]
    public Task<PostingDto> Posting(Guid id, CancellationToken ct) => recruitment.PostingAsync(id, ct);

    [HttpPost("postings")]
    public Task<PostingDto> Create(CreatePostingRequest r, CancellationToken ct) => recruitment.CreatePostingAsync(r, ct);

    [HttpPut("postings/{id:guid}")]
    public Task<PostingDto> Update(Guid id, PostingDetails r, CancellationToken ct) => recruitment.UpdatePostingAsync(id, r, ct);

    [HttpPost("postings/{id:guid}/publish")]
    public Task<PostingDto> Publish(Guid id, CancellationToken ct) => recruitment.PublishPostingAsync(id, ct);

    [HttpPost("postings/{id:guid}/close")]
    public Task<PostingDto> Close(Guid id, CancellationToken ct) => recruitment.ClosePostingAsync(id, ct);

    [HttpGet("postings/{id:guid}/applications")]
    public Task<IReadOnlyList<ApplicationSummaryDto>> Applications(Guid id, CancellationToken ct) => recruitment.ApplicationsAsync(id, ct);

    [HttpPost("postings/{id:guid}/applications")]
    [RequestSizeLimit(JobApplication.MaxCvBytes + 64 * 1024)]
    public Task<ApplicationDto> AddApplication(Guid id, [FromForm] ApplicationForm form, CancellationToken ct) =>
        recruitment.AddApplicationAsync(id, form.Candidate(), form.Cv?.FileName, form.Cv?.OpenReadStream(), ct);

    [HttpGet("applications/{id:guid}")]
    public Task<ApplicationDto> Application(Guid id, CancellationToken ct) => recruitment.ApplicationAsync(id, ct);

    [HttpPost("applications/{id:guid}/move")]
    public Task<ApplicationDto> Move(Guid id, MoveApplicationRequest r, CancellationToken ct) => recruitment.MoveAsync(id, r, ct);

    [HttpPost("applications/{id:guid}/rate")]
    public Task<ApplicationDto> Rate(Guid id, RateApplicationRequest r, CancellationToken ct) => recruitment.RateAsync(id, r.Rating, ct);

    [HttpPost("applications/{id:guid}/notes")]
    public Task<ApplicationDto> Note(Guid id, AddNoteRequest r, CancellationToken ct) => recruitment.AddNoteAsync(id, r.Text, ct);

    [HttpGet("applications/{id:guid}/cv")]
    public async Task<IActionResult> Cv(Guid id, CancellationToken ct)
    {
        var cv = await recruitment.CvAsync(id, ct);
        return File(cv.Content, cv.ContentType, cv.FileName);
    }
}

/// <summary>A candidate's details and CV as a multipart form.</summary>
public class ApplicationForm
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? CoverLetter { get; set; }
    public bool Consent { get; set; }

    /// <summary>A field people can't see; bots fill it in.</summary>
    public string? Website { get; set; }

    public IFormFile? Cv { get; set; }

    public CandidateDetails Candidate() => new(FirstName, LastName, Email, Phone, CoverLetter);
}

/// <summary>
/// The public careers page: open jobs and applying, without signing in. Applying is rate-limited per address and
/// company, and a hidden field turns away simple bots.
/// </summary>
[ApiController]
[Route("api/public/careers/{slug}")]
[AllowAnonymous]
public class CareersController(CareersService careers) : ControllerBase
{
    public const string ApplyLimit = "careers-apply";

    [HttpGet]
    public Task<CareersDto> Jobs(string slug, CancellationToken ct) => careers.JobsAsync(slug, ct);

    [HttpGet("jobs/{id:guid}")]
    public Task<PublicPostingDto> Job(string slug, Guid id, CancellationToken ct) => careers.JobAsync(slug, id, ct);

    [HttpPost("jobs/{id:guid}/apply")]
    [EnableRateLimiting(ApplyLimit)]
    [RequestSizeLimit(JobApplication.MaxCvBytes + 64 * 1024)]
    public async Task<IActionResult> Apply(string slug, Guid id, [FromForm] ApplicationForm form, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(form.Website)) return NoContent(); // looks accepted, isn't saved
        await careers.ApplyAsync(slug, id, form.Candidate(), form.Consent, form.Cv?.FileName, form.Cv?.OpenReadStream(), ct);
        return NoContent();
    }

    /// <summary>At most 10 applications per 10 minutes from one address to one company.</summary>
    public static void AddLimits(RateLimiterOptions o)
    {
        o.RejectionStatusCode = 429;
        o.AddPolicy(ApplyLimit, context => RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Request.RouteValues["slug"]}|{context.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10) }));
    }
}

/// <summary>Interviews, offers and hiring (HR), and the offer letter wording.</summary>
[ApiController]
[Route("api/recruitment")]
[RequireTenant]
[Authorize(Policy = Permissions.RecruitmentManage)]
public class HiringController(HiringService hiring) : ControllerBase
{
    [HttpGet("applications/{id:guid}/interviews")]
    public Task<IReadOnlyList<InterviewDto>> Interviews(Guid id, CancellationToken ct) => hiring.InterviewsAsync(id, ct);

    [HttpPost("applications/{id:guid}/interviews")]
    public Task<InterviewDto> Schedule(Guid id, ScheduleInterviewRequest r, CancellationToken ct) => hiring.ScheduleAsync(id, r, ct);

    [HttpPut("interviews/{id:guid}")]
    public Task<InterviewDto> Reschedule(Guid id, ScheduleInterviewRequest r, CancellationToken ct) => hiring.RescheduleAsync(id, r, ct);

    [HttpPost("interviews/{id:guid}/cancel")]
    public Task<InterviewDto> Cancel(Guid id, CancellationToken ct) => hiring.CancelInterviewAsync(id, ct);

    [HttpPost("interviews/{id:guid}/complete")]
    public Task<InterviewDto> Complete(Guid id, CancellationToken ct) => hiring.CompleteInterviewAsync(id, ct);

    [HttpGet("applications/{id:guid}/offers")]
    public Task<IReadOnlyList<OfferDto>> Offers(Guid id, CancellationToken ct) => hiring.OffersAsync(id, ct);

    [HttpPost("applications/{id:guid}/offers")]
    public Task<OfferDto> CreateOffer(Guid id, OfferTerms r, CancellationToken ct) => hiring.CreateOfferAsync(id, r, ct);

    [HttpPut("offers/{id:guid}")]
    public Task<OfferDto> UpdateOffer(Guid id, OfferTerms r, CancellationToken ct) => hiring.UpdateOfferAsync(id, r, ct);

    [HttpGet("offers/{id:guid}/letter")]
    public async Task<IActionResult> Letter(Guid id, CancellationToken ct)
    {
        var letter = await hiring.OfferLetterAsync(id, ct);
        return File(letter.Content, "application/pdf", letter.FileName);
    }

    [HttpPost("offers/{id:guid}/send")]
    public Task<OfferDto> Send(Guid id, SendOfferRequest r, CancellationToken ct) => hiring.SendOfferAsync(id, r.EmailCandidate, ct);

    [HttpPost("offers/{id:guid}/respond")]
    public Task<OfferDto> Respond(Guid id, OfferResponseRequest r, CancellationToken ct) => hiring.RespondAsync(id, r, ct);

    [HttpPost("offers/{id:guid}/withdraw")]
    public Task<OfferDto> Withdraw(Guid id, WithdrawOfferRequest r, CancellationToken ct) => hiring.WithdrawOfferAsync(id, r.Note, ct);

    [HttpPost("applications/{id:guid}/hire")]
    public Task<HireResult> Hire(Guid id, HireRequest r, CancellationToken ct) => hiring.HireAsync(id, r, ct);

    [HttpGet("offer-template")]
    public Task<OfferTemplateDto> Template(CancellationToken ct) => hiring.TemplateAsync(ct);

    [HttpPut("offer-template")]
    public Task<OfferTemplateDto> SaveTemplate(SaveOfferTemplateRequest r, CancellationToken ct) => hiring.SaveTemplateAsync(r.Body, ct);
}

/// <summary>Interviews the signed-in employee sits on: the candidate, their CV and the scorecard.</summary>
[ApiController]
[Route("api/me/interviews")]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class MyInterviewsController(HiringService hiring) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<InterviewDto>> List(CancellationToken ct) => hiring.MyInterviewsAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<InterviewDto> Get(Guid id, CancellationToken ct) => hiring.InterviewAsync(id, ct);

    [HttpGet("{id:guid}/cv")]
    public async Task<IActionResult> Cv(Guid id, CancellationToken ct)
    {
        var cv = await hiring.InterviewCvAsync(id, ct);
        return File(cv.Content, cv.ContentType, cv.FileName);
    }

    [HttpPut("{id:guid}/scorecard")]
    public Task<InterviewDto> Scorecard(Guid id, SubmitScorecardRequest r, CancellationToken ct) => hiring.SubmitScorecardAsync(id, r, ct);
}
