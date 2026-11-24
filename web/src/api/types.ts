// Mirrors the API's DTOs (api/src/TyHr.Application). Enums are sent as strings.

export interface Country {
  code: string
  name: string
  currencyCode: string
  currencyMinorUnits: number
  phonePrefix: string
  taxNumberLabel: string
  socialSecurityLabel: string
  nationalIdLabel: string
}

export type TenantStatus = 'Active' | 'Suspended'

export interface Tenant {
  id: string
  slug: string
  name: string
  country: Country
  adminEmail: string
  status: TenantStatus
  createdAt: string
  createdBy: string
}

export interface CreateTenantRequest {
  slug: string
  name: string
  countryCode: string
  adminEmail: string
  adminFirstName: string
  adminLastName: string
}

export interface Company {
  slug: string
  name: string
  country: Country
  legalName: string
  tradingName: string | null
  taxNumber: string | null
  socialSecurityNumber: string | null
  email: string
  phone: string | null
  physicalAddress: string | null
  postalAddress: string | null
  financialYearStartMonth: number
}

export type UpdateCompanyRequest = Omit<Company, 'slug' | 'country'>

export interface AuditEntry {
  id: number
  at: string
  userName: string
  entityType: string
  entityId: string
  action: 'created' | 'updated' | 'deleted'
  changes: string | null
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

// Organisation (api/src/TyHr.Application/Organization/OrgDtos.cs)

export interface Branch { id: string; name: string; code: string | null; address: string | null; isActive: boolean; employeeCount: number }
export interface SaveBranchRequest { name: string; code: string | null; address: string | null }

export interface Department {
  id: string; name: string; code: string | null; parentId: string | null; parentName: string | null
  headEmployeeId: string | null; headName: string | null; isActive: boolean; employeeCount: number
}
export interface SaveDepartmentRequest { name: string; code: string | null; parentId: string | null; headEmployeeId: string | null }

export interface JobGrade {
  id: string; code: string; name: string; rank: number; minSalary: number | null; maxSalary: number | null; isActive: boolean; positionCount: number
}
export interface SaveJobGradeRequest { code: string; name: string; rank: number; minSalary: number | null; maxSalary: number | null }

export interface Position {
  id: string; title: string; departmentId: string; departmentName: string; jobGradeId: string | null; jobGradeCode: string | null
  isActive: boolean; employeeCount: number
}
export interface SavePositionRequest { title: string; departmentId: string; jobGradeId: string | null }

export interface OrgChartNode {
  employeeId: string; employeeNumber: string; name: string; positionTitle: string | null; departmentName: string | null; managerId: string | null
}

// Employees (api/src/TyHr.Application/Employees/EmployeeDtos.cs). Dates are "YYYY-MM-DD".

export type Gender = 'Female' | 'Male'
export type MaritalStatus = 'Single' | 'Married' | 'Divorced' | 'Widowed'
export type IdType = 'NationalId' | 'Passport' | 'RefugeeId' | 'Other'
export type EmployeeStatus = 'Active' | 'Suspended' | 'Exited'
export type PaymentMethod = 'Bank' | 'MobileMoney' | 'Cash'
export type ContractType = 'Permanent' | 'FixedTerm' | 'Casual' | 'Internship'

export interface EmployeeListItem {
  id: string; employeeNumber: string; fullName: string; positionTitle: string | null; departmentName: string | null
  branchName: string | null; managerName: string | null; workEmail: string | null; hireDate: string; status: EmployeeStatus
}

export interface PersonalDetails {
  firstName: string; middleName: string | null; lastName: string; gender: Gender; dateOfBirth: string | null
  maritalStatus: MaritalStatus | null; nationality: string | null; idType: IdType | null; idNumber: string | null
  taxNumber: string | null; socialSecurityNumber: string | null; personalEmail: string | null; personalPhone: string | null
  homeAddress: string | null
}

export interface EmploymentDetails {
  hireDate: string; workEmail: string | null; workPhone: string | null; branchId: string | null; departmentId: string | null
  positionId: string | null; managerId: string | null
}

export interface PaymentDetails {
  method: PaymentMethod; bankName: string | null; bankBranch: string | null; bankAccountNumber: string | null
  bankAccountName: string | null; mobileMoneyProvider: string | null; mobileMoneyNumber: string | null
}

export interface Employment extends EmploymentDetails {
  branchName: string | null; departmentName: string | null; positionTitle: string | null; jobGradeCode: string | null; managerName: string | null
}

export type Personal = Omit<PersonalDetails, 'firstName' | 'middleName' | 'lastName'>

export interface Contract {
  id: string; type: ContractType; startDate: string; endDate: string | null; probationEndDate: string | null
  confirmedOn: string | null; notes: string | null; isCurrent: boolean
}
export interface SaveContractRequest { type: ContractType; startDate: string; endDate: string | null; probationEndDate: string | null; notes: string | null }

export interface Salary {
  id: string; effectiveDate: string; monthlyBasic: number; currencyCode: string; reason: string; isCurrent: boolean
  outsideGrade: boolean; recordedBy: string; recordedAt: string
}
export interface AddSalaryRequest { effectiveDate: string; monthlyBasic: number; reason: string }

export interface NextOfKin { id: string; name: string; relationship: string; phone: string; email: string | null; address: string | null }
export type SaveNextOfKinRequest = Omit<NextOfKin, 'id'>

export interface Employee {
  id: string; employeeNumber: string; fullName: string; firstName: string; middleName: string | null; lastName: string
  status: EmployeeStatus; employment: Employment; personal: Personal | null; payment: PaymentDetails | null
  contracts: Contract[]; salaries: Salary[] | null; nextOfKin: NextOfKin[] | null
  access: { canEdit: boolean; canSeePersonal: boolean; canSeePay: boolean; canEditPay: boolean }
}

export interface CreateEmployeeRequest {
  personal: PersonalDetails; employment: EmploymentDetails; contract: SaveContractRequest | null; salary: AddSalaryRequest | null
}

export interface DirectoryEntry {
  id: string; fullName: string; positionTitle: string | null; departmentName: string | null; branchName: string | null
  workEmail: string | null; workPhone: string | null; managerName: string | null
}

// Documents and expiries

export type DocumentCategory = 'Identification' | 'Contract' | 'Certificate' | 'WorkPermit' | 'Medical' | 'Other'

export interface EmployeeDocument {
  id: string
  employeeId: string
  title: string
  category: DocumentCategory
  fileName: string
  contentType: string
  sizeBytes: number
  issuedOn: string | null
  expiresOn: string | null
  /** Negative once expired; null without an expiry date. */
  daysToExpiry: number | null
  notes: string | null
  uploadedBy: string
  uploadedAt: string
}

export interface DocumentDetails {
  title: string
  category: DocumentCategory
  issuedOn: string | null
  expiresOn: string | null
  notes: string | null
}

export type ReminderKind = 'DocumentExpiry' | 'ContractEnd' | 'ProbationEnd'

export interface ExpiringItem {
  kind: ReminderKind
  itemId: string
  employeeId: string
  employeeNumber: string
  employeeName: string
  title: string
  dueOn: string
  daysLeft: number
}

export interface ReminderRunResult {
  itemsReminded: number
  sentTo: string | null
  mailConfigured: boolean
}

export interface ImportRowError {
  row: number
  message: string
}

export interface ImportResult {
  rows: number
  created: number
  committed: boolean
  errors: ImportRowError[]
  numbers: string[]
}

// Leave

export type LeaveAccrual = 'Monthly' | 'Yearly' | 'NoBalance'
export type LeaveStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'
export type DayOfWeek = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

export interface LeaveType {
  id: string
  code: string
  name: string
  accrual: LeaveAccrual
  daysPerYear: number | null
  maxDaysPerRequest: number | null
  carryForwardMax: number
  countsCalendarDays: boolean
  isPaid: boolean
  eligibleGender: Gender | null
  minServiceMonths: number
  documentRequiredAfterDays: number | null
  description: string | null
  isActive: boolean
}

export type SaveLeaveType = Omit<LeaveType, 'id' | 'isActive'>

export interface Holiday {
  id: string
  date: string
  name: string
}

export interface LeaveSettings {
  workingDays: DayOfWeek[]
  leaveYearStartMonth: number
  currentLeaveYear: number
}

export interface LeaveRequest {
  id: string
  employeeId: string
  employeeNumber: string
  employeeName: string
  leaveTypeId: string
  leaveTypeName: string
  startDate: string
  endDate: string
  halfDay: boolean
  days: number
  reason: string | null
  status: LeaveStatus
  requestedBy: string
  requestedAt: string
  decidedBy: string | null
  decidedAt: string | null
  decisionNote: string | null
  documentId: string | null
  needsDocument: boolean
  canDecide: boolean
  canCancel: boolean
}

export interface CreateLeaveRequest {
  employeeId: string | null
  leaveTypeId: string
  startDate: string
  endDate: string
  halfDay: boolean
  reason: string | null
  approve?: boolean
}

export interface LeavePreview {
  days: number
  availableAfter: number | null
  needsDocument: boolean
  problems: string[]
}

export interface LeaveBalance {
  year: number
  earned: number
  fullYear: number
  carriedForward: number
  adjusted: number
  taken: number
  pending: number
  available: number
}

export interface LeaveTypeBalance {
  leaveTypeId: string
  code: string
  name: string
  accrual: LeaveAccrual
  isPaid: boolean
  balance: LeaveBalance | null
  usedThisYear: number
  eligibleFrom: string | null
}

export interface LeaveAdjustment {
  id: string
  leaveTypeId: string
  leaveTypeName: string
  leaveYear: number
  days: number
  reason: string
  recordedBy: string
  recordedAt: string
}

export interface EmployeeLeave {
  employeeId: string
  employeeNumber: string
  employeeName: string
  gender: Gender
  hireDate: string
  year: number
  yearStart: string
  yearEnd: string
  balances: LeaveTypeBalance[]
  adjustments: LeaveAdjustment[]
  requests: LeaveRequest[]
  canManage: boolean
}

export interface LeaveCalendarEntry {
  requestId: string
  employeeId: string
  employeeName: string
  departmentName: string | null
  /** Null when the viewer may only know the person is away. */
  leaveTypeName: string | null
  startDate: string
  endDate: string
  halfDay: boolean
  status: LeaveStatus
}

export interface LeaveCalendar {
  from: string
  to: string
  entries: LeaveCalendarEntry[]
  holidays: Holiday[]
  workingDays: DayOfWeek[]
}

// Attendance

/** "HH:mm:ss" */
export type TimeOfDay = string
export type ClockDirection = 'In' | 'Out' | 'Unknown'
export type ClockSource = 'Web' | 'Import' | 'Manual'
export type DayStatus = 'Present' | 'Incomplete' | 'Absent' | 'OnLeave' | 'Holiday' | 'RestDay' | 'Upcoming' | 'NotEmployed'

export interface Schedule {
  id: string
  name: string
  startTime: TimeOfDay
  endTime: TimeOfDay
  breakMinutes: number
  workingDays: DayOfWeek[]
  graceMinutes: number
  overtimeMinimumMinutes: number
  scheduledMinutes: number
  overnight: boolean
  isDefault: boolean
  isActive: boolean
  employeeCount: number
}

export type SaveSchedule = Pick<Schedule, 'name' | 'startTime' | 'endTime' | 'breakMinutes' | 'workingDays' | 'graceMinutes' | 'overtimeMinimumMinutes'>

export interface EmployeeAttendance {
  employeeId: string
  employeeNumber: string
  employeeName: string
  deviceUserId: string | null
  currentScheduleId: string | null
  currentScheduleName: string | null
  onDefaultSchedule: boolean
  assignments: { id: string; scheduleId: string; scheduleName: string; effectiveFrom: string; recordedBy: string }[]
}

export interface Punch {
  id: string
  time: string
  /** Company local time, "YYYY-MM-DDTHH:mm:ss" */
  localTime: string
  direction: ClockDirection
  source: ClockSource
  recordedBy: string
  note: string | null
  latitude: number | null
  longitude: number | null
  accuracyMeters: number | null
}

export interface MyClock {
  employeeId: string
  employeeName: string
  localNow: string
  scheduleName: string | null
  startTime: TimeOfDay | null
  endTime: TimeOfDay | null
  today: Punch[]
  next: ClockDirection
}

export interface TimesheetDay {
  date: string
  status: DayStatus
  scheduleName: string | null
  scheduledMinutes: number
  firstIn: string | null
  lastOut: string | null
  punchCount: number
  workedMinutes: number
  lateMinutes: number
  earlyLeaveMinutes: number
  overtimeMinutes: number
  restDayOvertimeMinutes: number
  holidayName: string | null
  leave: { leaveType: string; paid: boolean; halfDay: boolean } | null
}

export interface TimesheetTotals {
  workingDays: number
  daysPresent: number
  daysAbsent: number
  daysIncomplete: number
  daysLate: number
  lateMinutes: number
  paidLeaveDays: number
  unpaidLeaveDays: number
  workedMinutes: number
  overtimeMinutes: number
  restDayOvertimeMinutes: number
}

export interface Timesheet {
  employeeId: string
  employeeNumber: string
  employeeName: string
  from: string
  to: string
  days: TimesheetDay[]
  totals: TimesheetTotals
  punches: Punch[]
  canCorrect: boolean
}

export interface AttendanceSummaryRow {
  employeeId: string
  employeeNumber: string
  employeeName: string
  departmentName: string | null
  totals: TimesheetTotals
}

export interface DayBoardRow {
  employeeId: string
  employeeNumber: string
  employeeName: string
  departmentName: string | null
  day: TimesheetDay
}

export interface PunchImportResult {
  headings: string[]
  columns: { id: string | null; dateTime: string | null; date: string | null; time: string | null; direction: string | null }
  lines: number
  matched: number
  alreadyImported: number
  imported: number
  from: string | null
  to: string | null
  unknownUsers: { deviceUserId: string; punches: number }[]
  errors: string[]
}

// Payroll

export type PayComponentKind = 'Earning' | 'Deduction'
export type PayLineKind = 'Earning' | 'Deduction' | 'Statutory' | 'Employer'
export type PayrollRunKind = 'Regular' | 'OffCycle' | 'Reversal'
export type PayrollStatus = 'Draft' | 'Calculated' | 'Submitted' | 'Approved' | 'Locked'

export interface PayComponent {
  id: string; code: string; name: string; kind: PayComponentKind; taxable: boolean; pensionable: boolean; description: string | null; isActive: boolean
}
export type SavePayComponent = Omit<PayComponent, 'id' | 'isActive'>

export interface PayItem {
  id: string; componentId: string; componentCode: string; componentName: string; kind: PayComponentKind; amount: number
  startDate: string; endDate: string | null; note: string | null; current: boolean
}
export interface SavePayItem { componentId: string; amount: number; startDate: string; endDate: string | null; note: string | null }

export interface EmployeePay {
  employeeId: string; employeeName: string; currencyCode: string; basicSalary: number | null; salaryEffectiveDate: string | null
  resident: boolean; secondaryEmployment: boolean; items: PayItem[]
}

export interface PayrollSettings {
  countryCode: string; countryName: string; currencyCode: string; currencyMinorUnits: number; supportedFrom: string; notes: string[]
}

export interface PayrollRun {
  id: string; year: number; month: number; periodStart: string; periodEnd: string; kind: PayrollRunKind; name: string; payDate: string
  status: PayrollStatus; reversesRunId: string | null; reversedByRunId: string | null
  createdBy: string; createdAt: string; calculatedBy: string | null; calculatedAt: string | null; submittedBy: string | null; submittedAt: string | null
  approvedBy: string | null; approvedAt: string | null; lockedBy: string | null; lockedAt: string | null; returnNote: string | null
  employeeCount: number; gross: number; deductions: number; net: number; employerCost: number; warnings: string[]; currencyCode: string
  canPrepare: boolean; canApprove: boolean; canReverse: boolean
}

export interface PayrollInput {
  id: string; employeeId: string; employeeNumber: string; employeeName: string; componentId: string; componentName: string
  kind: PayComponentKind; amount: number; note: string | null
}

export interface PayslipSummary {
  id: string; employeeId: string; employeeNumber: string; employeeName: string; departmentName: string | null
  gross: number; paye: number; totalDeductions: number; net: number; employerContributions: number; warnings: string[]
}

export interface PayLine { code: string; name: string; kind: PayLineKind; amount: number; taxable: boolean; pensionable: boolean; note: string | null }

export interface Payslip {
  id: string; runId: string; runName: string; year: number; month: number; payDate: string; runStatus: PayrollStatus
  employeeId: string; employeeNumber: string; employeeName: string; departmentName: string | null; positionTitle: string | null
  taxNumber: string | null; socialSecurityNumber: string | null; paymentMethod: PaymentMethod; paidTo: string | null
  lines: PayLine[]; gross: number; totalDeductions: number; net: number; employerContributions: number; warnings: string[]; currencyCode: string
}

export interface VarianceRow {
  employeeId: string; employeeNumber: string; employeeName: string; change: string; previousGross: number | null; gross: number | null
  previousNet: number | null; net: number | null; netChange: number; netChangePercent: number | null
}
export interface PayrollVariance {
  previousYear: number; previousMonth: number; previousGross: number; previousNet: number; gross: number; net: number; rows: VarianceRow[]
}

export interface EmailPayslipsResult { sent: number; alreadySent: number; noEmail: string[]; failed: string[]; mailConfigured: boolean; remaining: number }
export interface MyPayslip { id: string; runName: string; year: number; month: number; payDate: string; gross: number; net: number; currencyCode: string }

export type ProfileChangeKind = 'Personal' | 'Payment' | 'NextOfKin'
export type ProfileChangeStatus = 'Pending' | 'Approved' | 'Rejected' | 'Withdrawn'
/** `current` differs from `before` when HR changed the field after the request was made. */
export interface ChangeField { label: string; before: string | null; proposed: string | null; current: string | null }
export interface ProfileChange {
  id: string; employeeId: string; employeeNumber: string; employeeName: string; kind: ProfileChangeKind; what: string; status: ProfileChangeStatus
  reason: string | null; requestedBy: string; requestedAt: string; decidedBy: string | null; decidedAt: string | null; decisionNote: string | null
  changes: ChangeField[]; canDecide: boolean; canWithdraw: boolean
}
export interface Announcement { id: string; title: string; body: string; pinned: boolean; expiresOn: string | null; publishedBy: string; publishedAt: string; current: boolean }
export interface SaveAnnouncementRequest { title: string; body: string; pinned: boolean; expiresOn: string | null }
export interface Inbox { leaveRequests: number; profileChanges: number; payrollApprovals: number; requisitions: number; probations: number; resignations: number; total: number }
export interface TeamLeave { startDate: string; endDate: string; leaveTypeName: string; pending: boolean }
export interface TeamMember {
  id: string; employeeNumber: string; name: string; positionTitle: string | null; departmentName: string | null; directReport: boolean
  status: EmployeeStatus; today: TimesheetDay | null; upcomingLeave: TeamLeave[]
}

export type RequisitionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Closed'
export interface RequisitionDetails {
  title: string; departmentId: string | null; positionId: string | null; branchId: string | null; openings: number; employmentType: ContractType
  salaryMin: number | null; salaryMax: number | null; targetStartDate: string | null; justification: string
}
export interface Requisition extends RequisitionDetails {
  id: string; number: string; departmentName: string | null; positionTitle: string | null; branchName: string | null; status: RequisitionStatus
  requestedBy: string; requestedAt: string; decidedBy: string | null; decidedAt: string | null; decisionNote: string | null
  postings: number; hired: number; canEdit: boolean; canDecide: boolean; canClose: boolean
}
export type PostingStatus = 'Draft' | 'Open' | 'Closed'
export interface PostingDetails { title: string; description: string; location: string | null; closingDate: string | null; showSalary: boolean }
export type ApplicationStage = 'Applied' | 'Screening' | 'Interview' | 'Offer' | 'Hired' | 'Rejected' | 'Withdrawn'
export interface Posting extends PostingDetails {
  id: string; requisitionId: string; requisitionNumber: string; status: PostingStatus; publishedAt: string | null; acceptingApplications: boolean
  stages: Partial<Record<ApplicationStage, number>>; applications: number
}
export type ApplicationSource = 'CareersPage' | 'AddedByHr'
export interface ApplicationSummary {
  id: string; postingId: string; name: string; email: string; phone: string | null; stage: ApplicationStage; rating: number | null
  source: ApplicationSource; appliedAt: string; hasCv: boolean; employeeId: string | null
}
export interface ApplicationEvent { text: string; stage: ApplicationStage | null; by: string; at: string }
export interface JobApplication {
  id: string; postingId: string; postingTitle: string; firstName: string; lastName: string; email: string; phone: string | null
  coverLetter: string | null; stage: ApplicationStage; rating: number | null; source: ApplicationSource; appliedAt: string; consentedAt: string | null
  cvFileName: string | null; cvSizeBytes: number; employeeId: string | null; events: ApplicationEvent[]
}
export interface PublicPosting {
  id: string; title: string; description: string; location: string | null; departmentName: string | null; employmentType: ContractType
  closingDate: string | null; salary: string | null; publishedAt: string | null
}
export interface Careers { companyName: string; jobs: PublicPosting[] }

export type InterviewStatus = 'Scheduled' | 'Completed' | 'Cancelled'
export type Recommendation = 'StrongNo' | 'No' | 'Yes' | 'StrongYes'
export interface CriterionScore { criterion: string; score: number; comment: string | null }
export interface Scorecard {
  id: string; interviewerId: string; interviewerName: string; scores: CriterionScore[]; recommendation: Recommendation; comments: string | null
  average: number; submittedAt: string
}
export interface Interview {
  id: string; applicationId: string; candidateName: string; postingId: string; jobTitle: string; startsAt: string
  /** Company local time, "YYYY-MM-DDTHH:mm:ss" */
  localStart: string
  durationMinutes: number; location: string | null; status: InterviewStatus; panel: { employeeId: string; name: string; submitted: boolean }[]
  criteria: string[]; myScorecard: Scorecard | null; scorecards: Scorecard[]; onPanel: boolean; cvFileName: string | null; coverLetter: string | null
}
export interface ScheduleInterviewRequest {
  localStart: string; durationMinutes: number; location: string | null; panelIds: string[]; criteria: string[]; emailCandidate: boolean
}
export type OfferStatus = 'Draft' | 'Sent' | 'Accepted' | 'Declined' | 'Withdrawn'
export interface OfferTerms {
  jobTitle: string; startDate: string; monthlySalary: number; contractType: ContractType; contractEndDate: string | null; probationMonths: number
  respondBy: string; extraTerms: string | null
}
export interface Offer extends OfferTerms {
  id: string; applicationId: string; candidateName: string; candidateEmail: string; status: OfferStatus; createdBy: string; createdAt: string
  sentAt: string | null; respondedAt: string | null; responseNote: string | null; warnings: string[]
}
export interface OfferTemplate { body: string; isDefault: boolean; placeholders: string[] }
export interface HireRequest {
  gender: Gender; dateOfBirth: string | null; nationality: string | null; workEmail: string | null; managerId: string | null
  onboarding: StartOnboardingRequest | null
}
export interface HireResult {
  employeeId: string; employeeNumber: string; salaryRecorded: boolean; requisitionFilled: boolean; onboardingTasks: number; warnings: string[]
}

export type TaskOwner = 'Hr' | 'It' | 'Manager' | 'NewHire'
export interface ChecklistItem { title: string; owner: TaskOwner; dueDays: number; description: string | null }
export interface OnboardingTemplate { id: string; name: string; items: ChecklistItem[]; isDefault: boolean }
export interface OnboardingSetup { templates: OnboardingTemplate[]; standard: ChecklistItem[] }
export interface SaveOnboardingTemplate { name: string; items: ChecklistItem[]; isDefault: boolean }
export interface StartOnboardingRequest { templateId: string | null; itContactId: string | null }
export interface OnboardingStartResult { tasks: number; warnings: string[] }
export interface OnboardingTask {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; startDate: string; title: string; description: string | null
  owner: TaskOwner; assigneeId: string | null; assigneeName: string | null; dueOn: string; isDone: boolean; isOverdue: boolean
  completedAt: string | null; completedBy: string | null; note: string | null; canComplete: boolean
}
export interface OnboardingProgress {
  employeeId: string; employeeName: string; employeeNumber: string; startDate: string; managerName: string | null
  done: number; total: number; overdue: number; nextDue: string | null
}
export type ProbationOutcome = 'Confirm' | 'Extend' | 'NotConfirm'
export type ProbationReviewStatus = 'AwaitingHr' | 'Confirmed' | 'Extended' | 'NotConfirmed'
export interface ProbationReview {
  id: string; recommendation: ProbationOutcome; recommendedExtendTo: string | null; comments: string; recommendedBy: string; recommendedAt: string
  status: ProbationReviewStatus; extendedTo: string | null; decidedBy: string | null; decidedAt: string | null; decisionNote: string | null
  probationEndDate: string
}
export interface ProbationDue {
  employeeId: string; employeeName: string; employeeNumber: string; positionTitle: string | null; managerName: string | null; contractId: string
  startDate: string; probationEndDate: string; daysLeft: number; review: ProbationReview | null; history: ProbationReview[]
  canRecommend: boolean; canDecide: boolean
}

export type CycleStatus = 'Draft' | 'GoalSetting' | 'Review' | 'Closed'
export type AppraisalStatus = 'GoalSetting' | 'SelfReview' | 'ManagerReview' | 'Calibration' | 'Completed'
export interface CycleDetails {
  name: string; periodStart: string; periodEnd: string; selfReview: boolean; goalsDue: string | null; reviewsDue: string | null
  scale: string[] | null; departmentIds: string[]; peerFeedback: boolean; calibrate: boolean
}
export interface CycleSummary {
  id: string; name: string; periodStart: string; periodEnd: string; status: CycleStatus; selfReview: boolean; goalsDue: string | null
  reviewsDue: string | null; appraisals: number; completed: number
}
export interface AppraisalRow {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; departmentName: string | null; managerId: string | null
  managerName: string | null; status: AppraisalStatus; goalsAgreed: boolean; goalsSubmitted: boolean; overallRating: number | null; finalRating: number | null
  calibrationNote: string | null; acknowledged: boolean; feedbackRequested: number; feedbackReceived: number
}
export interface Cycle extends CycleDetails {
  id: string; status: CycleStatus; scale: string[]; createdBy: string; launchedAt: string | null; closedAt: string | null; resultsReleasedAt: string | null
  appraisals: AppraisalRow[]; distribution: Record<string, number>
}
export interface CycleActionResult { cycle: Cycle; warnings: string[] }
export interface Goal {
  id: string; title: string; measure: string | null; weight: number; selfRating: number | null; selfComment: string | null
  managerRating: number | null; managerComment: string | null
}
export interface Appraisal {
  id: string; cycleId: string; cycleName: string; cycleStatus: CycleStatus; periodStart: string; periodEnd: string; goalsDue: string | null
  reviewsDue: string | null; scale: string[]; cycleHasSelfReview: boolean; employeeId: string; employeeName: string; positionTitle: string | null
  managerId: string | null; managerName: string | null; status: AppraisalStatus; goals: Goal[]; goalsSubmittedAt: string | null
  goalsAgreedAt: string | null; selfComment: string | null; selfSubmittedAt: string | null; managerComment: string | null
  managerSubmittedAt: string | null; overallRating: number | null; finalRating: number | null; calibrationNote: string | null
  acknowledgedAt: string | null; employeeComment: string | null
  role: 'Employee' | 'Manager' | 'Hr'
  can: {
    editGoals: boolean; submitGoals: boolean; agreeGoals: boolean; reopenGoals: boolean; selfReview: boolean; managerReview: boolean; acknowledge: boolean
    requestFeedback: boolean; calibrate: boolean
  }
  feedback: PeerFeedback[]; cyclePeerFeedback: boolean
}
export type FeedbackStatus = 'Requested' | 'Submitted' | 'Declined'
export interface PeerFeedback {
  id: string; reviewerId: string; reviewerName: string; status: FeedbackStatus; rating: number | null; strengths: string | null; improvements: string | null
  requestedAt: string; submittedAt: string | null
}
export interface FeedbackRequest {
  id: string; appraisalId: string; employeeName: string; positionTitle: string | null; cycleName: string; goals: string[]; scale: string[]
  status: FeedbackStatus; rating: number | null; strengths: string | null; improvements: string | null; open: boolean; dueOn: string | null
}
export interface SaveGoal { id: string | null; title: string; measure: string | null; weight: number }
export interface SaveReviewRequest { ratings: { goalId: string; rating: number | null; comment: string | null }[]; comment: string | null; submit: boolean }
export interface AppraisalListItem {
  id: string; cycleName: string; cycleStatus: CycleStatus; employeeName: string; status: AppraisalStatus; goalsAgreed: boolean
  goalsSubmitted: boolean; overallRating: number | null; acknowledged: boolean; dueOn: string | null; actionDue: boolean
}
export interface MyAppraisals { mine: AppraisalListItem[]; team: AppraisalListItem[]; feedback: FeedbackRequest[]; myPlans: number; actionsDue: number }
export type PlanStatus = 'Active' | 'Succeeded' | 'NotSucceeded' | 'Cancelled'
export type PlanProgress = 'OnTrack' | 'AtRisk' | 'OffTrack'
export interface PlanObjective { objective: string; measure: string | null }
export interface PlanDetails { reason: string; objectives: PlanObjective[]; support: string | null; startDate: string; endDate: string }
export interface PlanCheckIn { id: string; on: string; progress: PlanProgress; note: string; by: string; at: string }
export interface ImprovementPlan extends PlanDetails {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; ownerId: string | null; ownerName: string | null; appraisalId: string | null
  status: PlanStatus; createdBy: string; createdAt: string; acknowledgedAt: string | null; employeeComment: string | null; outcome: string | null
  closedAt: string | null; closedBy: string | null; checkIns: PlanCheckIn[]; isMine: boolean; canManage: boolean; daysLeft: number
}
export interface PlanListItem {
  id: string; employeeId: string; employeeName: string; ownerName: string | null; startDate: string; endDate: string; status: PlanStatus
  lastProgress: PlanProgress | null; acknowledged: boolean; isMine: boolean
}
export type RecommendationStatus = 'Proposed' | 'Applied' | 'Skipped'
export interface SalaryRecommendation {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; departmentName: string | null; rating: number; currentSalary: number
  percent: number; proposedSalary: number; increase: number; adjusted: boolean; note: string | null; status: RecommendationStatus
  appliedAt: string | null; appliedBy: string | null
}
export interface SalaryReview {
  cycleId: string; cycleName: string; exists: boolean; percents: number[]; effectiveDate: string; scale: string[]
  recommendations: SalaryRecommendation[]; left: string[]; currentTotal: number; proposedTotal: number; currencyCode: string; canApply: boolean
}
export interface ApplyRaisesResult { applied: number; review: SalaryReview }

export type ExitType = 'Resignation' | 'Termination' | 'Dismissal' | 'Redundancy' | 'EndOfContract' | 'Retirement' | 'Death'
export type ExitStatus = 'Requested' | 'InProgress' | 'Completed' | 'Withdrawn'
export type ClearanceOwner = 'Hr' | 'It' | 'Manager' | 'Finance' | 'Employee'
export type LeavingReason = 'Pay' | 'CareerGrowth' | 'Management' | 'WorkEnvironment' | 'WorkLifeBalance' | 'Relocation' | 'Studies' | 'Personal' | 'Retirement' | 'Other'
export interface Notice { days: number; basis: string; source: string; monthsOfService: number; suggestedLastDay: string }
export interface ClearanceStep { title: string; owner: ClearanceOwner; dueDays: number; description: string | null }
export interface ClearanceItem {
  id: string; exitId: string; employeeId: string; employeeName: string; lastWorkingDay: string; title: string; description: string | null
  owner: ClearanceOwner; assigneeId: string | null; assigneeName: string | null; dueOn: string; isDone: boolean; isOverdue: boolean
  completedAt: string | null; completedBy: string | null; note: string | null; canComplete: boolean
}
export interface InterviewAnswers {
  mainReason: LeavingReason | null; roleRating: number | null; managerRating: number | null; payRating: number | null; growthRating: number | null
  cultureRating: number | null; wouldRecommend: boolean | null; wouldReturn: boolean | null; wentWell: string | null; couldImprove: string | null; comments: string | null
}
export interface ExitInterview { answers: InterviewAnswers; byEmployee: boolean; submittedBy: string | null; submittedAt: string | null }
export interface ExitListItem {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; departmentName: string | null; type: ExitType; status: ExitStatus
  noticeGivenOn: string; lastWorkingDay: string; cleared: number; items: number; interviewed: boolean
}
export interface EmployeeExit {
  id: string; employeeId: string; employeeName: string; employeeNumber: string; positionTitle: string | null; departmentName: string | null
  managerName: string | null; hireDate: string; type: ExitType; reason: string | null; status: ExitStatus; noticeGivenOn: string; lastWorkingDay: string
  payInLieu: boolean; notice: Notice; noticeDaysGiven: number; shortNotice: boolean; requestedBy: string; requestedAt: string
  acceptedBy: string | null; acceptedAt: string | null; eligibleForRehire: boolean | null; completionNote: string | null; completedBy: string | null
  completedAt: string | null; withdrawnBy: string | null; withdrawnAt: string | null; withdrawalNote: string | null; clearance: ClearanceItem[]
  interview: ExitInterview | null; warnings: string[]; canComplete: boolean
}
export interface ExitActionResult { exit: EmployeeExit; warnings: string[] }
export interface MyExit {
  id: string; type: ExitType; status: ExitStatus; noticeGivenOn: string; lastWorkingDay: string; reason: string | null; clearance: ClearanceItem[]
  interview: ExitInterview | null; canWithdraw: boolean; canInterview: boolean
}
export interface SettlementLine { componentId: string; label: string; amount: number; basis: string | null }
export interface DuesLine extends SettlementLine { componentName: string; kind: PayComponentKind }
export interface FinalDues {
  exitId: string; employeeId: string; employeeName: string; type: ExitType; exitStatus: ExitStatus; lastWorkingDay: string; monthlyBasic: number | null
  dailyRate: number; leaveDays: number; noticeDays: number; payInLieu: boolean; saved: boolean; status: 'Draft' | 'InPayroll'; lines: DuesLine[]
  earnings: number; deductions: number; runId: string | null; runName: string | null; runStatus: PayrollStatus | null; notes: string[]; canSend: boolean
  currencyCode: string
}
export interface FormerEmployee {
  employeeId: string; employeeNumber: string; name: string; departmentName: string | null; hireDate: string; lastWorkingDay: string | null
  exitType: ExitType | null; eligibleForRehire: boolean | null; keepUntil: string | null; due: boolean; anonymisedAt: string | null
}
export interface Archive { retentionYears: number; people: FormerEmployee[]; canRemove: boolean }
export interface Count { label: string; value: number }
export interface MonthValue { year: number; month: number; value: number }
export interface HeadcountReport {
  asOf: string; total: number; suspended: number; byDepartment: Count[]; byBranch: Count[]; byContract: Count[]; byGender: Count[]; byAge: Count[]
  byService: Count[]; trend: MonthValue[]
}
export interface TurnoverMonth { year: number; month: number; hires: number; leavers: number; voluntary: number; headcount: number }
export interface TurnoverReport {
  from: string; to: string; hires: number; leavers: number; voluntary: number; averageHeadcount: number; rate: number | null; voluntaryRate: number | null
  months: TurnoverMonth[]; byType: Count[]; byDepartment: Count[]; byReason: Count[] | null; byService: Count[]
}
export interface LiabilityRow { employeeId: string; employeeNumber: string; name: string; department: string | null; days: number; dailyRate: number | null; amount: number | null }
export interface LeaveLiabilityReport {
  asOf: string; leaveType: string | null; days: number; amount: number | null; daysByDepartment: Count[]; amountByDepartment: Count[] | null
  people: LiabilityRow[]; currencyCode: string; notes: string[]
}
export interface PayrollCostRow { department: string; people: number; gross: number; employerContributions: number; total: number }
export interface PayrollCostMonth { year: number; month: number; gross: number; employerContributions: number; net: number }
export interface PayrollCostReport {
  year: number | null; month: number | null; byDepartment: PayrollCostRow[]; gross: number; employerContributions: number; total: number
  trend: PayrollCostMonth[]; currencyCode: string
}
