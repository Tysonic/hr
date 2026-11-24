using TyHr.Domain.Payroll;
using TyHr.Domain.Attendance;
using TyHr.Domain.Documents;
using TyHr.Domain.Leave;
using TyHr.Domain.Reminders;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TyHr.Application.Common;
using TyHr.Domain;
using TyHr.Domain.Auditing;
using TyHr.Domain.Employees;
using TyHr.Domain.Organization;
using TyHr.Domain.Tenants;
using TyHr.Infrastructure.Auditing;
using TyHr.Infrastructure.Security;

namespace TyHr.Infrastructure.Persistence;

/// <summary>
/// One shared database for every company. Each <see cref="ITenantOwned"/> entity gets the query filter
/// <c>e.Tenant == CurrentTenant</c> (ported from MyBiz's TenantDbContext), so a request only ever reads its own
/// company's rows; with no tenant (a platform admin) it reads none. Saving stamps the request's tenant on new rows,
/// refuses to touch another company's rows, and writes the audit trail in the same transaction.
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    TimeProvider clock,
    SecretProtector secrets) : DbContext(options), IAppDbContext
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobGrade> JobGrades => Set<JobGrade>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<NextOfKin> NextOfKin => Set<NextOfKin>();
    public DbSet<EmploymentContract> EmploymentContracts => Set<EmploymentContract>();
    public DbSet<SalaryChange> SalaryChanges => Set<SalaryChange>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<PublicHoliday> PublicHolidays => Set<PublicHoliday>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveAdjustment> LeaveAdjustments => Set<LeaveAdjustment>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<ScheduleAssignment> ScheduleAssignments => Set<ScheduleAssignment>();
    public DbSet<AttendanceEnrollment> AttendanceEnrollments => Set<AttendanceEnrollment>();
    public DbSet<ClockEvent> ClockEvents => Set<ClockEvent>();
    public DbSet<PayComponent> PayComponents => Set<PayComponent>();
    public DbSet<EmployeePayItem> EmployeePayItems => Set<EmployeePayItem>();
    public DbSet<EmployeeTaxProfile> EmployeeTaxProfiles => Set<EmployeeTaxProfile>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayrollInput> PayrollInputs => Set<PayrollInput>();
    public DbSet<Domain.SelfService.ProfileChangeRequest> ProfileChangeRequests => Set<Domain.SelfService.ProfileChangeRequest>();
    public DbSet<Domain.SelfService.Announcement> Announcements => Set<Domain.SelfService.Announcement>();
    public DbSet<Domain.Recruitment.JobRequisition> JobRequisitions => Set<Domain.Recruitment.JobRequisition>();
    public DbSet<Domain.Recruitment.JobPosting> JobPostings => Set<Domain.Recruitment.JobPosting>();
    public DbSet<Domain.Recruitment.JobApplication> JobApplications => Set<Domain.Recruitment.JobApplication>();
    public DbSet<Domain.Recruitment.ApplicationEvent> ApplicationEvents => Set<Domain.Recruitment.ApplicationEvent>();
    public DbSet<Domain.Recruitment.Interview> Interviews => Set<Domain.Recruitment.Interview>();
    public DbSet<Domain.Recruitment.Scorecard> Scorecards => Set<Domain.Recruitment.Scorecard>();
    public DbSet<Domain.Recruitment.JobOffer> JobOffers => Set<Domain.Recruitment.JobOffer>();
    public DbSet<Domain.Recruitment.OfferLetterTemplate> OfferLetterTemplates => Set<Domain.Recruitment.OfferLetterTemplate>();
    public DbSet<Domain.Onboarding.OnboardingTemplate> OnboardingTemplates => Set<Domain.Onboarding.OnboardingTemplate>();
    public DbSet<Domain.Onboarding.OnboardingTask> OnboardingTasks => Set<Domain.Onboarding.OnboardingTask>();
    public DbSet<Domain.Onboarding.ProbationReview> ProbationReviews => Set<Domain.Onboarding.ProbationReview>();
    public DbSet<Domain.Performance.ReviewCycle> ReviewCycles => Set<Domain.Performance.ReviewCycle>();
    public DbSet<Domain.Performance.Appraisal> Appraisals => Set<Domain.Performance.Appraisal>();
    public DbSet<Domain.Performance.Goal> Goals => Set<Domain.Performance.Goal>();
    public DbSet<Domain.Performance.PeerFeedback> PeerFeedback => Set<Domain.Performance.PeerFeedback>();
    public DbSet<Domain.Performance.ImprovementPlan> ImprovementPlans => Set<Domain.Performance.ImprovementPlan>();
    public DbSet<Domain.Performance.PlanCheckIn> PlanCheckIns => Set<Domain.Performance.PlanCheckIn>();
    public DbSet<Domain.Performance.SalaryReview> SalaryReviews => Set<Domain.Performance.SalaryReview>();
    public DbSet<Domain.Performance.SalaryRecommendation> SalaryRecommendations => Set<Domain.Performance.SalaryRecommendation>();
    public DbSet<Domain.Exits.EmployeeExit> EmployeeExits => Set<Domain.Exits.EmployeeExit>();
    public DbSet<Domain.Exits.ClearanceItem> ClearanceItems => Set<Domain.Exits.ClearanceItem>();
    public DbSet<Domain.Exits.ExitInterview> ExitInterviews => Set<Domain.Exits.ExitInterview>();
    public DbSet<Domain.Exits.FinalSettlement> FinalSettlements => Set<Domain.Exits.FinalSettlement>();

    /// <summary>The company this context reads and writes for. EF re-reads it for every query.</summary>
    public string? CurrentTenant => tenantContext.Tenant;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        var context = Expression.Constant(this);
        foreach (var entity in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.BaseType == null && typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            var row = Expression.Parameter(entity.ClrType, "e");
            var sameTenant = Expression.Equal(
                Expression.Property(row, nameof(ITenantOwned.Tenant)),
                Expression.Property(context, nameof(CurrentTenant)));
            modelBuilder.Entity(entity.ClrType).HasQueryFilter(Expression.Lambda(sameTenant, row));
            modelBuilder.Entity(entity.ClrType).Property(nameof(ITenantOwned.Tenant)).HasMaxLength(63).IsRequired();
            modelBuilder.Entity(entity.ClrType).HasIndex(nameof(ITenantOwned.Tenant));
        }

        // [Encrypted] strings are stored as ciphertext ("v1:..."), which is longer than the value.
        var encrypted = new ValueConverter<string, string>(v => secrets.Protect(v), v => secrets.Unprotect(v));
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                     .Where(p => p.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>() != null).ToList())
        {
            property.SetValueConverter(encrypted);
            property.SetMaxLength(1000);
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(500);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("Use SaveChangesAsync.");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAndGuardTenants();
        AuditTrail.Record(this, CurrentTenant, currentUser.UserName, clock.GetUtcNow());
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (Database.CurrentTransaction != null) return await work();
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var result = await work();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private void StampAndGuardTenants()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            var tenant = entry.Property(nameof(ITenantOwned.Tenant));
            switch (entry.State)
            {
                case EntityState.Added:
                    if (CurrentTenant == null)
                        throw new InvalidOperationException($"Cannot save a new {entry.Metadata.ClrType.Name} without a company (no tenant on this request).");
                    if (tenant.CurrentValue is string existing && existing != CurrentTenant)
                        throw new InvalidOperationException($"Cannot add a {entry.Metadata.ClrType.Name} for another company.");
                    tenant.CurrentValue = CurrentTenant;
                    break;
                case EntityState.Modified or EntityState.Deleted:
                    if (tenant.IsModified || (string?)tenant.OriginalValue != CurrentTenant)
                        throw new InvalidOperationException($"Cannot change a {entry.Metadata.ClrType.Name} that belongs to another company.");
                    break;
            }
        }
    }
}

/// <summary>Counter behind <see cref="Application.Common.IDocumentNumbers"/>: the last number issued per company and prefix.</summary>
[NoAudit]
public class DocumentSequence
{
    public string Tenant { get; set; } = null!;
    public string Prefix { get; set; } = null!;
    public int Last { get; set; }
}
