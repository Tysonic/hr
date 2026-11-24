using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TyHr.Application.Auditing;
using TyHr.Application.Common;
using TyHr.Application.Company;
using TyHr.Application.Tenants;
using TyHr.Infrastructure.Identity;
using TyHr.Infrastructure.Persistence;
using TyHr.Infrastructure.Security;
using TyHr.Application.Organization;
using TyHr.Application.Employees;
using TyHr.Application.Documents;
using TyHr.Application.Attendance;
using TyHr.Application.Leave;
using TyHr.Application.Payroll;
using TyHr.Application.Reminders;
using TyHr.Infrastructure.Files;
using TyHr.Infrastructure.Mail;

namespace TyHr.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is not configured. For local development it is in appsettings.Development.json " +
                "(the docker compose database); elsewhere set the ConnectionStrings__Default environment variable.");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IDocumentNumbers, PostgresDocumentNumbers>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new SecretProtector(configuration["Secrets:Key"]));

        services.Configure<KeycloakOptions>(configuration.GetSection("Keycloak"));
        services.AddHttpClient<IIdentityProvisioner, KeycloakProvisioner>();

        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IFileStore, LocalFileStore>();
        services.Configure<MailOptions>(configuration.GetSection("Mail"));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddScoped<TenantService>();
        services.AddScoped<CompanyService>();
        services.AddScoped<AuditService>();
        services.AddScoped<OrgService>();
        services.AddScoped<EmployeeService>();
        services.AddScoped<EmployeeAccess>();
        services.AddScoped<EmployeeSpreadsheets>();
        services.AddScoped<DocumentService>();
        services.AddScoped<ExpiryService>();
        services.AddScoped<LeaveSetupService>();
        services.AddScoped<LeaveService>();
        services.AddScoped<LeaveNotifier>();
        services.AddScoped<AttendanceService>();
        services.AddScoped<PayrollSetupService>();
        services.AddScoped<PayrollService>();
        services.AddScoped<PayrollOutputService>();
        services.AddScoped<Application.SelfService.ProfileChangeService>();
        services.AddScoped<Application.SelfService.AnnouncementService>();
        services.AddScoped<Application.SelfService.TeamService>();
        services.AddScoped<Application.Recruitment.RecruitmentService>();
        services.AddScoped<Application.Recruitment.CareersService>();
        services.AddScoped<Application.Recruitment.HiringService>();
        services.AddScoped<Application.Onboarding.OnboardingService>();
        services.AddScoped<Application.Onboarding.ProbationService>();
        services.AddScoped<Application.Performance.PerformanceService>();
        services.AddScoped<Application.Performance.ImprovementPlanService>();
        services.AddScoped<Application.Performance.SalaryReviewService>();
        services.AddScoped<Application.Exits.ExitService>();
        services.AddScoped<Application.Exits.FinalDuesService>();
        services.AddScoped<Application.Exits.CertificateService>();
        services.AddScoped<Application.Exits.ArchiveService>();
        services.AddScoped<Application.Reports.ReportsService>();
        services.AddSingleton<Application.Exits.ICertificateRenderer, Pdf.QuestCertificateRenderer>();
        services.AddSingleton<Application.Recruitment.IOfferLetterRenderer, Pdf.QuestOfferLetterRenderer>();
        services.AddSingleton<IPayslipRenderer, Pdf.QuestPayslipRenderer>();
        return services;
    }
}
