using System.Text;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Application.Recruitment;
using TyHr.Domain;
using TyHr.Domain.Employees;
using TyHr.Domain.Recruitment;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Requisitions, postings, the public careers page and the hiring pipeline. Mark (line manager) asks to hire,
/// Grace (HR admin) approves, Henry (HR officer) advertises and moves candidates. Today is Monday 23 Nov 2026.
/// </summary>
public class RecruitmentTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TempFolder _files = new();

    public void Dispose() => _files.Dispose();

    private static readonly byte[] PdfCv = Encoding.ASCII.GetBytes("%PDF-1.4 a CV");

    private sealed record Services(Scope Scope, RecruitmentService Recruitment, CareersService Careers) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string? tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var store = new LocalFileStore(Options.Create(new StorageOptions { Path = _files.Path }));
        return new Services(scope, new RecruitmentService(scope.Db, scope.TenantContext, scope.User, new PostgresDocumentNumbers(scope.Db), store, fx.Clock),
            new CareersService(scope.Db, scope.TenantContext, store, fx.Clock));
    }

    private static RequisitionDetails Accountant(int openings = 1) =>
        new("Accountant", null, null, null, openings, ContractType.Permanent, 1_500_000, 2_000_000, new DateOnly(2027, 1, 4), "Year-end workload");

    private static PostingDetails Advert(DateOnly? closing = null) =>
        new("Accountant", "Keep our books.", "Kampala", closing ?? new DateOnly(2026, 12, 15), ShowSalary: true);

    private static CandidateDetails Candidate(string email = "jane@mail.test") => new("Jane", "Akello", email, "0772 123456", "I'd love to join.");

    /// <summary>An approved requisition with an open posting.</summary>
    private async Task<(string Tenant, Guid Requisition, Guid Posting)> OpenJobAsync()
    {
        var co = await fx.CreateTenantAsync();
        Guid req;
        await using (var mark = As(co, "mark", Roles.LineManager)) req = (await mark.Recruitment.CreateRequisitionAsync(Accountant(), default)).Id;
        await using (var grace = As(co, "grace", Roles.HrAdmin)) await grace.Recruitment.ApproveRequisitionAsync(req, null, default);
        await using var henry = As(co, "henry", Roles.HrOfficer);
        var posting = await henry.Recruitment.CreatePostingAsync(new CreatePostingRequest(req, Advert()), default);
        await henry.Recruitment.PublishPostingAsync(posting.Id, default);
        return (co, req, posting.Id);
    }

    [Fact]
    public async Task Requisitions_are_numbered_and_approved_by_an_HR_admin_other_than_who_raised_them()
    {
        var co = await fx.CreateTenantAsync();
        RequisitionDto marks, graces;
        await using (var mark = As(co, "mark", Roles.LineManager))
        {
            marks = await mark.Recruitment.CreateRequisitionAsync(Accountant(), default);
            Assert.Equal(("REQ-0001", RequisitionStatus.Pending, true, false), (marks.Number, marks.Status, marks.CanEdit, marks.CanDecide));
            Assert.Contains("above", (await Assert.ThrowsAsync<DomainException>(() => mark.Recruitment.CreateRequisitionAsync(
                Accountant() with { SalaryMin = 3_000_000 }, default))).Message);
        }
        await using (var grace = As(co, "grace", Roles.HrAdmin))
        {
            graces = await grace.Recruitment.CreateRequisitionAsync(Accountant(2), default);
            Assert.Equal("REQ-0002", graces.Number);
            Assert.Contains("another HR admin", (await Assert.ThrowsAsync<DomainException>(() => grace.Recruitment.ApproveRequisitionAsync(graces.Id, null, default))).Message);
            Assert.Equal(RequisitionStatus.Approved, (await grace.Recruitment.ApproveRequisitionAsync(marks.Id, "Go ahead", default)).Status);
        }
        await using (var mark = As(co, "mark", Roles.LineManager))
        {
            Assert.Equal([marks.Id], (await mark.Recruitment.RequisitionsAsync(null, default)).Select(r => r.Id)); // only his own
            await Assert.ThrowsAsync<NotFoundException>(() => mark.Recruitment.RequisitionAsync(graces.Id, default));
            Assert.Contains("waiting for approval", (await Assert.ThrowsAsync<DomainException>(() => mark.Recruitment.UpdateRequisitionAsync(marks.Id, Accountant(3), default))).Message);
        }
        await using (var henry = As(co, "henry", Roles.HrOfficer))
            Assert.Contains("approved requisition", (await Assert.ThrowsAsync<DomainException>(() =>
                henry.Recruitment.CreatePostingAsync(new CreatePostingRequest(graces.Id, Advert()), default))).Message);
    }

    [Fact]
    public async Task The_careers_page_shows_open_jobs_only_without_internal_details()
    {
        var (co, req, posting) = await OpenJobAsync();
        await using (var henry = As(co, "henry", Roles.HrOfficer))
            await henry.Recruitment.CreatePostingAsync(new CreatePostingRequest(req, Advert() with { Title = "Draft job" }), default);

        await using var visitor = As(null, "anonymous");
        var page = await visitor.Careers.JobsAsync(co.ToUpperInvariant(), default);
        var job = Assert.Single(page.Jobs);
        Assert.Equal((posting, "Accountant", "UGX 1,500,000–2,000,000 a month"), (job.Id, job.Title, job.Salary));
        await Assert.ThrowsAsync<NotFoundException>(() => visitor.Careers.JobsAsync("no-such-company", default));
    }

    [Fact]
    public async Task Candidates_apply_with_a_CV_once_per_job_and_need_to_consent()
    {
        var (co, _, posting) = await OpenJobAsync();
        await using (var visitor = As(null, "anonymous"))
        {
            Assert.Contains("agree", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate(), consent: false, "cv.pdf", new MemoryStream(PdfCv), default))).Message);
            Assert.Contains("PDF or Word", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate(), true, "cv.png", new MemoryStream([0x89, 0x50, 0x4E, 0x47]), default))).Message);
            Assert.Contains("doesn't look like", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate(), true, "cv.pdf", new MemoryStream("<html>"u8.ToArray()), default))).Message);
            Assert.Contains("at most 5 MB", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate(), true, "cv.pdf", new MemoryStream([.. PdfCv, .. new byte[6 * 1024 * 1024]]), default))).Message);
        }
        await using (var visitor = As(null, "anonymous"))
            await visitor.Careers.ApplyAsync(co, posting, Candidate(), true, "Jane CV.pdf", new MemoryStream(PdfCv), default);
        await using (var visitor = As(null, "anonymous"))
            Assert.Contains("already an application", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate("JANE@mail.test"), true, "cv.pdf", new MemoryStream(PdfCv), default))).Message);

        await using var henry = As(co, "henry", Roles.HrOfficer);
        var application = Assert.Single(await henry.Recruitment.ApplicationsAsync(posting, default));
        Assert.Equal(("Jane Akello", "+256772123456", ApplicationStage.Applied, ApplicationSource.CareersPage, true),
            (application.Name, application.Phone, application.Stage, application.Source, application.HasCv));
        var cv = await henry.Recruitment.CvAsync(application.Id, default);
        Assert.Equal(("Jane CV.pdf", "application/pdf"), (cv.FileName, cv.ContentType));
        await using var content = cv.Content;
        Assert.Equal(PdfCv, ((MemoryStream)await Copy(content)).ToArray());
        Assert.NotNull((await henry.Recruitment.ApplicationAsync(application.Id, default)).ConsentedAt);
    }

    private static async Task<Stream> Copy(Stream s)
    {
        var m = new MemoryStream();
        await s.CopyToAsync(m);
        return m;
    }

    [Fact]
    public async Task Closed_or_expired_jobs_take_no_applications_and_closing_the_requisition_closes_its_postings()
    {
        var (co, req, posting) = await OpenJobAsync();
        await using (var henry = As(co, "henry", Roles.HrOfficer))
            await henry.Recruitment.CloseRequisitionAsync(req, "Filled internally", default);
        await using (var visitor = As(null, "anonymous"))
        {
            Assert.Empty((await visitor.Careers.JobsAsync(co, default)).Jobs);
            Assert.Contains("no longer", (await Assert.ThrowsAsync<DomainException>(() =>
                visitor.Careers.ApplyAsync(co, posting, Candidate(), true, "cv.pdf", new MemoryStream(PdfCv), default))).Message);
        }
        await using (var henry = As(co, "henry", Roles.HrOfficer))
        {
            Assert.Equal(PostingStatus.Closed, (await henry.Recruitment.PostingAsync(posting, default)).Status);
            Assert.Contains("closed", (await Assert.ThrowsAsync<DomainException>(() => henry.Recruitment.UpdatePostingAsync(posting, Advert(), default))).Message);
            Assert.Contains("passed", Assert.Throws<DomainException>(() => new JobPosting(Guid.NewGuid(), Advert(new DateOnly(2026, 11, 1)), new DateOnly(2026, 11, 23))).Message);
        }
    }

    [Fact]
    public async Task HR_moves_candidates_through_the_stages_with_a_history_of_notes_and_ratings()
    {
        var (co, _, posting) = await OpenJobAsync();
        await using var henry = As(co, "henry", Roles.HrOfficer);
        var walkIn = await henry.Recruitment.AddApplicationAsync(posting, Candidate("tom@mail.test") with { FirstName = "Tom" }, null, null, default);
        Assert.Equal((ApplicationSource.AddedByHr, (DateTimeOffset?)null), (walkIn.Source, walkIn.ConsentedAt));
        await henry.Recruitment.RateAsync(walkIn.Id, 4, default);
        await henry.Recruitment.MoveAsync(walkIn.Id, new MoveApplicationRequest(ApplicationStage.Interview, "Strong IFRS background"), default);
        await henry.Recruitment.AddNoteAsync(walkIn.Id, "Available from January", default);
        Assert.Contains("making them an employee", (await Assert.ThrowsAsync<DomainException>(() =>
            henry.Recruitment.MoveAsync(walkIn.Id, new MoveApplicationRequest(ApplicationStage.Hired, null), default))).Message);
        Assert.Contains("1 to 5", (await Assert.ThrowsAsync<DomainException>(() => henry.Recruitment.RateAsync(walkIn.Id, 6, default))).Message);

        var a = await henry.Recruitment.ApplicationAsync(walkIn.Id, default);
        Assert.Equal((ApplicationStage.Interview, 4), (a.Stage, a.Rating));
        Assert.Equal(["Added by HR", "Rated 4 of 5", "Moved to interview: Strong IFRS background", "Available from January"], a.Events.Select(e => e.Text));
        var p = await henry.Recruitment.PostingAsync(posting, default);
        Assert.Equal((1, 1), (p.Applications, p.Stages[ApplicationStage.Interview]));
    }
}
