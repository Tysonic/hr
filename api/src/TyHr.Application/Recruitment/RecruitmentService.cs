using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TyHr.Application.Common;
using TyHr.Application.Documents;
using TyHr.Domain;
using TyHr.Domain.Recruitment;

namespace TyHr.Application.Recruitment;

/// <summary>
/// Requisitions (managers and HR raise them, HR admins approve), job postings for approved requisitions, and the
/// candidates who apply, moved through the hiring stages by HR. Managers see only the requisitions they raised.
/// </summary>
public class RecruitmentService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, IDocumentNumbers numbers, IFileStore files,
    TimeProvider clock)
{
    public const string NumberPrefix = "REQ";

    private bool CanManage => user.Can(Permissions.RecruitmentManage);
    private bool CanApprove => user.Can(Permissions.RecruitmentApprove);

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    // Requisitions

    public async Task<IReadOnlyList<RequisitionDto>> RequisitionsAsync(RequisitionStatus? status, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var query = db.JobRequisitions.AsNoTracking();
        if (!CanManage && !CanApprove) query = query.Where(r => r.RequestedBy == user.UserName);
        if (status is { } s) query = query.Where(r => r.Status == s);
        return await ToDtosAsync(await query.OrderByDescending(r => r.RequestedAt).Take(500).ToListAsync(ct), ct);
    }

    public async Task<RequisitionDto> RequisitionAsync(Guid id, CancellationToken ct) => (await ToDtosAsync([await FindRequisitionAsync(id, ct)], ct))[0];

    public async Task<RequisitionDto> CreateRequisitionAsync(RequisitionDetails d, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        await CheckOrgAsync(d, ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var requisition = await db.InTransactionAsync(async () =>
        {
            var r = new JobRequisition(await numbers.NextAsync(NumberPrefix, ct), d, country, user.UserName, clock.GetUtcNow());
            db.JobRequisitions.Add(r);
            await db.SaveChangesAsync(ct);
            return r;
        }, ct);
        return (await ToDtosAsync([requisition], ct))[0];
    }

    public async Task<RequisitionDto> UpdateRequisitionAsync(Guid id, RequisitionDetails d, CancellationToken ct)
    {
        var r = await FindRequisitionAsync(id, ct);
        if (!CanManage && r.RequestedBy != user.UserName) throw new NotFoundException("There is no such requisition.");
        await CheckOrgAsync(d, ct);
        r.Update(d, await db.CompanyCountryAsync(tenantContext, ct));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    public async Task<RequisitionDto> ApproveRequisitionAsync(Guid id, string? note, CancellationToken ct)
    {
        var r = await FindRequisitionAsync(id, ct);
        r.Approve(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    public async Task<RequisitionDto> RejectRequisitionAsync(Guid id, string? note, CancellationToken ct)
    {
        var r = await FindRequisitionAsync(id, ct);
        r.Reject(user.UserName, clock.GetUtcNow(), note);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    /// <summary>Closes the requisition and its postings; candidates still in progress stay where they are.</summary>
    public async Task<RequisitionDto> CloseRequisitionAsync(Guid id, string? note, CancellationToken ct)
    {
        var r = await FindRequisitionAsync(id, ct);
        if (!CanManage && r.RequestedBy != user.UserName) throw new NotFoundException("There is no such requisition.");
        r.Close(user.UserName, clock.GetUtcNow(), note);
        foreach (var p in await db.JobPostings.Where(p => p.RequisitionId == id).ToListAsync(ct)) p.Close();
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([r], ct))[0];
    }

    private async Task<JobRequisition> FindRequisitionAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var r = await db.JobRequisitions.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r == null || (!CanManage && !CanApprove && r.RequestedBy != user.UserName)) throw new NotFoundException("There is no such requisition.");
        return r;
    }

    private async Task CheckOrgAsync(RequisitionDetails d, CancellationToken ct)
    {
        if (d.DepartmentId is { } dept && !await db.Departments.AnyAsync(x => x.Id == dept, ct)) throw new DomainException("There is no such department.");
        if (d.BranchId is { } branch && !await db.Branches.AnyAsync(x => x.Id == branch, ct)) throw new DomainException("There is no such branch.");
        if (d.PositionId is { } pos)
        {
            var position = await db.Positions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pos, ct) ?? throw new DomainException("There is no such position.");
            if (d.DepartmentId != null && position.DepartmentId != d.DepartmentId) throw new DomainException("That position is in a different department.");
        }
    }

    private async Task<IReadOnlyList<RequisitionDto>> ToDtosAsync(IReadOnlyList<JobRequisition> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var positions = await db.Positions.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Title, ct);
        var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct);
        var ids = items.Select(r => r.Id).ToList();
        var postings = await db.JobPostings.AsNoTracking().Where(p => ids.Contains(p.RequisitionId)).Select(p => new { p.Id, p.RequisitionId }).ToListAsync(ct);
        var postingIds = postings.Select(p => p.Id).ToList();
        var hired = (await db.JobApplications.AsNoTracking().Where(a => postingIds.Contains(a.PostingId) && a.Stage == ApplicationStage.Hired)
                .Select(a => a.PostingId).ToListAsync(ct))
            .Select(pid => postings.Single(p => p.Id == pid).RequisitionId).ToList();
        return items.Select(r => new RequisitionDto(r.Id, r.Number, r.Title, r.DepartmentId, Name(departments, r.DepartmentId), r.PositionId,
            Name(positions, r.PositionId), r.BranchId, Name(branches, r.BranchId), r.Openings, r.EmploymentType, r.SalaryMin, r.SalaryMax, r.TargetStartDate,
            r.Justification, r.Status, r.RequestedBy, r.RequestedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote,
            postings.Count(p => p.RequisitionId == r.Id), hired.Count(h => h == r.Id),
            CanEdit: r.Status == RequisitionStatus.Pending && (CanManage || r.RequestedBy == user.UserName),
            CanDecide: r.Status == RequisitionStatus.Pending && CanApprove && r.RequestedBy != user.UserName,
            CanClose: r.Status is RequisitionStatus.Pending or RequisitionStatus.Approved && (CanManage || r.RequestedBy == user.UserName))).ToList();

        static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } x ? names.GetValueOrDefault(x) : null;
    }

    // Postings

    public async Task<IReadOnlyList<PostingDto>> PostingsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var postings = await db.JobPostings.AsNoTracking().OrderByDescending(p => p.Status == PostingStatus.Open).ThenByDescending(p => p.PublishedAt)
            .Take(500).ToListAsync(ct);
        return await ToDtosAsync(postings, ct);
    }

    public async Task<PostingDto> PostingAsync(Guid id, CancellationToken ct) => (await ToDtosAsync([await FindPostingAsync(id, ct)], ct))[0];

    public async Task<PostingDto> CreatePostingAsync(CreatePostingRequest r, CancellationToken ct)
    {
        var requisition = await FindRequisitionAsync(r.RequisitionId, ct);
        if (requisition.Status != RequisitionStatus.Approved) throw new DomainException("Only an approved requisition can be advertised.");
        var posting = new JobPosting(requisition.Id, r.Details, await TodayAsync(ct));
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([posting], ct))[0];
    }

    public async Task<PostingDto> UpdatePostingAsync(Guid id, PostingDetails d, CancellationToken ct)
    {
        var p = await FindPostingAsync(id, ct);
        p.Update(d, await TodayAsync(ct));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([p], ct))[0];
    }

    public async Task<PostingDto> PublishPostingAsync(Guid id, CancellationToken ct)
    {
        var p = await FindPostingAsync(id, ct);
        var requisition = await db.JobRequisitions.AsNoTracking().SingleAsync(r => r.Id == p.RequisitionId, ct);
        if (requisition.Status != RequisitionStatus.Approved) throw new DomainException("The requisition is no longer open, so the job can't be advertised.");
        p.Publish(clock.GetUtcNow(), await TodayAsync(ct));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([p], ct))[0];
    }

    public async Task<PostingDto> ClosePostingAsync(Guid id, CancellationToken ct)
    {
        var p = await FindPostingAsync(id, ct);
        p.Close();
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([p], ct))[0];
    }

    private async Task<JobPosting> FindPostingAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.JobPostings.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("There is no such job posting.");
    }

    private async Task<IReadOnlyList<PostingDto>> ToDtosAsync(IReadOnlyList<JobPosting> items, CancellationToken ct)
    {
        if (items.Count == 0) return [];
        var today = await TodayAsync(ct);
        var reqIds = items.Select(p => p.RequisitionId).Distinct().ToList();
        var numbersById = await db.JobRequisitions.AsNoTracking().Where(r => reqIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Number, ct);
        var ids = items.Select(p => p.Id).ToList();
        var stages = (await db.JobApplications.AsNoTracking().Where(a => ids.Contains(a.PostingId))
                .GroupBy(a => new { a.PostingId, a.Stage }).Select(g => new { g.Key.PostingId, g.Key.Stage, Count = g.Count() }).ToListAsync(ct))
            .ToLookup(x => x.PostingId);
        return items.Select(p => new PostingDto(p.Id, p.RequisitionId, numbersById[p.RequisitionId], p.Title, p.Description, p.Location, p.ClosingDate,
            p.ShowSalary, p.Status, p.PublishedAt, p.IsAcceptingOn(today), stages[p.Id].ToDictionary(x => x.Stage, x => x.Count),
            stages[p.Id].Sum(x => x.Count))).ToList();
    }

    // Applications

    public async Task<IReadOnlyList<ApplicationSummaryDto>> ApplicationsAsync(Guid postingId, CancellationToken ct)
    {
        await FindPostingAsync(postingId, ct);
        return await db.JobApplications.AsNoTracking().Where(a => a.PostingId == postingId).OrderBy(a => a.AppliedAt)
            .Select(a => new ApplicationSummaryDto(a.Id, a.PostingId, a.FirstName + " " + a.LastName, a.Email, a.Phone, a.Stage, a.Rating, a.Source,
                a.AppliedAt, a.CvStorageKey != null, a.EmployeeId))
            .ToListAsync(ct);
    }

    public async Task<ApplicationDto> ApplicationAsync(Guid id, CancellationToken ct)
    {
        var a = await FindApplicationAsync(id, ct);
        var title = await db.JobPostings.Where(p => p.Id == a.PostingId).Select(p => p.Title).SingleAsync(ct);
        var events = await db.ApplicationEvents.AsNoTracking().Where(e => e.ApplicationId == id).OrderBy(e => e.At)
            .Select(e => new ApplicationEventDto(e.Text, e.Stage, e.By, e.At)).ToListAsync(ct);
        return new ApplicationDto(a.Id, a.PostingId, title, a.FirstName, a.LastName, a.Email, a.Phone, a.CoverLetter, a.Stage, a.Rating, a.Source,
            a.AppliedAt, a.ConsentedAt, a.CvFileName, a.CvSizeBytes, a.EmployeeId, events);
    }

    /// <summary>HR adds a candidate who applied another way (email, walk-in, referral), optionally with their CV.</summary>
    public async Task<ApplicationDto> AddApplicationAsync(Guid postingId, CandidateDetails c, string? fileName, Stream? cv, CancellationToken ct)
    {
        var posting = await FindPostingAsync(postingId, ct);
        if (posting.Status == PostingStatus.Closed) throw new DomainException("This posting is closed.");
        var a = await SaveApplicationAsync(posting, c, ApplicationSource.AddedByHr, fileName, cv, db, tenantContext, files, clock, ct);
        db.ApplicationEvents.Add(new ApplicationEvent(a.Id, "Added by HR", null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await ApplicationAsync(a.Id, ct);
    }

    public async Task<ApplicationDto> MoveAsync(Guid id, MoveApplicationRequest r, CancellationToken ct)
    {
        var a = await FindApplicationAsync(id, ct);
        a.MoveTo(r.Stage);
        var text = $"Moved to {StageName(r.Stage)}" + (Guard.Optional(r.Note) is { } note ? $": {note}" : "");
        db.ApplicationEvents.Add(new ApplicationEvent(a.Id, text, r.Stage, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await ApplicationAsync(id, ct);
    }

    public async Task<ApplicationDto> RateAsync(Guid id, int? rating, CancellationToken ct)
    {
        var a = await FindApplicationAsync(id, ct);
        a.Rate(rating);
        db.ApplicationEvents.Add(new ApplicationEvent(a.Id, rating == null ? "Rating cleared" : $"Rated {rating} of 5", null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await ApplicationAsync(id, ct);
    }

    public async Task<ApplicationDto> AddNoteAsync(Guid id, string text, CancellationToken ct)
    {
        var a = await FindApplicationAsync(id, ct);
        db.ApplicationEvents.Add(new ApplicationEvent(a.Id, text, null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await ApplicationAsync(id, ct);
    }

    public async Task<CvDownload> CvAsync(Guid id, CancellationToken ct)
    {
        var a = await FindApplicationAsync(id, ct);
        if (a.CvStorageKey == null) throw new NotFoundException("This candidate has no CV on file.");
        return new CvDownload(await files.OpenReadAsync(tenantContext.RequireTenant(), a.CvStorageKey, ct), a.CvFileName!, a.CvContentType!);
    }

    private async Task<JobApplication> FindApplicationAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.JobApplications.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("There is no such application.");
    }

    public static string StageName(ApplicationStage s) => s switch
    {
        ApplicationStage.Applied => "applied",
        ApplicationStage.Screening => "screening",
        ApplicationStage.Interview => "interview",
        ApplicationStage.Offer => "offer",
        ApplicationStage.Hired => "hired",
        ApplicationStage.Rejected => "not selected",
        _ => "withdrawn",
    };

    /// <summary>Shared by HR's "add candidate" and the public careers page: checks, stores the CV, then saves the application.</summary>
    internal static async Task<JobApplication> SaveApplicationAsync(JobPosting posting, CandidateDetails c, ApplicationSource source, string? fileName,
        Stream? cv, IAppDbContext db, ITenantContext tenantContext, IFileStore files, TimeProvider clock, CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var email = Guard.Required(c.Email, "Email").ToLowerInvariant();
        if (await db.JobApplications.AnyAsync(a => a.PostingId == posting.Id && a.Email == email, ct))
            throw new DomainException("There's already an application for this job from that email address.");

        MemoryStream? buffered = null;
        string? contentType = null;
        if (cv != null && fileName != null)
        {
            buffered = new MemoryStream();
            await CopyLimitedAsync(cv, buffered, JobApplication.MaxCvBytes, ct);
            contentType = CvTypes.Check(fileName, buffered.GetBuffer().AsSpan(0, (int)Math.Min(16, buffered.Length)));
        }
        var a = new JobApplication(posting.Id, c, country, source, buffered == null ? null : fileName, contentType, buffered?.Length ?? 0, clock.GetUtcNow());
        if (buffered != null)
        {
            buffered.Position = 0;
            await files.SaveAsync(tenant, a.CvStorageKey!, buffered, ct);
        }
        try
        {
            db.JobApplications.Add(a);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            if (a.CvStorageKey != null) await files.DeleteAsync(tenant, a.CvStorageKey, CancellationToken.None);
            throw;
        }
        return a;
    }

    /// <summary>Copies at most <paramref name="max"/> bytes, so an oversized upload is refused without reading it all into memory.</summary>
    private static async Task CopyLimitedAsync(Stream from, MemoryStream to, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            if (to.Length + read > max) throw new DomainException($"CVs can be at most {max / 1024 / 1024} MB.");
            await to.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    internal static string? Salary(JobRequisition r, Domain.Countries.CountryPack country)
    {
        string F(decimal v) => v.ToString(country.CurrencyMinorUnits == 0 ? "N0" : "N2", CultureInfo.InvariantCulture);
        return (r.SalaryMin, r.SalaryMax) switch
        {
            ({ } min, { } max) when min == max => $"{country.CurrencyCode} {F(min)} a month",
            ({ } min, { } max) => $"{country.CurrencyCode} {F(min)}–{F(max)} a month",
            ({ } min, null) => $"From {country.CurrencyCode} {F(min)} a month",
            (null, { } max) => $"Up to {country.CurrencyCode} {F(max)} a month",
            _ => null,
        };
    }
}

/// <summary>CVs: PDF or Word only, checked by extension and leading bytes like other uploads.</summary>
public static class CvTypes
{
    public const string Accepted = "PDF or Word (.docx)";

    public static string Check(string fileName, ReadOnlySpan<byte> start)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".pdf" or ".docx")) throw new DomainException($"Upload your CV as a {Accepted} file.");
        return FileTypes.Check(fileName, start);
    }
}
