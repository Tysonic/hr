using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TyHr.Application.Common;
using TyHr.Application.Employees;
using TyHr.Domain;
using TyHr.Domain.Countries;
using TyHr.Domain.Documents;
using TyHr.Domain.Employees;
using TyHr.Domain.Recruitment;

namespace TyHr.Application.Recruitment;

/// <summary>
/// From shortlist to employee: interviews with scorecards from a panel of employees, the offer and its letter, and
/// turning a candidate who accepted into an employee record (contract, salary and CV carried over).
/// </summary>
public class HiringService(IAppDbContext db, ITenantContext tenantContext, ICurrentUser user, EmployeeAccess access, EmployeeService employees,
    IFileStore files, IEmailSender email, IOfferLetterRenderer renderer, Onboarding.OnboardingService onboarding, TimeProvider clock, ILogger<HiringService> logger)
{
    private bool IsHr => user.Can(Permissions.RecruitmentManage);

    // Interviews

    public async Task<IReadOnlyList<InterviewDto>> InterviewsAsync(Guid applicationId, CancellationToken ct)
    {
        await ApplicationAsync(applicationId, ct);
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.ApplicationId == applicationId).OrderBy(i => i.StartsAt).ToListAsync(ct);
        return await ToDtosAsync(interviews, ct);
    }

    /// <summary>The signed-in employee's interviews as a panellist, upcoming first, then the last 60 days.</summary>
    public async Task<IReadOnlyList<InterviewDto>> MyInterviewsAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        if (await access.MeAsync(ct) is not { } me) return [];
        var since = clock.GetUtcNow().AddDays(-60);
        var interviews = await db.Interviews.AsNoTracking().Where(i => i.PanelIds.Contains(me) && i.StartsAt >= since && i.Status != InterviewStatus.Cancelled)
            .OrderBy(i => i.StartsAt).ToListAsync(ct);
        return await ToDtosAsync(interviews, ct);
    }

    public async Task<InterviewDto> InterviewAsync(Guid id, CancellationToken ct) => (await ToDtosAsync([await VisibleInterviewAsync(id, ct)], ct))[0];

    public async Task<InterviewDto> ScheduleAsync(Guid applicationId, ScheduleInterviewRequest r, CancellationToken ct)
    {
        var application = await ApplicationAsync(applicationId, ct);
        if (!application.IsActive) throw new DomainException($"{application.FullName}'s application is closed.");
        await CheckPanelAsync(r.PanelIds, ct);
        var interview = new Interview(applicationId, await DetailsAsync(r, ct), user.UserName, clock.GetUtcNow());
        db.Interviews.Add(interview);
        if (application.Stage is ApplicationStage.Applied or ApplicationStage.Screening)
        {
            application.MoveTo(ApplicationStage.Interview);
            db.ApplicationEvents.Add(new ApplicationEvent(application.Id, "Moved to interview", ApplicationStage.Interview, user.UserName, clock.GetUtcNow()));
        }
        db.ApplicationEvents.Add(new ApplicationEvent(application.Id, $"Interview set for {await LocalAsync(interview.StartsAt, ct):d MMM yyyy HH:mm}",
            null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await InviteAsync(interview, application, r.EmailCandidate, "Interview", ct);
        return await InterviewAsync(interview.Id, ct);
    }

    public async Task<InterviewDto> RescheduleAsync(Guid id, ScheduleInterviewRequest r, CancellationToken ct)
    {
        var interview = await HrInterviewAsync(id, ct);
        await CheckPanelAsync(r.PanelIds, ct);
        interview.Update(await DetailsAsync(r, ct), clock.GetUtcNow());
        var application = await ApplicationAsync(interview.ApplicationId, ct);
        db.ApplicationEvents.Add(new ApplicationEvent(application.Id, $"Interview moved to {await LocalAsync(interview.StartsAt, ct):d MMM yyyy HH:mm}",
            null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        await InviteAsync(interview, application, r.EmailCandidate, "Interview moved", ct);
        return await InterviewAsync(id, ct);
    }

    public async Task<InterviewDto> CancelInterviewAsync(Guid id, CancellationToken ct)
    {
        var interview = await HrInterviewAsync(id, ct);
        interview.Cancel();
        db.ApplicationEvents.Add(new ApplicationEvent(interview.ApplicationId, "Interview cancelled", null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return await InterviewAsync(id, ct);
    }

    public async Task<InterviewDto> CompleteInterviewAsync(Guid id, CancellationToken ct)
    {
        var interview = await HrInterviewAsync(id, ct);
        interview.Complete();
        await db.SaveChangesAsync(ct);
        return await InterviewAsync(id, ct);
    }

    /// <summary>A panellist sends (or corrects) their scorecard.</summary>
    public async Task<InterviewDto> SubmitScorecardAsync(Guid interviewId, SubmitScorecardRequest r, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var me = await access.MeAsync(ct) ?? throw new DomainException(EmployeeService.NotLinked);
        var interview = await db.Interviews.SingleOrDefaultAsync(i => i.Id == interviewId, ct);
        if (interview == null || !interview.PanelIds.Contains(me)) throw new NotFoundException("There is no such interview.");
        if (interview.StartsAt > clock.GetUtcNow()) throw new DomainException("Send your scorecard once the interview has started.");
        var existing = await db.Scorecards.SingleOrDefaultAsync(s => s.InterviewId == interviewId && s.InterviewerId == me, ct);
        if (existing != null) existing.Update(interview, r.Scores, r.Recommendation, r.Comments, clock.GetUtcNow());
        else
        {
            var name = await db.Employees.Where(e => e.Id == me).Select(e => e.FirstName + " " + e.LastName).SingleAsync(ct);
            db.Scorecards.Add(new Scorecard(interviewId, me, name, interview, r.Scores, r.Recommendation, r.Comments, clock.GetUtcNow()));
        }
        await db.SaveChangesAsync(ct);
        if (interview.Status == InterviewStatus.Scheduled
            && await db.Scorecards.CountAsync(s => s.InterviewId == interviewId, ct) >= interview.PanelIds.Count)
        {
            interview.Complete();
            await db.SaveChangesAsync(ct);
        }
        return await InterviewAsync(interviewId, ct);
    }

    /// <summary>The candidate's CV, for HR and the interview's panel.</summary>
    public async Task<CvDownload> InterviewCvAsync(Guid interviewId, CancellationToken ct)
    {
        var interview = await VisibleInterviewAsync(interviewId, ct);
        var a = await db.JobApplications.AsNoTracking().SingleAsync(x => x.Id == interview.ApplicationId, ct);
        if (a.CvStorageKey == null) throw new NotFoundException("This candidate has no CV on file.");
        return new CvDownload(await files.OpenReadAsync(tenantContext.RequireTenant(), a.CvStorageKey, ct), a.CvFileName!, a.CvContentType!);
    }

    private async Task<Interview> VisibleInterviewAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var interview = await db.Interviews.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        var me = await access.MeAsync(ct);
        if (interview == null || (!IsHr && (me == null || !interview.PanelIds.Contains(me.Value)))) throw new NotFoundException("There is no such interview.");
        return interview;
    }

    private async Task<Interview> HrInterviewAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.Interviews.SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("There is no such interview.");
    }

    private async Task<InterviewDetails> DetailsAsync(ScheduleInterviewRequest r, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var start = new DateTimeOffset(DateTime.SpecifyKind(r.LocalStart, DateTimeKind.Unspecified), country.UtcOffset).ToUniversalTime();
        return new InterviewDetails(start, r.DurationMinutes, r.Location, r.PanelIds, r.Criteria);
    }

    private async Task CheckPanelAsync(IReadOnlyList<Guid> panel, CancellationToken ct)
    {
        var ids = panel.Distinct().ToList();
        var found = await db.Employees.Where(e => ids.Contains(e.Id) && e.Status != EmployeeStatus.Exited).ToListAsync(ct);
        if (found.Count != ids.Count) throw new DomainException("Interviewers must be current employees.");
        var noEmail = found.Where(e => e.WorkEmail == null).Select(e => e.FullName).ToList();
        if (noEmail.Count > 0)
            throw new DomainException($"{string.Join(", ", noEmail)} can't sign in to send a scorecard: set a work email on their record first.");
    }

    private async Task<IReadOnlyList<InterviewDto>> ToDtosAsync(IReadOnlyList<Interview> interviews, CancellationToken ct)
    {
        if (interviews.Count == 0) return [];
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var me = await access.MeAsync(ct);
        var appIds = interviews.Select(i => i.ApplicationId).Distinct().ToList();
        var apps = await db.JobApplications.AsNoTracking().Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var postingIds = apps.Values.Select(a => a.PostingId).Distinct().ToList();
        var titles = await db.JobPostings.AsNoTracking().Where(p => postingIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var panelIds = interviews.SelectMany(i => i.PanelIds).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => panelIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FirstName + " " + e.LastName, ct);
        var ids = interviews.Select(i => i.Id).ToList();
        var cards = (await db.Scorecards.AsNoTracking().Where(s => ids.Contains(s.InterviewId)).ToListAsync(ct)).ToLookup(s => s.InterviewId);
        return interviews.Select(i =>
        {
            var a = apps[i.ApplicationId];
            var all = cards[i.Id].Select(ToDto).ToList();
            var mine = all.SingleOrDefault(s => s.InterviewerId == me);
            var onPanel = me != null && i.PanelIds.Contains(me.Value);
            return new InterviewDto(i.Id, a.Id, a.FullName, a.PostingId, titles[a.PostingId], i.StartsAt, country.ToLocal(i.StartsAt).DateTime, i.DurationMinutes,
                i.Location, i.Status, i.PanelIds.Select(p => new PanelistDto(p, names.GetValueOrDefault(p, "?"), all.Any(s => s.InterviewerId == p))).ToList(),
                i.Criteria, mine, IsHr || mine != null ? all : [], onPanel, a.CvFileName, a.CoverLetter);
        }).ToList();
    }

    private static ScorecardDto ToDto(Scorecard s) =>
        new(s.Id, s.InterviewerId, s.InterviewerName, s.Scores, s.Recommendation, s.Comments, s.Average, s.SubmittedAt);

    /// <summary>Emails the panel (and the candidate if asked) with a calendar invite. Best effort.</summary>
    private async Task InviteAsync(Interview interview, JobApplication application, bool emailCandidate, string what, CancellationToken ct)
    {
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var companyName = company.TradingName ?? company.LegalName;
        var title = await db.JobPostings.Where(p => p.Id == application.PostingId).Select(p => p.Title).SingleAsync(ct);
        var when = $"{country.ToLocal(interview.StartsAt):dddd d MMMM yyyy, HH:mm} ({interview.DurationMinutes} minutes)";
        var where = interview.Location ?? "to be confirmed";
        var panel = await db.Employees.AsNoTracking().Where(e => interview.PanelIds.Contains(e.Id)).Select(e => e.WorkEmail!).ToListAsync(ct);
        var ics = Calendar(interview, $"Interview: {application.FullName} for {title}", where);
        await SendAsync(panel, $"{what}: {application.FullName} for {title}",
            $"You're on the panel interviewing {application.FullName} for {title}.\nWhen: {when}\nWhere: {where}\n\n"
            + "Their CV and the scorecard are in TyHR under My interviews. Please send your scorecard after the interview.", ics, ct);
        if (emailCandidate)
            await SendAsync([application.Email], $"{what}: {title} at {companyName}",
                $"Dear {application.FirstName},\n\nThank you for applying for {title}. We'd like to meet you.\nWhen: {when}\nWhere: {where}\n\n"
                + $"Please reply to let us know you can make it.\n\n{companyName}", Calendar(interview, $"Interview at {companyName}", where), ct);
    }

    private static EmailAttachment Calendar(Interview i, string summary, string location)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        var f = "yyyyMMdd'T'HHmmss'Z'";
        var ics = string.Join("\r\n",
            "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//TyHR//Interviews//EN", "METHOD:REQUEST", "BEGIN:VEVENT",
            $"UID:{i.Id}@tyhr", $"DTSTAMP:{DateTime.UtcNow.ToString(f, CultureInfo.InvariantCulture)}",
            $"DTSTART:{i.StartsAt.UtcDateTime.ToString(f, CultureInfo.InvariantCulture)}",
            $"DTEND:{i.StartsAt.AddMinutes(i.DurationMinutes).UtcDateTime.ToString(f, CultureInfo.InvariantCulture)}",
            $"SUMMARY:{Esc(summary)}", $"LOCATION:{Esc(location)}", "END:VEVENT", "END:VCALENDAR", "");
        return new EmailAttachment("interview.ics", "text/calendar", Encoding.UTF8.GetBytes(ics));
    }

    // Offers

    public async Task<IReadOnlyList<OfferDto>> OffersAsync(Guid applicationId, CancellationToken ct)
    {
        await ApplicationAsync(applicationId, ct);
        var offers = await db.JobOffers.AsNoTracking().Where(o => o.ApplicationId == applicationId).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(offers, ct);
    }

    public async Task<OfferDto> CreateOfferAsync(Guid applicationId, OfferTerms t, CancellationToken ct)
    {
        var application = await ApplicationAsync(applicationId, ct);
        if (!application.IsActive) throw new DomainException($"{application.FullName}'s application is closed.");
        if (await db.JobOffers.AnyAsync(o => o.ApplicationId == applicationId && (o.Status == OfferStatus.Draft || o.Status == OfferStatus.Sent || o.Status == OfferStatus.Accepted), ct))
            throw new DomainException("This candidate already has an offer. Withdraw it to make a different one.");
        var offer = new JobOffer(applicationId, t, await db.CompanyCountryAsync(tenantContext, ct), user.UserName, clock.GetUtcNow(), await TodayAsync(ct));
        db.JobOffers.Add(offer);
        if (application.Stage != ApplicationStage.Offer)
        {
            application.MoveTo(ApplicationStage.Offer);
            db.ApplicationEvents.Add(new ApplicationEvent(application.Id, "Moved to offer", ApplicationStage.Offer, user.UserName, clock.GetUtcNow()));
        }
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([offer], ct))[0];
    }

    public async Task<OfferDto> UpdateOfferAsync(Guid id, OfferTerms t, CancellationToken ct)
    {
        var offer = await OfferAsync(id, ct);
        offer.Update(t, await db.CompanyCountryAsync(tenantContext, ct), await TodayAsync(ct));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([offer], ct))[0];
    }

    public async Task<OutputLetter> OfferLetterAsync(Guid id, CancellationToken ct)
    {
        var offer = await OfferAsync(id, ct);
        var application = await ApplicationAsync(offer.ApplicationId, ct);
        return new OutputLetter(await RenderAsync(offer, application, ct), LetterName(application));
    }

    /// <summary>Marks the offer sent; with <paramref name="emailCandidate"/>, emails them the letter first.</summary>
    public async Task<OfferDto> SendOfferAsync(Guid id, bool emailCandidate, CancellationToken ct)
    {
        var offer = await OfferAsync(id, ct);
        var application = await ApplicationAsync(offer.ApplicationId, ct);
        offer.Sent(clock.GetUtcNow());
        if (emailCandidate)
        {
            var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
            var companyName = company.TradingName ?? company.LegalName;
            var pdf = await RenderAsync(offer, application, ct);
            var text = $"Dear {application.FirstName},\n\nPlease find attached our offer for the position of {offer.JobTitle}. "
                       + $"We'd be grateful for your reply by {offer.RespondBy:d MMMM yyyy}.\n\n{companyName}";
            // Sending is part of the action here, so a refused or unconfigured mail server is an error, not a warning.
            if (!await email.SendAsync(new EmailMessage([application.Email], $"Job offer: {offer.JobTitle} at {companyName}", Html(text), text,
                    [new EmailAttachment(LetterName(application), "application/pdf", pdf)]), ct))
                throw new DomainException("No mail server is set up, so the offer wasn't emailed. Download the letter and send it yourself.");
        }
        db.ApplicationEvents.Add(new ApplicationEvent(application.Id, emailCandidate ? "Offer emailed to the candidate" : "Offer marked as sent",
            null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([offer], ct))[0];
    }

    public async Task<OfferDto> RespondAsync(Guid id, OfferResponseRequest r, CancellationToken ct)
    {
        var offer = await OfferAsync(id, ct);
        offer.Respond(r.Accepted, r.Note, clock.GetUtcNow());
        db.ApplicationEvents.Add(new ApplicationEvent(offer.ApplicationId, (r.Accepted ? "Offer accepted" : "Offer declined")
            + (Guard.Optional(r.Note) is { } n ? $": {n}" : ""), null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([offer], ct))[0];
    }

    public async Task<OfferDto> WithdrawOfferAsync(Guid id, string? note, CancellationToken ct)
    {
        var offer = await OfferAsync(id, ct);
        offer.Withdraw(note, clock.GetUtcNow());
        db.ApplicationEvents.Add(new ApplicationEvent(offer.ApplicationId, "Offer withdrawn" + (Guard.Optional(note) is { } n ? $": {n}" : ""),
            null, user.UserName, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([offer], ct))[0];
    }

    private async Task<JobOffer> OfferAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.JobOffers.SingleOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("There is no such offer.");
    }

    private async Task<IReadOnlyList<OfferDto>> ToDtosAsync(IReadOnlyList<JobOffer> offers, CancellationToken ct)
    {
        if (offers.Count == 0) return [];
        var appIds = offers.Select(o => o.ApplicationId).Distinct().ToList();
        var apps = await db.JobApplications.AsNoTracking().Where(a => appIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var postingIds = apps.Values.Select(a => a.PostingId).ToList();
        var requisitions = await db.JobPostings.AsNoTracking().Where(p => postingIds.Contains(p.Id))
            .Join(db.JobRequisitions.AsNoTracking(), p => p.RequisitionId, r => r.Id, (p, r) => new { PostingId = p.Id, r.SalaryMin, r.SalaryMax })
            .ToDictionaryAsync(x => x.PostingId, ct);
        return offers.Select(o =>
        {
            var a = apps[o.ApplicationId];
            var req = requisitions[a.PostingId];
            var warnings = new List<string>();
            if (o.MonthlySalary > req.SalaryMax || o.MonthlySalary < req.SalaryMin)
                warnings.Add("The salary is outside the range approved on the requisition.");
            return new OfferDto(o.Id, a.Id, a.FullName, a.Email, o.JobTitle, o.StartDate, o.MonthlySalary, o.ContractType, o.ContractEndDate,
                o.ProbationMonths, o.RespondBy, o.ExtraTerms, o.Status, o.CreatedBy, o.CreatedAt, o.SentAt, o.RespondedAt, o.ResponseNote, warnings);
        }).ToList();
    }

    // Offer letter template

    public async Task<OfferTemplateDto> TemplateAsync(CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var t = await db.OfferLetterTemplates.AsNoTracking().SingleOrDefaultAsync(ct);
        return new OfferTemplateDto(t?.Body ?? OfferLetterTemplate.Default, t == null, OfferLetterTemplate.Placeholders);
    }

    public async Task<OfferTemplateDto> SaveTemplateAsync(string body, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        var t = await db.OfferLetterTemplates.SingleOrDefaultAsync(ct);
        if (t == null) db.OfferLetterTemplates.Add(new OfferLetterTemplate(body));
        else t.Update(body);
        await db.SaveChangesAsync(ct);
        return await TemplateAsync(ct);
    }

    /// <summary>The template with the offer's terms filled in.</summary>
    public static string Fill(string template, JobOffer o, JobApplication a, string companyName, CountryPack country)
    {
        string Money(decimal v) => $"{country.CurrencyCode} {v.ToString(country.CurrencyMinorUnits == 0 ? "N0" : "N2", CultureInfo.InvariantCulture)}";
        var values = new Dictionary<string, string>
        {
            ["CandidateName"] = a.FullName,
            ["FirstName"] = a.FirstName,
            ["JobTitle"] = o.JobTitle,
            ["StartDate"] = o.StartDate.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
            ["Salary"] = Money(o.MonthlySalary),
            ["ContractType"] = o.ContractType switch
            {
                ContractType.Permanent => "permanent",
                ContractType.FixedTerm => "on a fixed-term contract",
                ContractType.Casual => "casual",
                _ => "an internship",
            },
            ["ContractEndDate"] = o.ContractEndDate is { } end ? $" ending on {end.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}" : "",
            ["ProbationMonths"] = o.ProbationMonths.ToString(CultureInfo.InvariantCulture),
            ["RespondBy"] = o.RespondBy.ToString("d MMMM yyyy", CultureInfo.InvariantCulture),
            ["CompanyName"] = companyName,
        };
        var body = Regex.Replace(template, @"\{\{\s*(\w+)\s*\}\}", m => values.GetValueOrDefault(m.Groups[1].Value, m.Value));
        return o.ExtraTerms is { } extra ? $"{body.TrimEnd()}\n\n{extra}" : body;
    }

    private async Task<byte[]> RenderAsync(JobOffer offer, JobApplication application, CancellationToken ct)
    {
        var company = await db.CompanyProfiles.AsNoTracking().SingleAsync(ct);
        var country = await db.CompanyCountryAsync(tenantContext, ct);
        var name = company.TradingName ?? company.LegalName;
        var template = (await TemplateAsync(ct)).Body;
        return renderer.Render(new OfferLetterDocument(name, company.PhysicalAddress ?? company.PostalAddress, company.Email, company.Phone,
            DateOnly.FromDateTime(country.ToLocal(offer.SentAt ?? clock.GetUtcNow()).DateTime), application.FullName, application.Email, application.Phone,
            offer.JobTitle, Fill(template, offer, application, name, country), $"For {name}"));
    }

    private static string LetterName(JobApplication a) => $"offer-{a.LastName}-{a.FirstName}.pdf".ToLowerInvariant().Replace(' ', '-');

    // Hiring

    /// <summary>
    /// Makes the candidate an employee from their accepted offer: personal details from the application, position from
    /// the requisition, a contract and starting salary from the offer (the salary only if the user may set pay), and
    /// their CV filed under the employee's documents. When the requisition's openings are all filled, it closes.
    /// </summary>
    public async Task<HireResult> HireAsync(Guid applicationId, HireRequest r, CancellationToken ct)
    {
        var tenant = tenantContext.RequireTenant();
        var application = await ApplicationAsync(applicationId, ct);
        var offer = await db.JobOffers.AsNoTracking().SingleOrDefaultAsync(o => o.ApplicationId == applicationId && o.Status == OfferStatus.Accepted, ct)
                    ?? throw new DomainException("Record the candidate's acceptance of an offer before making them an employee.");
        var posting = await db.JobPostings.SingleAsync(p => p.Id == application.PostingId, ct);
        var requisition = await db.JobRequisitions.SingleAsync(q => q.Id == posting.RequisitionId, ct);
        var salaryRecorded = access.CanSeePay;

        var (employee, filled) = await db.InTransactionAsync(async () =>
        {
            var personal = new PersonalDetails(application.FirstName, null, application.LastName, r.Gender, r.DateOfBirth, null, r.Nationality,
                null, null, null, null, application.Email, application.Phone, null);
            var employment = new EmploymentDetails(offer.StartDate, r.WorkEmail, null, requisition.BranchId, requisition.DepartmentId, requisition.PositionId, r.ManagerId);
            var contract = new SaveContractRequest(offer.ContractType, offer.StartDate, offer.ContractEndDate,
                offer.ProbationMonths > 0 ? offer.StartDate.AddMonths(offer.ProbationMonths).AddDays(-1) : null, $"From offer for {offer.JobTitle}");
            var salary = salaryRecorded ? new AddSalaryRequest(offer.StartDate, offer.MonthlySalary, "Starting salary (job offer)") : null;
            var created = await employees.CreateAsync(new CreateEmployeeRequest(personal, employment, contract, salary), ct);

            if (application.CvStorageKey != null)
            {
                await using var cv = await files.OpenReadAsync(tenant, application.CvStorageKey, ct);
                var doc = new EmployeeDocument(created.Id, "CV", DocumentCategory.Other, application.CvFileName!, application.CvContentType!,
                    application.CvSizeBytes, null, null, $"From their application for {posting.Title}", user.UserName, clock.GetUtcNow());
                await files.SaveAsync(tenant, doc.StorageKey, cv, ct);
                db.EmployeeDocuments.Add(doc);
            }
            application.Hired(created.Id);
            db.ApplicationEvents.Add(new ApplicationEvent(application.Id, $"Hired as {created.EmployeeNumber}", ApplicationStage.Hired, user.UserName, clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);

            var postingIds = await db.JobPostings.Where(p => p.RequisitionId == requisition.Id).Select(p => p.Id).ToListAsync(ct);
            var hired = await db.JobApplications.CountAsync(a => postingIds.Contains(a.PostingId) && a.Stage == ApplicationStage.Hired, ct);
            var isFilled = hired >= requisition.Openings && requisition.Status == RequisitionStatus.Approved;
            if (isFilled)
            {
                requisition.Close(user.UserName, clock.GetUtcNow(), "All openings filled");
                foreach (var p in await db.JobPostings.Where(p => p.RequisitionId == requisition.Id).ToListAsync(ct)) p.Close();
                await db.SaveChangesAsync(ct);
            }
            return (created, isFilled);
        }, ct);
        // Onboarding starts after the hire is saved: if it can't (say a bad IT contact), the employee still exists and HR can start it from their record.
        var tasks = 0;
        var warnings = new List<string>();
        if (r.Onboarding != null)
            try
            {
                var started = await onboarding.StartAsync(employee.Id, r.Onboarding, ct);
                tasks = started.Tasks;
                warnings.AddRange(started.Warnings);
            }
            catch (DomainException e)
            {
                warnings.Add($"Onboarding didn't start: {e.Message} Start it from their record.");
            }
        return new HireResult(employee.Id, employee.EmployeeNumber, salaryRecorded, filled, tasks, warnings);
    }

    // Helpers

    private async Task<JobApplication> ApplicationAsync(Guid id, CancellationToken ct)
    {
        tenantContext.RequireTenant();
        return await db.JobApplications.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException("There is no such application.");
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct) =>
        DateOnly.FromDateTime((await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(clock.GetUtcNow()).DateTime);

    private async Task<DateTime> LocalAsync(DateTimeOffset utc, CancellationToken ct) => (await db.CompanyCountryAsync(tenantContext, ct)).ToLocal(utc).DateTime;

    private static string Html(string text) => string.Join("", text.Split('\n').Select(line => $"<p>{WebUtility.HtmlEncode(line)}</p>"));

    private async Task SendAsync(IReadOnlyList<string> to, string subject, string text, EmailAttachment? attachment, CancellationToken ct)
    {
        if (to.Count == 0) return;
        try
        {
            await email.SendAsync(new EmailMessage(to, subject, Html(text), text, attachment == null ? null : [attachment]), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Could not send the email \"{Subject}\".", subject);
        }
    }
}

public record OutputLetter(byte[] Content, string FileName);
