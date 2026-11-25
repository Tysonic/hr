import {
  ActionIcon, AppShell, Avatar, Badge, Group, Menu, Text, UnstyledButton,
  useComputedColorScheme, useMantineColorScheme,
} from '@mantine/core'
import { useDisclosure, useMediaQuery } from '@mantine/hooks'
import {
  IconAddressBook, IconBeach, IconCash, IconReceipt, IconClock, IconFileImport, IconUserCheck, IconBuildingSkyscraper, IconCalendarDue, IconCalendarMonth, IconChecklist, IconBuildings, IconHierarchy2, IconHistory, IconLayoutDashboard, IconLogout, IconMoon,
  IconBriefcase, IconFileDescription, IconMessages, IconFileCertificate, IconListCheck, IconUserPlus, IconTargetArrow, IconChartBar, IconTrendingUp, IconDoorExit, IconArchive, IconReportAnalytics, IconRosetteDiscountCheck, IconListDetails, IconInbox, IconSitemap, IconSpeakerphone, IconSun, IconUserCircle, IconUsers, IconUsersGroup, IconMenu2, IconSettings, IconUserSearch, IconWorld,
} from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { Outlet } from 'react-router'
import { Sidebar } from './Sidebar'
import { signOut } from '../lib/auth'
import { can, roleLabels, useMe, type Policy } from '../lib/permissions'
import { useInbox, useMyInterviews, useMyTasks, scorecardsOwed } from '../features/me/hooks'
import { useMyAppraisals } from '../features/performance/components'
import { useMyClearance } from '../features/exits/components'

type NavItem = { to: string; label: string; icon: ReactNode; policy?: Policy; badge?: 'inbox' | 'interviews' | 'tasks' | 'reviews' }

// Each item shows only to users whose roles allow it, and a section only if it has any. The everyday items come first.
const everyday: NavItem[] = [
  { to: '/', label: 'Home', icon: <IconLayoutDashboard size={18} /> },
  { to: '/me', label: 'My profile', icon: <IconUserCircle size={18} />, policy: 'selfService' },
  { to: '/approvals', label: 'Approvals', icon: <IconInbox size={18} />, policy: 'inbox', badge: 'inbox' },
  { to: '/reviews', label: 'My reviews', icon: <IconTargetArrow size={18} />, policy: 'selfService', badge: 'reviews' },
  { to: '/tasks', label: 'My tasks', icon: <IconListCheck size={18} />, policy: 'selfService', badge: 'tasks' },
  { to: '/interviews', label: 'My interviews', icon: <IconMessages size={18} />, policy: 'selfService', badge: 'interviews' },
  { to: '/team', label: 'My team', icon: <IconUsersGroup size={18} />, policy: 'leaveApprove' },
]

const sections: { title: string; icon: ReactNode; items: NavItem[] }[] = [
  {
    title: 'People',
    icon: <IconUsers size={18} />,
    items: [
      { to: '/employees', label: 'Employees', icon: <IconUsers size={18} />, policy: 'employeesView' },
      { to: '/reports', label: 'Reports', icon: <IconReportAnalytics size={18} />, policy: 'reportsView' },
      { to: '/directory', label: 'Directory', icon: <IconAddressBook size={18} />, policy: 'directoryView' },
      { to: '/org-chart', label: 'Org chart', icon: <IconHierarchy2 size={18} />, policy: 'orgView' },
      { to: '/onboarding', label: 'Onboarding', icon: <IconUserPlus size={18} />, policy: 'employeesManage' },
      { to: '/performance', label: 'Review cycles', icon: <IconChartBar size={18} />, policy: 'performanceManage' },
      { to: '/plans', label: 'Improvement plans', icon: <IconTrendingUp size={18} />, policy: 'plans' },
      { to: '/exits', label: 'Exits', icon: <IconDoorExit size={18} />, policy: 'exitsManage' },
      { to: '/former-employees', label: 'Former employees', icon: <IconArchive size={18} />, policy: 'exitsManage' },
      { to: '/probation', label: 'Probation reviews', icon: <IconRosetteDiscountCheck size={18} />, policy: 'employeesView' },
      { to: '/expiries', label: 'Expiring soon', icon: <IconCalendarDue size={18} />, policy: 'expiriesView' },
    ],
  },
  {
    title: 'Recruitment',
    icon: <IconUserSearch size={18} />,
    items: [
      { to: '/recruitment', label: 'Jobs', icon: <IconBriefcase size={18} />, policy: 'recruitmentManage' },
      { to: '/recruitment/requisitions', label: 'Requisitions', icon: <IconFileDescription size={18} />, policy: 'recruitmentRequest' },
    ],
  },
  {
    title: 'Leave',
    icon: <IconBeach size={18} />,
    items: [
      { to: '/leave', label: 'My leave', icon: <IconBeach size={18} />, policy: 'leaveView' },
      { to: '/leave/approvals', label: 'Leave requests', icon: <IconChecklist size={18} />, policy: 'leaveApprove' },
      { to: '/leave/calendar', label: 'Leave calendar', icon: <IconCalendarMonth size={18} />, policy: 'leaveView' },
    ],
  },
  {
    title: 'Attendance',
    icon: <IconClock size={18} />,
    items: [
      { to: '/attendance', label: 'My attendance', icon: <IconClock size={18} />, policy: 'attendanceClock' },
      { to: '/attendance/team', label: 'Team attendance', icon: <IconUserCheck size={18} />, policy: 'attendanceView' },
      { to: '/attendance/import', label: 'Import punches', icon: <IconFileImport size={18} />, policy: 'attendanceManage' },
    ],
  },
  {
    title: 'Payroll',
    icon: <IconCash size={18} />,
    items: [
      { to: '/payslips', label: 'My payslips', icon: <IconReceipt size={18} />, policy: 'payslipsOwn' },
      { to: '/payroll', label: 'Payrolls', icon: <IconCash size={18} />, policy: 'payrollView' },
    ],
  },
  {
    title: 'Platform',
    icon: <IconWorld size={18} />,
    items: [{ to: '/platform/tenants', label: 'Companies', icon: <IconBuildings size={18} />, policy: 'platformTenants' }],
  },
  {
    title: 'Settings',
    icon: <IconSettings size={18} />,
    items: [
      { to: '/settings/company', label: 'Company', icon: <IconBuildingSkyscraper size={18} />, policy: 'companyView' },
      { to: '/settings/organisation', label: 'Organisation', icon: <IconSitemap size={18} />, policy: 'orgView' },
      { to: '/settings/onboarding', label: 'Onboarding checklists', icon: <IconListDetails size={18} />, policy: 'employeesManage' },
      { to: '/settings/offer-letter', label: 'Offer letter', icon: <IconFileCertificate size={18} />, policy: 'recruitmentManage' },
      { to: '/settings/announcements', label: 'Announcements', icon: <IconSpeakerphone size={18} />, policy: 'announcementsManage' },
      { to: '/settings/leave', label: 'Leave', icon: <IconBeach size={18} />, policy: 'leaveSetup' },
      { to: '/settings/attendance', label: 'Attendance', icon: <IconClock size={18} />, policy: 'attendanceManage' },
      { to: '/settings/payroll', label: 'Payroll', icon: <IconReceipt size={18} />, policy: 'payrollSetup' },
      { to: '/settings/audit', label: 'Audit trail', icon: <IconHistory size={18} />, policy: 'auditView' },
    ],
  },
]

export function AppLayout() {
  // Like Tycount: open on a wide screen, icons only on a narrower one. On a phone it's a drawer, closed to start with.
  const mobile = useMediaQuery('(max-width: 48em)', undefined, { getInitialValueInEffect: false })
  const [wideOpen, wide] = useDisclosure(window.innerWidth > 1080)
  const [drawerOpen, drawer] = useDisclosure()
  const opened = mobile ? drawerOpen : wideOpen
  const menu = mobile ? drawer : wide
  const rail = !mobile && !wideOpen
  const { data: me } = useMe()
  const roles = me?.roles ?? []
  const { setColorScheme } = useMantineColorScheme()
  const scheme = useComputedColorScheme('light')
  const inbox = useInbox(!!me?.company && can(roles, 'inbox'))
  const interviews = useMyInterviews(!!me?.company && can(roles, 'selfService'))
  const owed = scorecardsOwed(interviews.data)
  const tasks = useMyTasks(!!me?.company && can(roles, 'selfService'))
  const reviews = useMyAppraisals(!!me?.company && can(roles, 'selfService'))
  const clearance = useMyClearance(!!me?.company && can(roles, 'selfService'))
  const badges = { inbox: inbox.data?.total ?? 0, interviews: owed, tasks: (tasks.data ?? []).filter((t) => !t.isDone).length + (clearance.data ?? []).filter((i) => !i.isDone).length, reviews: reviews.data?.actionsDue ?? 0 }

  const allowed = (items: NavItem[]) => items
    .filter((i) => (!i.policy || can(roles, i.policy))
      && (i.to !== '/interviews' || !!interviews.data?.length)
      && (i.to !== '/tasks' || !!tasks.data?.length || !!clearance.data?.length)
      && (i.to !== '/reviews' || !!(reviews.data?.mine.length || reviews.data?.team.length || reviews.data?.feedback.length || reviews.data?.myPlans)))
    .map((i) => ({ ...i, badge: i.badge && badges[i.badge] }))
  const visible = sections.map((s) => ({ ...s, items: allowed(s.items) })).filter((s) => s.items.length > 0)

  return (
    <AppShell layout="alt" header={{ height: 64 }} navbar={{ width: rail ? 65 : 240, breakpoint: 'sm', collapsed: { mobile: !drawerOpen } }}
      padding={{ base: 'sm', sm: 'lg' }} transitionDuration={225}>
      <AppShell.Header className="app-topbar" withBorder={false}>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap" miw={0}>
            {!opened && (
              <ActionIcon className="frame-btn" variant="default" size="lg" aria-label="Open menu" onClick={menu.open}>
                <IconMenu2 size={20} />
              </ActionIcon>
            )}
            {me?.company && <Text fw={600} truncate>{me.company.name}</Text>}
          </Group>
          <Group gap="xs" wrap="nowrap">
            <UserMenu />
            <ActionIcon className="frame-btn" variant="default" size="lg" aria-label="Toggle colour scheme"
              onClick={() => setColorScheme(scheme === 'light' ? 'dark' : 'light')}>
              {scheme === 'light' ? <IconMoon size={18} /> : <IconSun size={18} />}
            </ActionIcon>
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar className="app-sidebar" withBorder={false} data-rail={rail || undefined}>
        <Sidebar items={allowed(everyday)} sections={visible} rail={rail} onClose={menu.close} onNavigate={drawer.close} />
      </AppShell.Navbar>

      <AppShell.Main className="app-main">
        <Outlet />
      </AppShell.Main>
    </AppShell>
  )
}

function UserMenu() {
  const { data: me } = useMe()
  if (!me) return null
  const initials = me.displayName.split(/\s+/).map((w) => w[0]).join('').slice(0, 2).toUpperCase()
  return (
    <Menu position="bottom-end" width={260}>
      <Menu.Target>
        <UnstyledButton aria-label="Signed-in user">
          <Group gap="xs" wrap="nowrap">
            <Avatar size="sm" color="indigo" radius="xl">{initials}</Avatar>
            <Text size="sm" fw={500} visibleFrom="xs">{me.displayName}</Text>
          </Group>
        </UnstyledButton>
      </Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>Signed in as {me.userName}</Menu.Label>
        {me.company && <Menu.Label>{me.company.name}</Menu.Label>}
        <Group gap={4} px="sm" pb="xs">
          {me.roles.map((r) => <Badge key={r} size="sm" variant="light">{roleLabels[r] ?? r}</Badge>)}
        </Group>
        <Menu.Divider />
        <Menu.Item leftSection={<IconLogout size={16} />} onClick={() => void signOut()}>Sign out</Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}
