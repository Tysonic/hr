using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Performance;

namespace TyHr.Application.Performance;

public record SalaryRecommendationDto(Guid Id, Guid EmployeeId, string EmployeeName, string EmployeeNumber, string? DepartmentName, decimal Rating,
    decimal CurrentSalary, decimal Percent, decimal ProposedSalary, decimal Increase, bool Adjusted, string? Note, RecommendationStatus Status,
    DateTimeOffset? AppliedAt, string? AppliedBy);

/// <summary>A cycle's salary review: the guideline, each completed review's recommendation, and who was left out and why.</summary>
public record SalaryReviewDto(Guid CycleId, string CycleName, bool Exists, IReadOnlyList<decimal> Percents, DateOnly EffectiveDate, IReadOnlyList<string> Scale,
    IReadOnlyList<SalaryRecommendationDto> Recommendations, IReadOnlyList<string> Left, decimal CurrentTotal, decimal ProposedTotal, string CurrencyCode,
    bool CanApply);

public record SaveGuidelineRequest(IReadOnlyList<decimal> Percents, DateOnly EffectiveDate);
public record AdjustRecommendationRequest(decimal? ProposedSalary, string? Note, bool Skip);
public record ApplyRaisesResult(int Applied, SalaryReviewDto Review);

/// <summary>
/// Turns a cycle's final ratings into raises: an increase per rating from the guideline, adjustable per person, then
/// applied as salary changes effective on the review's date. Pay data, so for people who may see pay only.
/// </summary>
public class SalaryReviewService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, EmployeeService employees,
    TimeProvider clock)
{
    public async Task<SalaryReviewDto> GetAsync(Guid cycleId, CancellationToken ct)
    {
        var cycle = await CycleAsync(cycleId, ct);
        var review = await db.SalaryReviews.AsNoTracking().SingleOrDefaultAsync(r => r.CycleId == cycleId, ct);
        return await ToDtoAsync(cycle, review, ct);
    }

    /// <summary>Saves the guideline and brings every recommendation not yet applied in line with it and with today's salaries.</summary>
    public async Task<SalaryReviewDto> SaveGuidelineAsync(Guid cycleId, SaveGuidelineRequest r, CancellationToken ct)
    {
        var cycle = await CycleAsync(cycleId, ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        if (r.EffectiveDate <= cycle.PeriodStart) throw new DomainException("Raises take effect after the review period starts.");
        var review = await db.SalaryReviews.SingleOrDefaultAsync(x => x.CycleId == cycleId, ct);
        if (review == null) db.SalaryReviews.Add(review = new SalaryReview(cycleId, r.Percents, r.EffectiveDate));
        else review.Update(r.Percents, r.EffectiveDate);

        var completed = await db.Appraisals.AsNoTracking().Where(a => a.CycleId == cycleId && a.Status == AppraisalStatus.Completed && a.FinalRating != null).ToListAsync(ct);
        var salaries = await CurrentSalariesAsync(completed.Select(a => a.EmployeeId).ToList(), r.EffectiveDate, ct);
        var existing = await db.SalaryRecommendations.Where(x => x.ReviewId == review.Id).ToDictionaryAsync(x => x.EmployeeId, ct);
        foreach (var a in completed)
        {
            if (!salaries.TryGetValue(a.EmployeeId, out var current)) continue;
            if (!existing.TryGetValue(a.EmployeeId, out var rec))
                db.SalaryRecommendations.Add(rec = new SalaryRecommendation(review.Id, a.EmployeeId, a.FinalRating!.Value, current));
            if (rec.Status != RecommendationStatus.Applied) rec.FromGuideline(review, country, current);
        }
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(cycle, review, ct);
    }

    public async Task<SalaryReviewDto> AdjustAsync(Guid id, AdjustRecommendationRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        CheckPay();
        var rec = await db.SalaryRecommendations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("There is no such recommendation.");
        var review = await db.SalaryReviews.SingleAsync(x => x.Id == rec.ReviewId, ct);
        var cycle = await CycleAsync(review.CycleId, ct);
        if (r.Skip) rec.Skip(r.Note);
        else
        {
            rec.Unskip();
            if (r.ProposedSalary is { } p) rec.Adjust(p, r.Note, await db.CompanyCountryAsync(tenantContext, ct));
        }
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(cycle, review, ct);
    }

    /// <summary>Applies every proposed raise as a salary change effective on the review's date, all or none.</summary>
    public async Task<ApplyRaisesResult> ApplyAsync(Guid cycleId, CancellationToken ct)
    {
        var cycle = await CycleAsync(cycleId, ct);
        var review = await db.SalaryReviews.SingleOrDefaultAsync(x => x.CycleId == cycleId, ct) ?? throw new DomainException("Set the guideline first.");
        var recs = await db.SalaryRecommendations.Where(x => x.ReviewId == review.Id && x.Status == RecommendationStatus.Proposed).ToListAsync(ct);
        var raises = recs.Where(x => x.ProposedSalary > x.CurrentSalary).ToList();
        if (raises.Count == 0) throw new DomainException("There are no raises to apply.");
        await db.InTransactionAsync(async () =>
        {
            foreach (var rec in raises)
            {
                await employees.AddSalaryAsync(rec.EmployeeId, new AddSalaryRequest(review.EffectiveDate, rec.ProposedSalary, $"Salary review: {cycle.Name}"), ct);
                rec.Applied(user.UserName, clock.GetUtcNow());
            }
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return new ApplyRaisesResult(raises.Count, await ToDtoAsync(cycle, review, ct));
    }

    private void CheckPay()
    {
        if (!access.CanSeePay) throw new NotFoundException("There is no such review cycle.");
    }

    private async Task<ReviewCycle> CycleAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        CheckPay();
        return await db.ReviewCycles.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("There is no such review cycle.");
    }

    /// <summary>Each employee's basic salary in force just before <paramref name="effective"/>.</summary>
    private async Task<Dictionary<Guid, decimal>> CurrentSalariesAsync(IReadOnlyList<Guid> ids, DateOnly effective, CancellationToken ct) =>
        (await db.SalaryChanges.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.EffectiveDate < effective).ToListAsync(ct))
        .GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.EffectiveDate).First().MonthlyBasic);

    private async Task<SalaryReviewDto> ToDtoAsync(ReviewCycle cycle, SalaryReview? review, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var recs = review == null ? [] : await db.SalaryRecommendations.AsNoTracking().Where(x => x.ReviewId == review.Id).ToListAsync(ct);
        var completed = await db.Appraisals.AsNoTracking().Where(a => a.CycleId == cycle.Id && a.Status == AppraisalStatus.Completed).Select(a => a.EmployeeId).ToListAsync(ct);
        var ids = completed.Concat(recs.Select(r => r.EmployeeId)).Distinct().ToList();
        var people = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, Name = e.FirstName + " " + e.LastName, e.EmployeeNumber, e.DepartmentId, e.Status }).ToDictionaryAsync(e => e.Id, ct);
        var depts = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var left = new List<string>();
        var noSalary = completed.Where(id => recs.All(r => r.EmployeeId != id)).Select(id => people[id].Name).ToList();
        if (review != null && noSalary.Count > 0) left.Add($"No salary on record before {review.EffectiveDate:d MMM yyyy}: {string.Join(", ", noSalary)}.");
        var unfinished = await db.Appraisals.CountAsync(a => a.CycleId == cycle.Id && a.Status != AppraisalStatus.Completed, ct);
        if (unfinished > 0) left.Add($"{unfinished} review{(unfinished == 1 ? " isn't" : "s aren't")} complete, so {(unfinished == 1 ? "has" : "have")} no recommendation yet.");
        var rows = recs.Select(r =>
        {
            var e = people[r.EmployeeId];
            return new SalaryRecommendationDto(r.Id, r.EmployeeId, e.Name, e.EmployeeNumber, e.DepartmentId is { } d ? depts.GetValueOrDefault(d) : null, r.Rating,
                r.CurrentSalary, r.Percent, r.ProposedSalary, r.ProposedSalary - r.CurrentSalary, r.Adjusted, r.Note, r.Status, r.AppliedAt, r.AppliedBy);
        }).OrderByDescending(r => r.Rating).ThenBy(r => r.EmployeeName).ToList();
        var counted = rows.Where(r => r.Status != RecommendationStatus.Skipped).ToList();
        return new SalaryReviewDto(cycle.Id, cycle.Name, review != null, review?.Percents ?? [0, 0, 3, 5, 8], review?.EffectiveDate ?? cycle.PeriodEnd.AddDays(1),
            cycle.Scale, rows, left, counted.Sum(r => r.CurrentSalary), counted.Sum(r => r.ProposedSalary), country.CurrencyCode,
            rows.Any(r => r.Status == RecommendationStatus.Proposed && r.ProposedSalary > r.CurrentSalary));
    }
}
