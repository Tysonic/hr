using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Onboarding;

namespace TyHr.Application.Onboarding;

/// <summary>
/// End-of-probation reviews. Probations ending within <see cref="WindowDays"/> days (or ended in the last
/// <see cref="LookBackDays"/>) and not yet confirmed are due. The line manager (or anyone above them) recommends confirming, extending or not confirming;
/// HR decides, never on their own record. Confirming sets the contract's confirmation date and extending moves its
/// probation end; the employee is told either way.
/// </summary>
public class ProbationService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, IEmailSender email,
    TimeProvider clock, ILogger<ProbationService> logger)
{
    public const int WindowDays = 30;

    /// <summary>Probations that ended longer ago than this without a decision are old records, not reviews to chase.</summary>
    public const int LookBackDays = 90;

    private bool IsHr => user.Can(Permissions.EmployeesManage);

    /// <summary>Probations due for review that the user can act on: all for HR, their reporting tree for a manager.</summary>
    public async Task<IReadOnlyList<ProbationDueDto>> DueAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var today = await TodayAsync(ct);
        var until = today.AddDays(WindowDays);
        var from = today.AddDays(-LookBackDays);
        var contracts = await db.EmploymentContracts.AsNoTracking()
            .Where(c => c.ConfirmedOn == null && c.ProbationEndDate != null && c.ProbationEndDate <= until && c.ProbationEndDate >= from && c.StartDate <= until)
            .Join(db.Employees.AsNoTracking().Where(e => e.Status != EmployeeStatus.Exited), c => c.EmployeeId, e => e.Id, (c, e) => new { c, e })
            .ToListAsync(ct);
        var team = IsHr ? null : await access.TeamAsync(ct);
        if (team != null) contracts = contracts.Where(x => team.Contains(x.e.Id)).ToList();
        if (contracts.Count == 0) return [];
        // A contract that has been followed by a later one is history, not a probation to review.
        var contractIds = contracts.Select(x => x.c.Id).ToList();
        var reviews = (await db.ProbationReviews.AsNoTracking().Where(r => contractIds.Contains(r.ContractId)).OrderBy(r => r.RecommendedAt).ToListAsync(ct))
            .ToLookup(r => r.ContractId);
        var notConfirmed = reviews.SelectMany(g => g).Where(r => r.Status == ProbationReviewStatus.NotConfirmed).Select(r => r.ContractId).ToHashSet();
        var me = await access.MeAsync(ct);
        var names = await NamesAsync(contracts.Select(x => x.e.ManagerId).Concat(contracts.Select(x => x.e.PositionId)).ToList(), ct);
        return contracts.Where(x => !notConfirmed.Contains(x.c.Id)).Select(x =>
        {
            var history = reviews[x.c.Id].Select(ToDto).ToList();
            var open = history.LastOrDefault(r => r.Status == ProbationReviewStatus.AwaitingHr);
            return new ProbationDueDto(x.e.Id, x.e.FullName, x.e.EmployeeNumber, x.e.PositionId is { } p ? names.Positions.GetValueOrDefault(p) : null,
                x.e.ManagerId is { } m ? names.People.GetValueOrDefault(m) : null, x.c.Id, x.c.StartDate, x.c.ProbationEndDate!.Value,
                x.c.ProbationEndDate.Value.DayNumber - today.DayNumber, open, history,
                x.e.Id != me && (team?.Contains(x.e.Id) ?? true), IsHr && x.e.Id != me);
        }).OrderBy(d => d.ProbationEndDate).ToList();
    }

    private async Task<(Dictionary<Guid, string> People, Dictionary<Guid, string> Positions)> NamesAsync(List<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => list.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        var positions = await db.Positions.AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        return (people, positions);
    }

    /// <summary>How many probations wait for the user: HR's decisions, or a manager's recommendations.</summary>
    public async Task<int> WaitingCountAsync(CancellationToken ct)
    {
        var due = await DueAsync(ct);
        return IsHr ? due.Count(d => d.CanDecide && d.Review != null) : due.Count(d => d.CanRecommend && d.Review == null);
    }

    public async Task<ProbationDueDto> RecommendAsync(Guid contractId, RecommendProbationRequest r, CancellationToken ct)
    {
        var (contract, employee) = await DueContractAsync(contractId, ct);
        if (employee.Id == await access.MeAsync(ct)) throw new DomainException("You can't review your own probation.");
        if (!IsHr && !await access.InTeamAsync(employee.Id, ct)) throw new NotFoundException("There is no such probation to review.");
        if (r.Recommendation == ProbationOutcome.Extend && r.ExtendTo > contract.EndDate) throw new DomainException("Probation must end within the contract.");
        var review = await OpenReviewAsync(contractId, ct);
        if (review == null)
            db.ProbationReviews.Add(new ProbationReview(employee.Id, contractId, contract.ProbationEndDate!.Value, r.Recommendation, r.ExtendTo, r.Comments,
                user.UserName, clock.GetUtcNow()));
        else review.Recommend(r.Recommendation, r.ExtendTo, r.Comments, user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ItemAsync(contractId, ct);
    }

    /// <summary>HR's decision. Without a manager's recommendation, HR's note serves as the review.</summary>
    public async Task<ProbationDueDto> DecideAsync(Guid contractId, DecideProbationRequest r, CancellationToken ct)
    {
        var (contract, employee) = await DueContractAsync(contractId, ct);
        if (employee.Id == await access.MeAsync(ct)) throw new DomainException("Someone else in HR must decide on your own probation.");
        var review = await OpenReviewAsync(contractId, ct);
        if (review == null)
        {
            var note = Guard.Optional(r.Note) ?? throw new DomainException("Without a manager's review, add a note on why.");
            review = new ProbationReview(employee.Id, contractId, contract.ProbationEndDate!.Value, r.Outcome, r.ExtendTo, note, user.UserName, clock.GetUtcNow());
            db.ProbationReviews.Add(review);
        }
        var today = await TodayAsync(ct);
        switch (r.Outcome)
        {
            case ProbationOutcome.Confirm:
                contract.Confirm(today < contract.StartDate ? contract.StartDate : today);
                break;
            case ProbationOutcome.Extend:
                if (r.ExtendTo is not { } to) throw new DomainException("Give the new probation end date.");
                contract.ExtendProbation(to);
                break;
        }
        review.Decide(r.Outcome, r.ExtendTo, r.Note, user.UserName, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await TellEmployeeAsync(employee, contract, r.Outcome, ct);
        var after = (await DueAsync(ct)).SingleOrDefault(d => d.ContractId == contractId);
        return after ?? new ProbationDueDto(employee.Id, employee.FullName, employee.EmployeeNumber, null, null, contractId, contract.StartDate,
            contract.ProbationEndDate!.Value, contract.ProbationEndDate.Value.DayNumber - today.DayNumber, null,
            (await db.ProbationReviews.AsNoTracking().Where(x => x.ContractId == contractId).OrderBy(x => x.RecommendedAt).ToListAsync(ct)).Select(ToDto).ToList(),
            false, false);
    }

    private async Task TellEmployeeAsync(Employee e, EmploymentContract c, ProbationOutcome outcome, CancellationToken ct)
    {
        if (outcome == ProbationOutcome.NotConfirm || e.WorkEmail == null) return; // that conversation is for HR to have in person
        var company = await db.CompanyProfiles.AsNoTracking().Select(p => p.TradingName ?? p.LegalName).SingleAsync(ct);
        var text = outcome == ProbationOutcome.Confirm
            ? $"Dear {e.FirstName},\n\nWe're pleased to confirm your employment with {company}: you have completed your probation. Congratulations.\n\n{company} HR"
            : $"Dear {e.FirstName},\n\nYour probation has been extended to {c.ProbationEndDate:d MMMM yyyy}. Your manager will talk you through what to focus on.\n\n{company} HR";
        var subject = outcome == ProbationOutcome.Confirm ? "Your employment is confirmed" : "Your probation has been extended";
        try
        {
            await email.SendAsync(new EmailMessage([e.WorkEmail], subject, string.Join("", text.Split('\n').Select(l => $"<p>{WebUtility.HtmlEncode(l)}</p>")), text), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not email the probation decision.");
        }
    }

    private async Task<ProbationDueDto> ItemAsync(Guid contractId, CancellationToken ct) =>
        (await DueAsync(ct)).SingleOrDefault(d => d.ContractId == contractId) ?? throw new NotFoundException("There is no such probation to review.");

    private async Task<(EmploymentContract, Employee)> DueContractAsync(Guid contractId, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var contract = await db.EmploymentContracts.SingleOrDefaultAsync(c => c.Id == contractId, ct);
        if (contract?.ProbationEndDate == null) throw new NotFoundException("There is no such probation to review.");
        if (contract.ConfirmedOn != null) throw new DomainException("The employee is already confirmed.");
        if (await db.ProbationReviews.AnyAsync(r => r.ContractId == contractId && r.Status == ProbationReviewStatus.NotConfirmed, ct))
            throw new DomainException("HR has decided not to confirm this employee.");
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == contract.EmployeeId, ct);
        return (contract, employee);
    }

    private Task<ProbationReview?> OpenReviewAsync(Guid contractId, CancellationToken ct) =>
        db.ProbationReviews.SingleOrDefaultAsync(r => r.ContractId == contractId && r.Status == ProbationReviewStatus.AwaitingHr, ct);

    private static ProbationReviewDto ToDto(ProbationReview r) => new(r.Id, r.Recommendation, r.RecommendedExtendTo, r.Comments, r.RecommendedBy,
        r.RecommendedAt, r.Status, r.ExtendedTo, r.DecidedBy, r.DecidedAt, r.DecisionNote, r.ProbationEndDate);

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);
}
