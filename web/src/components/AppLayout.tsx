import {
  ActionIcon, AppShell, Avatar, Badge, Burger, Group, Menu, NavLink, ScrollArea, Text, UnstyledButton,
  useComputedColorScheme, useMantineColorScheme,
} from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import {
  IconAddressBook, IconBeach, IconCash, IconReceipt, IconClock, IconFileImport, IconUserCheck, IconBuildingSkyscraper, IconCalendarDue, IconCalendarMonth, IconChecklist, IconBuildings, IconHierarchy2, IconHistory, IconLayoutDashboard, IconLogout, IconMoon,
  IconBriefcase, IconFileDescription, IconMessages, IconFileCertificate, IconListCheck, IconUserPlus, IconTargetArrow, IconChartBar, IconTrendingUp, IconDoorExit, IconArchive, IconReportAnalytics, IconRosetteDiscountCheck, IconListDetails, IconInbox, IconSitemap, IconSpeakerphone, IconSun, IconUserCircle, IconUsers, IconUsersGroup,
} from '@tabler/icons-react'
import type { ReactNode } from 'react'
import { Link, Outlet, useLocation } from 'react-router'
import { signOut } from '../lib/auth'
import { can, roleLabels, useMe, type Policy } from '../lib/permissions'
import { useInbox, useMyInterviews, useMyTasks, scorecardsOwed } from '../features/me/hooks'
import { useMyAppraisals } from '../features/performance/components'
import { useMyClearance } from '../features/exits/components'

type NavItem = { to: string; label: string; icon: ReactNode; exact?: boolean; policy?: Policy; badge?: 'inbox' | 'interviews' | 'tasks' | 'reviews' }

// Each item shows only to users whose roles allow it; later milestones add more.
const sections: { title?: string; items: NavItem[] }[] = [
  {
    items: [
      { to: '/', label: 'Home', icon: <IconLayoutDashboard size={18} />, exact: true },
      { to: '/me', label: 'My profile', icon: <IconUserCircle size={18} />, policy: 'selfService' },
      { to: '/approvals', label: 'Approvals', icon: <IconInbox size={18} />, policy: 'inbox', badge: 'inbox' },
      { to: '/reviews', label: 'My reviews', icon: <IconTargetArrow size={18} />, policy: 'selfService', badge: 'reviews' },
      { to: '/tasks', label: 'My tasks', icon: <IconListCheck size={18} />, policy: 'selfService', badge: 'tasks' },
      { to: '/interviews', label: 'My interviews', icon: <IconMessages size={18} />, policy: 'selfService', badge: 'interviews' },
      { to: '/team', label: 'My team', icon: <IconUsersGroup size={18} />, policy: 'leaveApprove' },
    ],
  },
  {
    title: 'People',
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
    items: [
      { to: '/recruitment', label: 'Jobs', icon: <IconBriefcase size={18} />, policy: 'recruitmentManage', exact: true },
      { to: '/recruitment/requisitions', label: 'Requisitions', icon: <IconFileDescription size={18} />, policy: 'recruitmentRequest' },
    ],
  },
  {
    title: 'Leave',
    items: [
      { to: '/leave', label: 'My leave', icon: <IconBeach size={18} />, policy: 'leaveView', exact: true },
      { to: '/leave/approvals', label: 'Leave requests', icon: <IconChecklist size={18} />, policy: 'leaveApprove' },
      { to: '/leave/calendar', label: 'Leave calendar', icon: <IconCalendarMonth size={18} />, policy: 'leaveView' },
    ],
  },
  {
    title: 'Attendance',
    items: [
      { to: '/attendance', label: 'My attendance', icon: <IconClock size={18} />, policy: 'attendanceClock', exact: true },
      { to: '/attendance/team', label: 'Team attendance', icon: <IconUserCheck size={18} />, policy: 'attendanceView' },
      { to: '/attendance/import', label: 'Import punches', icon: <IconFileImport size={18} />, policy: 'attendanceManage' },
    ],
  },
  {
    title: 'Payroll',
    items: [
      { to: '/payslips', label: 'My payslips', icon: <IconReceipt size={18} />, policy: 'payslipsOwn' },
      { to: '/payroll', label: 'Payrolls', icon: <IconCash size={18} />, policy: 'payrollView' },
    ],
  },
  {
    title: 'Platform',
    items: [{ to: '/platform/tenants', label: 'Companies', icon: <IconBuildings size={18} />, policy: 'platformTenants' }],
  },
  {
    title: 'Settings',
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
  const [opened, { toggle, close }] = useDisclosure()
  const { pathname } = useLocation()
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

  const isActive = (item: NavItem) => (item.exact ? pathname === item.to : pathname === item.to || pathname.startsWith(`${item.to}/`))
  const visible = sections
    .map((s) => ({ ...s, items: s.items.filter((i) => (!i.policy || can(roles, i.policy)) && (i.to !== '/interviews' || !!interviews.data?.length) && (i.to !== '/tasks' || !!tasks.data?.length || !!clearance.data?.length)
      && (i.to !== '/reviews' || !!(reviews.data?.mine.length || reviews.data?.team.length || reviews.data?.feedback.length || reviews.data?.myPlans))) }))
    .filter((s) => s.items.length > 0)

  return (
    <AppShell header={{ height: 56 }} navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }} padding={{ base: 'sm', sm: 'lg' }}>
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
            <Text fw={700} size="lg">TyHR</Text>
            {me?.company && <Text size="sm" c="dimmed" visibleFrom="xs" truncate>{me.company.name}</Text>}
          </Group>
          <Group gap="xs" wrap="nowrap">
            <UserMenu />
            <ActionIcon variant="default" size="lg" aria-label="Toggle colour scheme"
              onClick={() => setColorScheme(scheme === 'light' ? 'dark' : 'light')}>
              {scheme === 'light' ? <IconMoon size={18} /> : <IconSun size={18} />}
            </ActionIcon>
          </Group>
        </Group>
      </AppShell.Header>

      <AppShell.Navbar p="xs">
        <ScrollArea>
          {visible.map((section, i) => (
            <div key={i}>
              {section.title && <Text size="xs" c="dimmed" fw={600} tt="uppercase" px="sm" mt="md" mb={4}>{section.title}</Text>}
              {section.items.map((item) => (
                <NavLink key={item.to} component={Link} to={item.to} label={item.label} leftSection={item.icon}
                  rightSection={item.badge && badges[item.badge] ? <Badge size="sm" color="orange" circle>{badges[item.badge]}</Badge> : undefined}
                  active={isActive(item)} onClick={close} />
              ))}
            </div>
          ))}
        </ScrollArea>
      </AppShell.Navbar>

      <AppShell.Main>
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
