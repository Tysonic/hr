namespace TyHr.Application.Common;

/// <summary>
/// Keycloak realm roles in the "tyhr" realm. Platform admins run the SaaS (tenants); every other role belongs to a
/// user of one company. The API maps roles to permissions in TyHr.Api/Infrastructure/Permissions.cs.
/// </summary>
public static class Roles
{
    public const string PlatformAdmin = "platform-admin";
    public const string HrAdmin = "hr-admin";
    public const string HrOfficer = "hr-officer";
    public const string PayrollOfficer = "payroll-officer";
    public const string LineManager = "line-manager";
    public const string Employee = "employee";

    public static readonly string[] All = [PlatformAdmin, HrAdmin, HrOfficer, PayrollOfficer, LineManager, Employee];
    public static readonly string[] Company = [HrAdmin, HrOfficer, PayrollOfficer, LineManager, Employee];
}
