namespace TyHr.Application.Common;

/// <summary>
/// Permissions are fine-grained codes guarding endpoints and data; each is granted to a bundle of TyHR roles
/// (<see cref="Roles"/>). The API turns them into authorization policies, services check them for what a user may see
/// (salaries, ID numbers, other teams), and the web app mirrors the table in web/src/lib/policies.ts. Company data is
/// also limited to the user's own company by the tenant query filter, whatever their permissions.
/// </summary>
public static class Permissions
{
    public const string PlatformTenants = "platform.tenants";
    public const string CompanyView = "company.view";
    public const string CompanySettings = "company.settings";
    public const string AuditView = "audit.view";

    /// <summary>Branches, departments, grades, positions and the org chart.</summary>
    public const string OrgView = "org.view";
    public const string OrgManage = "org.manage";

    /// <summary>The staff directory: names, jobs and work contacts of active employees.</summary>
    public const string DirectoryView = "directory.view";

    /// <summary>Employee records: every employee's, or (line managers) their own team's.</summary>
    public const string EmployeesView = "employees.view";
    public const string EmployeesViewAll = "employees.view-all";
    public const string EmployeesManage = "employees.manage";

    /// <summary>Personal data: ID numbers, date of birth, home contacts, next of kin.</summary>
    public const string EmployeesPersonal = "employees.personal";

    /// <summary>Salaries and payment details (bank and mobile-money accounts).</summary>
    public const string EmployeesPay = "employees.pay";

    /// <summary>Employees' documents (ID copies, contracts, certificates): reading them, and uploading or removing them.</summary>
    public const string DocumentsView = "documents.view";
    public const string DocumentsManage = "documents.manage";

    /// <summary>What is about to expire or end (documents, contracts, probations), and the reminder emails about it.</summary>
    public const string ExpiriesView = "expiries.view";

    /// <summary>Leave types and holidays, one's own leave (request, balance) and the leave calendar.</summary>
    public const string LeaveView = "leave.view";

    /// <summary>Deciding leave requests: HR for anyone, line managers for their team; never one's own.</summary>
    public const string LeaveApprove = "leave.approve";

    /// <summary>Leave types, public holidays and the working week.</summary>
    public const string LeaveSetup = "leave.setup";

    /// <summary>Recording leave for anyone and adjusting balances.</summary>
    public const string LeaveManage = "leave.manage";

    /// <summary>Everyone's leave and balances (payroll needs unpaid leave).</summary>
    public const string LeaveViewAll = "leave.view-all";

    /// <summary>Clocking in and out, and one's own timesheet.</summary>
    public const string AttendanceClock = "attendance.clock";

    /// <summary>Timesheets and attendance reports: everyone's for HR and payroll, the team's for line managers.</summary>
    public const string AttendanceView = "attendance.view";
    public const string AttendanceViewAll = "attendance.view-all";

    /// <summary>Work schedules, who works which, device IDs, importing punches and correcting them.</summary>
    public const string AttendanceManage = "attendance.manage";

    /// <summary>Payroll runs, payslips and pay set-up (components, employees' pay items and tax profiles).</summary>
    public const string PayrollView = "payroll.view";
    public const string PayrollSetup = "payroll.setup";

    /// <summary>Creating runs, entering one-off amounts, calculating, submitting for approval and locking.</summary>
    public const string PayrollPrepare = "payroll.prepare";

    /// <summary>Approving a submitted payroll (never one the same person prepared).</summary>
    public const string PayrollApprove = "payroll.approve";

    /// <summary>One's own payslips from locked payrolls.</summary>
    public const string PayslipsOwn = "payroll.own-payslips";

    /// <summary>One's own record, change requests, announcements, team and approvals inbox (each filtered to what the user may see).</summary>
    public const string SelfService = "self-service";

    /// <summary>Approving employees' changes to their own records (which kinds depends on the role; see ProfileChangeService).</summary>
    public const string ProfileChangesDecide = "profile-changes.decide";

    public const string AnnouncementsManage = "announcements.manage";

    /// <summary>Raising requisitions and seeing one's own (managers); HR and approvers see all.</summary>
    public const string RecruitmentRequest = "recruitment.request";

    public const string RecruitmentApprove = "recruitment.approve";

    /// <summary>Job postings and candidates.</summary>
    public const string RecruitmentManage = "recruitment.manage";

    /// <summary>Run review cycles and see everyone's appraisals.</summary>
    public const string PerformanceManage = "performance.manage";

    /// <summary>Resignations, terminations, clearance and exit interviews.</summary>
    public const string ExitsManage = "exits.manage";

    /// <summary>Company reports: headcount, turnover and leave liability (pay figures need their own permissions).</summary>
    public const string ReportsView = "reports.view";

    /// <summary>The retention period, and removing former employees' personal data once it has passed.</summary>
    public const string RecordsRetention = "records.retention";

    /// <summary>Which roles each permission allows (any one of them).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Policies = new Dictionary<string, string[]>
    {
        [PlatformTenants] = [Roles.PlatformAdmin],
        [CompanyView] = Roles.Company,
        [CompanySettings] = [Roles.HrAdmin],
        [AuditView] = [Roles.HrAdmin, Roles.PlatformAdmin],
        [OrgView] = Roles.Company,
        [OrgManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [DirectoryView] = Roles.Company,
        [EmployeesView] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer, Roles.LineManager],
        [EmployeesViewAll] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [EmployeesManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [EmployeesPersonal] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [EmployeesPay] = [Roles.HrAdmin, Roles.PayrollOfficer],
        [DocumentsView] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [DocumentsManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [ExpiriesView] = [Roles.HrAdmin, Roles.HrOfficer],
        [LeaveView] = Roles.Company,
        [LeaveApprove] = [Roles.HrAdmin, Roles.HrOfficer, Roles.LineManager],
        [LeaveSetup] = [Roles.HrAdmin, Roles.HrOfficer],
        [LeaveManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [LeaveViewAll] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [AttendanceClock] = Roles.Company,
        [AttendanceView] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer, Roles.LineManager],
        [AttendanceViewAll] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [AttendanceManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [PayrollView] = [Roles.HrAdmin, Roles.PayrollOfficer],
        [PayrollSetup] = [Roles.HrAdmin, Roles.PayrollOfficer],
        [PayrollPrepare] = [Roles.HrAdmin, Roles.PayrollOfficer],
        [PayrollApprove] = [Roles.HrAdmin],
        [PayslipsOwn] = Roles.Company,
        [SelfService] = Roles.Company,
        [ProfileChangesDecide] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
        [AnnouncementsManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [RecruitmentRequest] = [Roles.HrAdmin, Roles.HrOfficer, Roles.LineManager],
        [RecruitmentApprove] = [Roles.HrAdmin],
        [RecruitmentManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [PerformanceManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [ExitsManage] = [Roles.HrAdmin, Roles.HrOfficer],
        [RecordsRetention] = [Roles.HrAdmin],
        [ReportsView] = [Roles.HrAdmin, Roles.HrOfficer, Roles.PayrollOfficer],
    };

    /// <summary>Whether the user holds a role the permission allows.</summary>
    public static bool Can(this ICurrentUser user, string permission) => Policies[permission].Any(user.IsInRole);
}
