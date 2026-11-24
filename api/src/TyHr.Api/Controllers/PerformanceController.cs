using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TyHr.Api.Infrastructure;
using TyHr.Application.Common;
using TyHr.Application.Performance;
using TyHr.Domain.Performance;

namespace TyHr.Api.Controllers;

/// <summary>Review cycles (HR).</summary>
[ApiController]
[Route("api/performance/cycles")]
[RequireTenant]
[Authorize(Policy = Permissions.PerformanceManage)]
public class ReviewCyclesController(PerformanceService performance) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<CycleSummaryDto>> List(CancellationToken ct) => performance.CyclesAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<CycleDto> Get(Guid id, CancellationToken ct) => performance.CycleAsync(id, ct);

    [HttpPost]
    public Task<CycleDto> Create(CycleDetails d, CancellationToken ct) => performance.CreateCycleAsync(d, ct);

    [HttpPut("{id:guid}")]
    public Task<CycleDto> Update(Guid id, CycleDetails d, CancellationToken ct) => performance.UpdateCycleAsync(id, d, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await performance.DeleteCycleAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/launch")]
    public Task<CycleActionResult> Launch(Guid id, CancellationToken ct) => performance.LaunchAsync(id, ct);

    [HttpPost("{id:guid}/start-reviews")]
    public Task<CycleActionResult> StartReviews(Guid id, CancellationToken ct) => performance.StartReviewsAsync(id, ct);

    [HttpPost("{id:guid}/close")]
    public Task<CycleActionResult> Close(Guid id, CancellationToken ct) => performance.CloseAsync(id, ct);

    [HttpPost("{id:guid}/release")]
    public Task<CycleActionResult> Release(Guid id, CancellationToken ct) => performance.ReleaseResultsAsync(id, ct);
}

/// <summary>A cycle's salary review (people who may see pay).</summary>
[ApiController]
[Route("api/performance/cycles/{cycleId:guid}/salary-review")]
[RequireTenant]
[Authorize(Policy = Permissions.EmployeesPay)]
public class SalaryReviewController(SalaryReviewService reviews) : ControllerBase
{
    [HttpGet]
    public Task<SalaryReviewDto> Get(Guid cycleId, CancellationToken ct) => reviews.GetAsync(cycleId, ct);

    [HttpPut]
    public Task<SalaryReviewDto> Guideline(Guid cycleId, SaveGuidelineRequest r, CancellationToken ct) => reviews.SaveGuidelineAsync(cycleId, r, ct);

    [HttpPut("recommendations/{id:guid}")]
    public Task<SalaryReviewDto> Adjust(Guid id, AdjustRecommendationRequest r, CancellationToken ct) => reviews.AdjustAsync(id, r, ct);

    [HttpPost("apply")]
    public Task<ApplyRaisesResult> Apply(Guid cycleId, CancellationToken ct) => reviews.ApplyAsync(cycleId, ct);
}

/// <summary>Improvement plans: HR, the employee's managers, and the employee (service checks).</summary>
[ApiController]
[Route("api/improvement-plans")]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class ImprovementPlansController(ImprovementPlanService plans) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PlanListItem>> List(CancellationToken ct) => plans.ListAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<PlanDto> Get(Guid id, CancellationToken ct) => plans.GetAsync(id, ct);

    [HttpPost]
    public Task<PlanDto> Create(CreatePlanRequest r, CancellationToken ct) => plans.CreateAsync(r, ct);

    [HttpPut("{id:guid}")]
    public Task<PlanDto> Update(Guid id, UpdatePlanRequest r, CancellationToken ct) => plans.UpdateAsync(id, r.Details, r.OwnerId, ct);

    [HttpPost("{id:guid}/check-ins")]
    public Task<PlanDto> CheckIn(Guid id, AddCheckInRequest r, CancellationToken ct) => plans.CheckInAsync(id, r, ct);

    [HttpPost("{id:guid}/close")]
    public Task<PlanDto> Close(Guid id, ClosePlanRequest r, CancellationToken ct) => plans.CloseAsync(id, r, ct);

    [HttpPost("{id:guid}/acknowledge")]
    public Task<PlanDto> Acknowledge(Guid id, PlanAcknowledgeRequest r, CancellationToken ct) => plans.AcknowledgeAsync(id, r.Comment, ct);
}

public record UpdatePlanRequest(PlanDetails Details, Guid? OwnerId);

/// <summary>Appraisals, for the employee, their manager and HR (the service checks who may do what).</summary>
[ApiController]
[RequireTenant]
[Authorize(Policy = Permissions.SelfService)]
public class AppraisalsController(PerformanceService performance) : ControllerBase
{
    [HttpGet("api/me/appraisals")]
    public Task<MyAppraisalsDto> Mine(CancellationToken ct) => performance.MineAsync(ct);

    [HttpGet("api/appraisals/{id:guid}")]
    public Task<AppraisalDto> Get(Guid id, CancellationToken ct) => performance.AppraisalAsync(id, ct);

    [HttpPut("api/appraisals/{id:guid}/goals")]
    public Task<AppraisalDto> Goals(Guid id, IReadOnlyList<SaveGoal> goals, CancellationToken ct) => performance.SaveGoalsAsync(id, goals, ct);

    [HttpPost("api/appraisals/{id:guid}/submit-goals")]
    public Task<AppraisalDto> SubmitGoals(Guid id, CancellationToken ct) => performance.SubmitGoalsAsync(id, ct);

    [HttpPost("api/appraisals/{id:guid}/agree-goals")]
    public Task<AppraisalDto> AgreeGoals(Guid id, CancellationToken ct) => performance.AgreeGoalsAsync(id, ct);

    [HttpPost("api/appraisals/{id:guid}/reopen-goals")]
    public Task<AppraisalDto> ReopenGoals(Guid id, CancellationToken ct) => performance.ReopenGoalsAsync(id, ct);

    [HttpPost("api/appraisals/{id:guid}/self-review")]
    public Task<AppraisalDto> SelfReview(Guid id, SaveReviewRequest r, CancellationToken ct) => performance.SelfReviewAsync(id, r, ct);

    [HttpPost("api/appraisals/{id:guid}/manager-review")]
    public Task<AppraisalDto> ManagerReview(Guid id, SaveReviewRequest r, CancellationToken ct) => performance.ManagerReviewAsync(id, r, ct);

    [HttpPost("api/appraisals/{id:guid}/acknowledge")]
    public Task<AppraisalDto> Acknowledge(Guid id, AcknowledgeRequest r, CancellationToken ct) => performance.AcknowledgeAsync(id, r.Comment, ct);

    [HttpPost("api/appraisals/{id:guid}/feedback")]
    public Task<AppraisalDto> RequestFeedback(Guid id, RequestFeedbackRequest r, CancellationToken ct) => performance.RequestFeedbackAsync(id, r.ReviewerIds, ct);

    [HttpGet("api/me/feedback/{id:guid}")]
    public Task<FeedbackRequestDto> Feedback(Guid id, CancellationToken ct) => performance.FeedbackRequestAsync(id, ct);

    [HttpPost("api/me/feedback/{id:guid}")]
    public Task<FeedbackRequestDto> GiveFeedback(Guid id, SubmitFeedbackRequest r, CancellationToken ct) => performance.SubmitFeedbackAsync(id, r, ct);

    [HttpPost("api/me/feedback/{id:guid}/decline")]
    public Task<FeedbackRequestDto> DeclineFeedback(Guid id, CancellationToken ct) => performance.DeclineFeedbackAsync(id, ct);

    [HttpPut("api/appraisals/{id:guid}/final-rating")]
    [Authorize(Policy = Permissions.PerformanceManage)]
    public Task<AppraisalDto> FinalRating(Guid id, FinalRatingRequest r, CancellationToken ct) => performance.SetFinalRatingAsync(id, r, ct);

    [HttpPut("api/appraisals/{id:guid}/manager")]
    [Authorize(Policy = Permissions.PerformanceManage)]
    public Task<AppraisalDto> ChangeManager(Guid id, ChangeManagerRequest r, CancellationToken ct) => performance.ChangeManagerAsync(id, r.ManagerId, ct);
}
