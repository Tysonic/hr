import { createBrowserRouter } from 'react-router'
import { AppLayout } from './components/AppLayout'
import type { ComponentType } from 'react'
import { Loading } from './components/common'

// Each page is its own chunk, fetched the first time it's opened; the layout, Mantine and sign-in load up front.
// While a page's chunk loads, the previous page stays on screen (or the spinner, on first load).
const page = <K extends string>(load: () => Promise<Record<K, ComponentType>>, name: K) =>
  async () => ({ Component: (await load())[name] })

export const router = createBrowserRouter([
  {
    element: <AppLayout />,
    HydrateFallback: Loading,
    children: [
      { path: '/', lazy: page(() => import('./features/dashboard/DashboardPage'), 'DashboardPage') },
      { path: '/me', lazy: page(() => import('./features/me/MyProfilePage'), 'MyProfilePage') },
      { path: '/approvals', lazy: page(() => import('./features/me/InboxPage'), 'InboxPage') },
      { path: '/team', lazy: page(() => import('./features/me/MyTeamPage'), 'MyTeamPage') },
      { path: '/settings/announcements', lazy: page(() => import('./features/me/AnnouncementsPage'), 'AnnouncementsPage') },
      { path: '/recruitment', lazy: page(() => import('./features/recruitment/PostingsPage'), 'PostingsPage') },
      { path: '/recruitment/postings/:id', lazy: page(() => import('./features/recruitment/PostingPage'), 'PostingPage') },
      { path: '/reviews', lazy: page(() => import('./features/performance/MyReviewsPage'), 'MyReviewsPage') },
      { path: '/reviews/:id', lazy: page(() => import('./features/performance/AppraisalPage'), 'AppraisalPage') },
      { path: '/performance', lazy: page(() => import('./features/performance/CyclesPage'), 'CyclesPage') },
      { path: '/performance/cycles/:id', lazy: page(() => import('./features/performance/CyclePage'), 'CyclePage') },
      { path: '/performance/cycles/:id/salary-review', lazy: page(() => import('./features/performance/SalaryReviewPage'), 'SalaryReviewPage') },
      { path: '/feedback/:id', lazy: page(() => import('./features/performance/FeedbackPage'), 'FeedbackPage') },
      { path: '/exits', lazy: page(() => import('./features/exits/ExitsPage'), 'ExitsPage') },
      { path: '/exits/:id', lazy: page(() => import('./features/exits/ExitPage'), 'ExitPage') },
      { path: '/reports', lazy: page(() => import('./features/reports/ReportsPage'), 'ReportsPage') },
      { path: '/former-employees', lazy: page(() => import('./features/exits/FormerEmployeesPage'), 'FormerEmployeesPage') },
      { path: '/me/exit', lazy: page(() => import('./features/exits/MyExitPage'), 'MyExitPage') },
      { path: '/plans', lazy: page(() => import('./features/performance/PlansPage'), 'PlansPage') },
      { path: '/plans/:id', lazy: page(() => import('./features/performance/PlanPage'), 'PlanPage') },
      { path: '/tasks', lazy: page(() => import('./features/onboarding/MyTasksPage'), 'MyTasksPage') },
      { path: '/onboarding', lazy: page(() => import('./features/onboarding/OnboardingPage'), 'OnboardingPage') },
      { path: '/probation', lazy: page(() => import('./features/onboarding/ProbationPage'), 'ProbationPage') },
      { path: '/settings/onboarding', lazy: page(() => import('./features/onboarding/ChecklistsPage'), 'ChecklistsPage') },
      { path: '/interviews', lazy: page(() => import('./features/recruitment/MyInterviewsPage'), 'MyInterviewsPage') },
      { path: '/settings/offer-letter', lazy: page(() => import('./features/recruitment/OfferTemplatePage'), 'OfferTemplatePage') },
      { path: '/recruitment/requisitions', lazy: page(() => import('./features/recruitment/RequisitionsPage'), 'RequisitionsPage') },
      { path: '/employees', lazy: page(() => import('./features/employees/EmployeesPage'), 'EmployeesPage') },
      { path: '/employees/:id', lazy: page(() => import('./features/employees/EmployeePage'), 'EmployeePage') },
      { path: '/directory', lazy: page(() => import('./features/employees/DirectoryPage'), 'DirectoryPage') },
      { path: '/expiries', lazy: page(() => import('./features/employees/ExpiriesPage'), 'ExpiriesPage') },
      { path: '/org-chart', lazy: page(() => import('./features/org/OrgChartPage'), 'OrgChartPage') },
      { path: '/settings/organisation', lazy: page(() => import('./features/org/OrganisationPage'), 'OrganisationPage') },
      { path: '/leave', lazy: page(() => import('./features/leave/MyLeavePage'), 'MyLeavePage') },
      { path: '/leave/approvals', lazy: page(() => import('./features/leave/ApprovalsPage'), 'ApprovalsPage') },
      { path: '/leave/calendar', lazy: page(() => import('./features/leave/LeaveCalendarPage'), 'LeaveCalendarPage') },
      { path: '/attendance', lazy: page(() => import('./features/attendance/MyAttendancePage'), 'MyAttendancePage') },
      { path: '/attendance/team', lazy: page(() => import('./features/attendance/TeamAttendancePage'), 'TeamAttendancePage') },
      { path: '/attendance/import', lazy: page(() => import('./features/attendance/ImportPunchesPage'), 'ImportPunchesPage') },
      { path: '/settings/attendance', lazy: page(() => import('./features/attendance/AttendanceSettingsPage'), 'AttendanceSettingsPage') },
      { path: '/payslips', lazy: page(() => import('./features/payroll/MyPayslipsPage'), 'MyPayslipsPage') },
      { path: '/payroll', lazy: page(() => import('./features/payroll/PayrollRunsPage'), 'PayrollRunsPage') },
      { path: '/payroll/runs/:id', lazy: page(() => import('./features/payroll/PayrollRunPage'), 'PayrollRunPage') },
      { path: '/settings/payroll', lazy: page(() => import('./features/payroll/PayrollSettingsPage'), 'PayrollSettingsPage') },
      { path: '/settings/leave', lazy: page(() => import('./features/leave/LeaveSettingsPage'), 'LeaveSettingsPage') },
      { path: '/platform/tenants', lazy: page(() => import('./features/platform/TenantsPage'), 'TenantsPage') },
      { path: '/settings/company', lazy: page(() => import('./features/company/CompanyPage'), 'CompanyPage') },
      { path: '/settings/audit', lazy: page(() => import('./features/audit/AuditPage'), 'AuditPage') },
      { path: '*', lazy: page(() => import('./features/NotFoundPage'), 'NotFoundPage') },
    ],
  },
])
