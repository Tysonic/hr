using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Recruitment;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Recruitment;

/// <summary>
/// The public careers page: a company's open jobs and applying for one, without signing in. The company comes from
/// the URL; it must be an active TyHR customer, and from then on the request is scoped to it like a signed-in one.
/// Only what a candidate should see is returned.
/// </summary>
public class CareersService(IAppDbContext db, ITenantContext tenantContext, IFileStore files, TimeProvider clock)
{
    public async Task<CareersDto> JobsAsync(string slug, CancellationToken ct)
    {
        var name = await EnterAsync(slug, ct);
        var today = await TodayAsync(ct);
        var postings = await db.JobPostings.AsNoTracking()
            .Where(p => p.Status == PostingStatus.Open && (p.ClosingDate == null || p.ClosingDate >= today))
            .OrderByDescending(p => p.PublishedAt).ToListAsync(ct);
        return new CareersDto(name, await ToDtosAsync(postings, ct));
    }

    public async Task<PublicPostingDto> JobAsync(string slug, Guid id, CancellationToken ct)
    {
        await EnterAsync(slug, ct);
        return (await ToDtosAsync([await OpenAsync(id, ct)], ct))[0];
    }

    public async Task ApplyAsync(string slug, Guid id, CandidateDetails c, bool consent, string? fileName, Stream? cv, CancellationToken ct)
    {
        await EnterAsync(slug, ct);
        if (!consent) throw new DomainException("Please agree to us using your details for this application.");
        if (cv == null) throw new DomainException("Please attach your CV.");
        var posting = await OpenAsync(id, ct);
        await RecruitmentService.SaveApplicationAsync(posting, c, ApplicationSource.CareersPage, fileName, cv, db, tenantContext, files, clock, ct);
    }

    /// <summary>Scopes the request to the company in the URL; unknown or suspended companies look the same: not found.</summary>
    private async Task<string> EnterAsync(string slug, CancellationToken ct)
    {
        var normalized = (slug ?? "").Trim().ToLowerInvariant();
        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Slug == normalized && t.Status == TenantStatus.Active)
            .Select(t => new { t.Slug, t.Name }).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("There is no such careers page.");
        tenantContext.Tenant = tenant.Slug;
        var profile = await db.CompanyProfiles.AsNoTracking().Select(p => p.TradingName ?? p.LegalName).SingleOrDefaultAsync(ct);
        return profile ?? tenant.Name;
    }

    private async Task<JobPosting> OpenAsync(Guid id, CancellationToken ct)
    {
        var today = await TodayAsync(ct);
        var posting = await db.JobPostings.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (posting == null || posting.Status == PostingStatus.Draft) throw new NotFoundException("There is no such job.");
        if (!posting.IsAcceptingOn(today)) throw new DomainException("This job is no longer taking applications.");
        return posting;
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    private async Task<IReadOnlyList<PublicPostingDto>> ToDtosAsync(IReadOnlyList<JobPosting> postings, CancellationToken ct)
    {
        if (postings.Count == 0) return [];
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var reqIds = postings.Select(p => p.RequisitionId).ToList();
        var requisitions = await db.JobRequisitions.AsNoTracking().Where(r => reqIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        return postings.Select(p =>
        {
            var r = requisitions[p.RequisitionId];
            return new PublicPostingDto(p.Id, p.Title, p.Description, p.Location, r.DepartmentId is { } d ? departments.GetValueOrDefault(d) : null,
                r.EmploymentType, p.ClosingDate, p.ShowSalary ? RecruitmentService.Salary(r, country) : null, p.PublishedAt);
        }).ToList();
    }
}
