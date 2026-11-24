using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Application.Onboarding;
using TyHr.Application.Recruitment;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Employees;
using TyHr.Domain.Recruitment;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Pdf;
using TyHr.Infrastructure.Persistence;

namespace TyHr.Tests;

/// <summary>
/// Interviews, scorecards, offers and hiring. Grace (HR admin) runs hiring; Mark and Olive (employees with sign-ins)
/// sit on the panel; the candidate, Jane, applied with a CV for an accountant's job. Now is 23 Nov 2026 (fixture clock).
/// </summary>
public class HiringTests(PostgresFixture fx) : IClassFixture<PostgresFixture>, IDisposable
{
    private readonly TempFolder _files = new();
    private readonly FakeEmail _email = new();

    public void Dispose() => _files.Dispose();

    private static readonly byte[] PdfCv = Encoding.ASCII.GetBytes("%PDF-1.4 Jane's CV");

    private sealed record Services(Scope Scope, RecruitmentService Recruitment, HiringService Hiring, EmployeeService Employees) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Scope.DisposeAsync();
    }

    private Services As(string tenant, string user, params string[] roles)
    {
        var scope = fx.NewScope(tenant, user: user, roles: roles, email: $"{user}@co.test");
        var store = new LocalFileStore(Options.Create(new StorageOptions { Path = _files.Path }));
        var access = new EmployeeAccess(scope.Db, scope.User);
        var employees = new EmployeeService(scope.Db, scope.TenantContext, new PostgresDocumentNumbers(scope.Db), access, scope.User, fx.Clock);
        return new Services(scope, new RecruitmentService(scope.Db, scope.TenantContext, scope.User, new PostgresDocumentNumbers(scope.Db), store, fx.Clock),
            new HiringService(scope.Db, scope.TenantContext, scope.User, access, employees, store, _email, new QuestOfferLetterRenderer(),
                new OnboardingService(scope.Db, scope.TenantContext, scope.User, access, _email, fx.Clock, NullLogger<OnboardingService>.Instance), fx.Clock,
                NullLogger<HiringService>.Instance),
            employees);
    }

    private sealed record Setup(string Tenant, Guid Application, Guid Mark, Guid Olive, Guid Department);

    /// <summary>An approved, advertised accountant job (one opening) with Jane's application, and two panellists.</summary>
    private async Task<Setup> CandidateAsync()
    {
        var co = await fx.CreateTenantAsync();
        await using var grace = As(co, "grace", Roles.HrAdmin);
        var mark = (await grace.Employees.CreateAsync(EmployeeTests.NewEmployee("Mark", "Opio", email: "mark@co.test"), default)).Id;
        var olive = (await grace.Employees.CreateAsync(EmployeeTests.NewEmployee("Olive", "Ainembabazi", email: "olive@co.test"), default)).Id;
        var finance = new Domain.Organization.Department("Finance", "FIN", null);
        grace.Scope.Db.Departments.Add(finance);
        await grace.Scope.Db.SaveChangesAsync();
        Guid req;
        await using (var henry = As(co, "henry", Roles.HrOfficer))
            req = (await henry.Recruitment.CreateRequisitionAsync(new RequisitionDetails("Accountant", finance.Id, null, null, 1, ContractType.Permanent,
                1_500_000, 2_000_000, null, "Growth"), default)).Id;
        await grace.Recruitment.ApproveRequisitionAsync(req, null, default);
        var posting = await grace.Recruitment.CreatePostingAsync(new CreatePostingRequest(req, new PostingDetails("Accountant", "Books", null, null, false)), default);
        await grace.Recruitment.PublishPostingAsync(posting.Id, default);
        var app = await grace.Recruitment.AddApplicationAsync(posting.Id, new CandidateDetails("Jane", "Akello", "jane@mail.test", "0772123456", null),
            "Jane CV.pdf", new MemoryStream(PdfCv), default);
        return new Setup(co, app.Id, mark, olive, finance.Id);
    }

    /// <summary>An interview at a UTC time, sent as company local time (UTC+3) like the web app does.</summary>
    private static ScheduleInterviewRequest At(DateTimeOffset when, bool emailCandidate, params Guid[] panel) =>
        new(when.ToOffset(TimeSpan.FromHours(3)).DateTime, 45, "Head office, room 2", panel, ["Accounting skills", "Communication"], emailCandidate);

    private static SubmitScorecardRequest Card(int a, int b, Recommendation r) =>
        new([new("Accounting skills", a, null), new("communication", b, "Clear")], r, "Good candidate");

    [Fact]
    public async Task Panellists_see_their_interviews_and_others_scorecards_only_after_sending_their_own()
    {
        var s = await CandidateAsync();
        InterviewDto interview;
        await using (var grace = As(s.Tenant, "grace", Roles.HrAdmin))
        {
            Assert.Contains("passed", (await Assert.ThrowsAsync<DomainException>(() => grace.Hiring.ScheduleAsync(s.Application,
                At(fx.Clock.GetUtcNow().AddDays(-1), false, s.Mark), default))).Message);
            interview = await grace.Hiring.ScheduleAsync(s.Application, At(fx.Clock.GetUtcNow().AddDays(1), true, s.Mark, s.Olive), default);
            Assert.Equal(ApplicationStage.Interview, (await grace.Recruitment.ApplicationAsync(s.Application, default)).Stage);
        }
        var invite = _email.Sent.Single(m => m.To.Contains("mark@co.test"));
        Assert.Contains("Jane Akello", invite.Subject);
        Assert.Equal("text/calendar", Assert.Single(invite.Attachments!).ContentType);
        Assert.Contains(_email.Sent, m => m.To.SequenceEqual(["jane@mail.test"]));

        // Move the interview into the past, as if it had happened.
        await using (var grace = As(s.Tenant, "grace", Roles.HrAdmin))
            await grace.Scope.Db.Interviews.Where(i => i.Id == interview.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.StartsAt, fx.Clock.GetUtcNow().AddHours(-2)));

        await using (var mark = As(s.Tenant, "mark", Roles.LineManager))
        {
            Assert.Single(await mark.Hiring.MyInterviewsAsync(default));
            Assert.Contains("every criterion", (await Assert.ThrowsAsync<DomainException>(() => mark.Hiring.SubmitScorecardAsync(interview.Id,
                new SubmitScorecardRequest([new("Accounting skills", 4, null)], Recommendation.Yes, null), default))).Message);
            var after = await mark.Hiring.SubmitScorecardAsync(interview.Id, Card(4, 5, Recommendation.Yes), default);
            Assert.Equal((4.5m, 1), (after.MyScorecard!.Average, after.Scorecards.Count));
            Assert.Equal(["Accounting skills", "Communication"], after.MyScorecard.Scores.Select(x => x.Criterion));
            Assert.Equal("%PDF", Encoding.ASCII.GetString(((MemoryStream)await Copy((await mark.Hiring.InterviewCvAsync(interview.Id, default)).Content)).ToArray(), 0, 4));
        }
        await using (var olive = As(s.Tenant, "olive", Roles.Employee))
        {
            var before = await olive.Hiring.InterviewAsync(interview.Id, default);
            Assert.Empty(before.Scorecards); // Mark's marks stay hidden until Olive sends hers
            var after = await olive.Hiring.SubmitScorecardAsync(interview.Id, Card(3, 3, Recommendation.No), default);
            Assert.Equal(2, after.Scorecards.Count);
            Assert.Equal(InterviewStatus.Completed, after.Status); // everyone has sent theirs
        }
        await using (var outsider = As(s.Tenant, "nobody", Roles.Employee))
            await Assert.ThrowsAsync<NotFoundException>(() => outsider.Hiring.InterviewAsync(interview.Id, default));
    }

    private static async Task<Stream> Copy(Stream s)
    {
        var m = new MemoryStream();
        await using (s) await s.CopyToAsync(m);
        return m;
    }

    private static OfferTerms Terms(decimal salary = 1_800_000) =>
        new("Accountant", new DateOnly(2027, 1, 4), salary, ContractType.Permanent, null, 6, new DateOnly(2026, 12, 4), null);

    [Fact]
    public async Task Offers_fill_the_company_template_warn_outside_the_approved_range_and_are_one_at_a_time()
    {
        var s = await CandidateAsync();
        await using var grace = As(s.Tenant, "grace", Roles.HrAdmin);
        Assert.Contains("Unknown placeholder {{Bonus}}", (await Assert.ThrowsAsync<DomainException>(() => grace.Hiring.SaveTemplateAsync("Hi {{Bonus}}", default))).Message);
        await grace.Hiring.SaveTemplateAsync("Dear {{FirstName}}, we offer you {{JobTitle}} at {{Salary}} from {{StartDate}}.", default);

        var offer = await grace.Hiring.CreateOfferAsync(s.Application, Terms(2_500_000), default);
        Assert.Contains("outside the range", Assert.Single(offer.Warnings));
        Assert.Equal(ApplicationStage.Offer, (await grace.Recruitment.ApplicationAsync(s.Application, default)).Stage);
        Assert.Contains("already has an offer", (await Assert.ThrowsAsync<DomainException>(() => grace.Hiring.CreateOfferAsync(s.Application, Terms(), default))).Message);
        offer = await grace.Hiring.UpdateOfferAsync(offer.Id, Terms(), default);
        Assert.Empty(offer.Warnings);

        var o = await grace.Scope.Db.JobOffers.SingleAsync(x => x.Id == offer.Id);
        var a = await grace.Scope.Db.JobApplications.SingleAsync(x => x.Id == s.Application);
        Assert.Equal("Dear Jane, we offer you Accountant at UGX 1,800,000 from 4 January 2027.",
            HiringService.Fill("Dear {{FirstName}}, we offer you {{JobTitle}} at {{Salary}} from {{StartDate}}.", o, a, "Co", CountryPacks.Uganda));

        var sent = await grace.Hiring.SendOfferAsync(offer.Id, emailCandidate: true, default);
        Assert.Equal(OfferStatus.Sent, sent.Status);
        var mail = _email.Sent.Single(m => m.Subject.StartsWith("Job offer"));
        Assert.Equal(["jane@mail.test"], mail.To);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(Assert.Single(mail.Attachments!).Content, 0, 4));
        Assert.Contains("draft", (await Assert.ThrowsAsync<DomainException>(() => grace.Hiring.UpdateOfferAsync(offer.Id, Terms(), default))).Message);
    }

    [Fact]
    public async Task Hiring_an_accepted_candidate_creates_the_employee_with_contract_salary_and_CV_and_fills_the_requisition()
    {
        var s = await CandidateAsync();
        await using (var grace = As(s.Tenant, "grace", Roles.HrAdmin))
        {
            var offer = await grace.Hiring.CreateOfferAsync(s.Application, Terms(), default);
            Assert.Contains("acceptance", (await Assert.ThrowsAsync<DomainException>(() =>
                grace.Hiring.HireAsync(s.Application, new HireRequest(Gender.Female, null, "UG", "jane@co.test", s.Mark), default))).Message);
            await grace.Hiring.SendOfferAsync(offer.Id, false, default);
            await grace.Hiring.RespondAsync(offer.Id, new OfferResponseRequest(true, "Signed copy received"), default);

            var hired = await grace.Hiring.HireAsync(s.Application, new HireRequest(Gender.Female, new DateOnly(1995, 3, 2), "UG", "jane@co.test", s.Mark, new StartOnboardingRequest(null, s.Olive)), default);
            Assert.True(hired.SalaryRecorded);
            Assert.True(hired.RequisitionFilled);
            Assert.Equal((Domain.Onboarding.OnboardingTemplate.Standard.Length, 0), (hired.OnboardingTasks, hired.Warnings.Count));

            var e = await grace.Employees.GetAsync(hired.EmployeeId, default);
            Assert.Equal(("Jane Akello", "jane@co.test", "Finance", "Mark Opio", new DateOnly(2027, 1, 4)),
                (e.FullName, e.Employment.WorkEmail, e.Employment.DepartmentName, e.Employment.ManagerName, e.Employment.HireDate));
            Assert.Equal(("jane@mail.test", "+256772123456"), (e.Personal!.PersonalEmail, e.Personal.PersonalPhone));
            var contract = Assert.Single(e.Contracts);
            Assert.Equal((ContractType.Permanent, new DateOnly(2027, 7, 3)), (contract.Type, contract.ProbationEndDate));
            Assert.Equal(1_800_000m, Assert.Single(e.Salaries!).MonthlyBasic);
            var cv = await grace.Scope.Db.EmployeeDocuments.SingleAsync(d => d.EmployeeId == hired.EmployeeId);
            Assert.Equal(("CV", "Jane CV.pdf"), (cv.Title, cv.FileName));

            var app = await grace.Recruitment.ApplicationAsync(s.Application, default);
            Assert.Equal((ApplicationStage.Hired, hired.EmployeeId), (app.Stage, app.EmployeeId));
            var req = Assert.Single(await grace.Recruitment.RequisitionsAsync(null, default));
            Assert.Equal((RequisitionStatus.Closed, 1), (req.Status, req.Hired));
            Assert.Contains("closed", (await Assert.ThrowsAsync<DomainException>(() =>
                grace.Hiring.HireAsync(s.Application, new HireRequest(Gender.Female, null, null, null, null), default))).Message);
        }
    }

    [Fact]
    public async Task An_HR_officer_can_hire_but_the_salary_is_left_for_payroll()
    {
        var s = await CandidateAsync();
        Guid offer;
        await using (var grace = As(s.Tenant, "grace", Roles.HrAdmin))
        {
            offer = (await grace.Hiring.CreateOfferAsync(s.Application, Terms(), default)).Id;
            await grace.Hiring.SendOfferAsync(offer, false, default);
            await grace.Hiring.RespondAsync(offer, new OfferResponseRequest(true, null), default);
        }
        await using var henry = As(s.Tenant, "henry", Roles.HrOfficer);
        var hired = await henry.Hiring.HireAsync(s.Application, new HireRequest(Gender.Female, null, null, null, null), default);
        Assert.False(hired.SalaryRecorded);
        Assert.False(await henry.Scope.Db.SalaryChanges.AnyAsync(x => x.EmployeeId == hired.EmployeeId));
    }
}
