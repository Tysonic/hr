// Mirrors api/src/TyHr.Application/Common/Permissions.cs. The API enforces these; the UI only hides what a user
// can't do. Kept free of browser APIs so it can be unit-tested.

export type Role = 'platform-admin' | 'hr-admin' | 'hr-officer' | 'payroll-officer' | 'line-manager' | 'employee'

const companyRoles: Role[] = ['hr-admin', 'hr-officer', 'payroll-officer', 'line-manager', 'employee']

export const policies = {
  platformTenants: ['platform-admin'],
  companyView: companyRoles,
  companySettings: ['hr-admin'],
  auditView: ['hr-admin', 'platform-admin'],
  orgView: companyRoles,
  orgManage: ['hr-admin', 'hr-officer'],
  directoryView: companyRoles,
  employeesView: ['hr-admin', 'hr-officer', 'payroll-officer', 'line-manager'],
  employeesViewAll: ['hr-admin', 'hr-officer', 'payroll-officer'],
  employeesManage: ['hr-admin', 'hr-officer'],
  employeesPersonal: ['hr-admin', 'hr-officer', 'payroll-officer'],
  employeesPay: ['hr-admin', 'payroll-officer'],
  documentsView: ['hr-admin', 'hr-officer', 'payroll-officer'],
  documentsManage: ['hr-admin', 'hr-officer'],
  expiriesView: ['hr-admin', 'hr-officer'],
  leaveView: companyRoles,
  leaveApprove: ['hr-admin', 'hr-officer', 'line-manager'],
  leaveSetup: ['hr-admin', 'hr-officer'],
  leaveManage: ['hr-admin', 'hr-officer'],
  leaveViewAll: ['hr-admin', 'hr-officer', 'payroll-officer'],
  attendanceClock: companyRoles,
  attendanceView: ['hr-admin', 'hr-officer', 'payroll-officer', 'line-manager'],
  attendanceViewAll: ['hr-admin', 'hr-officer', 'payroll-officer'],
  attendanceManage: ['hr-admin', 'hr-officer'],
  payrollView: ['hr-admin', 'payroll-officer'],
  payrollSetup: ['hr-admin', 'payroll-officer'],
  payrollPrepare: ['hr-admin', 'payroll-officer'],
  payrollApprove: ['hr-admin'],
  payslipsOwn: companyRoles,
  selfService: companyRoles,
  profileChangesDecide: ['hr-admin', 'hr-officer', 'payroll-officer'],
  announcementsManage: ['hr-admin', 'hr-officer'],
  recruitmentRequest: ['hr-admin', 'hr-officer', 'line-manager'],
  recruitmentApprove: ['hr-admin'],
  recruitmentManage: ['hr-admin', 'hr-officer'],
  performanceManage: ['hr-admin', 'hr-officer'],
  /** Who may have something to decide: leave, profile changes or payroll. */
  inbox: ['hr-admin', 'hr-officer', 'payroll-officer', 'line-manager'],
  plans: ['hr-admin', 'hr-officer', 'line-manager'],
  exitsManage: ['hr-admin', 'hr-officer'],
  recordsRetention: ['hr-admin'],
  reportsView: ['hr-admin', 'hr-officer', 'payroll-officer'],
} satisfies Record<string, Role[]>

export type Policy = keyof typeof policies

export const roleLabels: Record<Role, string> = {
  'platform-admin': 'Platform admin',
  'hr-admin': 'HR admin',
  'hr-officer': 'HR officer',
  'payroll-officer': 'Payroll officer',
  'line-manager': 'Line manager',
  employee: 'Employee',
}

export function can(roles: readonly Role[], policy: Policy): boolean {
  const allowed: readonly Role[] = policies[policy]
  return roles.some((r) => allowed.includes(r))
}
