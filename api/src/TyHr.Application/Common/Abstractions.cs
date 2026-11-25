using TyHr.Domain.Payroll;
using TyHr.Domain.Attendance;
using Microsoft.EntityFrameworkCore;
using TyHr.Domain.Auditing;
using TyHr.Domain.Documents;
using TyHr.Domain.Leave;
using TyHr.Domain.Reminders;
using TyHr.Domain.Employees;
using TyHr.Domain.Organization;
using TyHr.Domain.Tenants;

namespace TyHr.Application.Common;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<CompanyProfile> CompanyProfiles { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Department> Departments { get; }
    DbSet<JobGrade> JobGrades { get; }
    DbSet<Position> Positions { get; }
    DbSet<Employee> Employees { get; }
    DbSet<NextOfKin> NextOfKin { get; }
    DbSet<EmploymentContract> EmploymentContracts { get; }
    DbSet<SalaryChange> SalaryChanges { get; }
    DbSet<EmployeeDocument> EmployeeDocuments { get; }
    DbSet<ReminderLog> ReminderLogs { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<PublicHoliday> PublicHolidays { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<LeaveAdjustment> LeaveAdjustments { get; }
    DbSet<WorkSchedule> WorkSchedules { get; }
    DbSet<ScheduleAssignment> ScheduleAssignments { get; }
    DbSet<AttendanceEnrollment> AttendanceEnrollments { get; }
    DbSet<ClockEvent> ClockEvents { get; }
    DbSet<PayComponent> PayComponents { get; }
    DbSet<EmployeePayItem> EmployeePayItems { get; }
    DbSet<EmployeeTaxProfile> EmployeeTaxProfiles { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<Payslip> Payslips { get; }
    DbSet<PayrollInput> PayrollInputs { get; }
    DbSet<Domain.SelfService.ProfileChangeRequest> ProfileChangeRequests { get; }
    DbSet<Domain.SelfService.Announcement> Announcements { get; }
    DbSet<Domain.Recruitment.JobRequisition> JobRequisitions { get; }
    DbSet<Domain.Recruitment.JobPosting> JobPostings { get; }
    DbSet<Domain.Recruitment.JobApplication> JobApplications { get; }
    DbSet<Domain.Recruitment.ApplicationEvent> ApplicationEvents { get; }
    DbSet<Domain.Recruitment.Interview> Interviews { get; }
    DbSet<Domain.Recruitment.Scorecard> Scorecards { get; }
    DbSet<Domain.Recruitment.JobOffer> JobOffers { get; }
    DbSet<Domain.Recruitment.OfferLetterTemplate> OfferLetterTemplates { get; }
    DbSet<Domain.Onboarding.OnboardingTemplate> OnboardingTemplates { get; }
    DbSet<Domain.Onboarding.OnboardingTask> OnboardingTasks { get; }
    DbSet<Domain.Onboarding.ProbationReview> ProbationReviews { get; }
    DbSet<Domain.Performance.ReviewCycle> ReviewCycles { get; }
    DbSet<Domain.Performance.Appraisal> Appraisals { get; }
    DbSet<Domain.Performance.Goal> Goals { get; }
    DbSet<Domain.Performance.PeerFeedback> PeerFeedback { get; }
    DbSet<Domain.Performance.ImprovementPlan> ImprovementPlans { get; }
    DbSet<Domain.Performance.PlanCheckIn> PlanCheckIns { get; }
    DbSet<Domain.Performance.SalaryReview> SalaryReviews { get; }
    DbSet<Domain.Performance.SalaryRecommendation> SalaryRecommendations { get; }
    DbSet<Domain.Exits.EmployeeExit> EmployeeExits { get; }
    DbSet<Domain.Exits.ClearanceItem> ClearanceItems { get; }
    DbSet<Domain.Exits.ExitInterview> ExitInterviews { get; }
    DbSet<Domain.Exits.FinalSettlement> FinalSettlements { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs <paramref name="work"/> in a database transaction, committing only if it completes.</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}

/// <summary>The signed-in user: their username for audit fields and maker-checker rules, and what they may see.</summary>
public interface ICurrentUser
{
    /// <summary>Keycloak preferred_username; "system" when there is no signed-in user, e.g. a background job.</summary>
    string UserName { get; }

    /// <summary>The sign-in's email (lower case), which links the user to their employee record by work email.</summary>
    string? Email { get; }

    /// <summary>Whether the user holds a TyHR role (<see cref="Roles"/>). Use <see cref="Permissions.Can"/>.</summary>
    bool IsInRole(string role);
}

/// <summary>
/// The company the current request acts for: scoped, set once per request by the tenant middleware from the
/// access token's "tenant" claim, and read by the DbContext's query filters. Null for platform admins, whose
/// requests can only reach platform endpoints.
/// </summary>
public interface ITenantContext
{
    string? Tenant { get; set; }
}

public sealed class TenantContext : ITenantContext
{
    public string? Tenant { get; set; }
}

/// <summary>Per-company document numbers (EMP-0001, PR-0001), gap-free within a committed transaction.</summary>
public interface IDocumentNumbers
{
    Task<string> NextAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>Creates sign-in accounts for a company's users in the identity provider (Keycloak).</summary>
public interface IIdentityProvisioner
{
    /// <summary>
    /// Gives <paramref name="email"/> the TyHR role for <paramref name="tenant"/>: a new user is created and emailed a
    /// link to set a password; an existing user of the same company (e.g. from MyBiz, which shares the realm) just gets
    /// the role. Fails for an existing user of another company. Returns the identity provider's user id.
    /// </summary>
    Task<string> InviteUserAsync(string tenant, string email, string firstName, string lastName, string role, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where uploaded files are kept: local disk now (a Docker volume in production), S3-compatible storage later.
/// Files are filed under their company, so one company's key can never reach another's files.
/// </summary>
public interface IFileStore
{
    Task SaveAsync(string tenant, string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>The file's content, or a NotFoundException when it is missing.</summary>
    Task<Stream> OpenReadAsync(string tenant, string key, CancellationToken cancellationToken = default);

    /// <summary>Removes the file; nothing happens when it is already gone.</summary>
    Task DeleteAsync(string tenant, string key, CancellationToken cancellationToken = default);
}

public record EmailMessage(IReadOnlyList<string> To, string Subject, string HtmlBody, string TextBody, IReadOnlyList<EmailAttachment>? Attachments = null);

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>The mail server can't be reached or won't let TyHR sign in, so no message would get through.</summary>
public class MailUnavailableException(Exception inner) : Exception("The mail server can't be reached or refused the sign-in: " + inner.Message, inner);

/// <summary>Sends email through the configured SMTP server.</summary>
public interface IEmailSender
{
    /// <summary>False (and nothing sent) when no mail server is configured; throws when the server refuses.</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends several messages, over one connection where the sender can, calling <paramref name="done"/> after each with
    /// its index and the error (null when sent); one failure doesn't stop the rest. False when no mail server is configured.
    /// Throws <see cref="MailUnavailableException"/>, leaving the rest untried, when the server itself is the problem.
    /// </summary>
    async Task<bool> SendManyAsync(IReadOnlyList<EmailMessage> messages, Func<int, Exception?, Task> done, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            Exception? error = null;
            try
            {
                if (!await SendAsync(messages[i], cancellationToken)) return false;
            }
            catch (Exception e) when (e is not (OperationCanceledException or MailUnavailableException)) { error = e; }
            await done(i, error);
        }
        return true;
    }
}
