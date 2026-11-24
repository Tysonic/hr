import { Alert, Anchor, Badge, Button, Card, Group, SimpleGrid, Stack, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { api, ApiError } from '../../api/client'
import type { EmployeeLeave, MyClock, MyPayslip } from '../../api/types'
import { PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { roleLabels, useCan, useMe } from '../../lib/permissions'
import { clockTime } from '../attendance/components'
import { days } from '../leave/components'
import { AnnouncementCard, useAnnouncements } from '../me/AnnouncementsPage'
import { useInbox } from '../me/hooks'
import { monthName, useMoney } from '../payroll/components'

/** A 400 means the sign-in isn't linked to an employee record: show that once, not as an error per card. */
const noRecord = (e: unknown) => e instanceof ApiError && e.status === 400
const once = (count: number, e: unknown) => !noRecord(e) && count < 2

export function DashboardPage() {
  const { data: me } = useMe()
  const company = !!me?.company
  const canInbox = useCan('inbox')
  const canAnnounce = useCan('announcementsManage')
  const m = useMoney()
  const clock = useQuery({ queryKey: ['attendance', 'me'], queryFn: () => api.get<MyClock>('/attendance/me'), enabled: company, retry: once })
  const leave = useQuery({ queryKey: ['leave', 'balances', 'me', null], queryFn: () => api.get<EmployeeLeave>('/leave/balances'), enabled: company, retry: once })
  const payslips = useQuery({ queryKey: ['payroll', 'mine'], queryFn: () => api.get<MyPayslip[]>('/payroll/my-payslips'), enabled: company, retry: once })
  const inbox = useInbox(company && canInbox)
  const announcements = useAnnouncements()
  if (!me) return null

  if (!me.company) {
    return (
      <>
        <PageHeader title={`Welcome, ${me.displayName}`} description="TyHR platform administration" />
        <Card withBorder maw={400}>
          <Text size="sm" c="dimmed">Your roles</Text>
          <Group gap={4} mt={4}>{me.roles.map((r) => <Badge key={r} variant="light">{roleLabels[r]}</Badge>)}</Group>
        </Card>
      </>
    )
  }

  const unlinked = noRecord(clock.error) || noRecord(leave.error)
  const main = leave.data?.balances.find((b) => b.balance && b.code === 'ANNUAL') ?? leave.data?.balances.find((b) => b.balance)
  const latest = payslips.data?.[0]
  const punches = clock.data?.today ?? []
  const pendingLeave = leave.data?.requests.filter((r) => r.status === 'Pending').length ?? 0

  return (
    <>
      <PageHeader title={`Welcome, ${me.displayName}`} description={`${me.company.name} · ${new Date().toLocaleDateString('en-GB', { weekday: 'long', day: 'numeric', month: 'long' })}`} />
      <Stack maw={1100}>
        {unlinked && (
          <Alert color="yellow" title="Your sign-in isn't linked to an employee record">
            Ask HR to put your sign-in email ({me.email ?? 'unknown'}) as the work email on your record, to see your leave, attendance and payslips here.
          </Alert>
        )}

        {canInbox && inbox.data && inbox.data.total > 0 && (
          <Alert color="orange" title={`${inbox.data.total} waiting for your decision`}>
            <Group justify="space-between" wrap="wrap">
              <Text size="sm">
                {[inbox.data.leaveRequests && `${inbox.data.leaveRequests} leave`, inbox.data.profileChanges && `${inbox.data.profileChanges} profile change${inbox.data.profileChanges === 1 ? '' : 's'}`,
                  inbox.data.payrollApprovals && `${inbox.data.payrollApprovals} payroll`,
                  inbox.data.requisitions && `${inbox.data.requisitions} requisition${inbox.data.requisitions === 1 ? '' : 's'}`,
                  inbox.data.probations && `${inbox.data.probations} probation review${inbox.data.probations === 1 ? '' : 's'}`].filter(Boolean).join(' · ')}
              </Text>
              <Button size="xs" component={Link} to="/approvals">Review</Button>
            </Group>
          </Alert>
        )}

        {!unlinked && (
          <SimpleGrid cols={{ base: 1, sm: 3 }}>
            <Tile title="Today" link="/attendance" linkLabel={clock.data?.next === 'Out' ? 'Clock out' : 'Clock in'}>
              {clock.data && (punches.length === 0
                ? <Text fw={600}>Not clocked in</Text>
                : <Text fw={600}>In {clockTime(punches[0].localTime)}{clock.data.next === 'In' && punches.length > 1 && `, out ${clockTime(punches[punches.length - 1].localTime)}`}</Text>)}
              {clock.data?.scheduleName && <Text size="xs" c="dimmed">{clock.data.scheduleName} {clockTime(clock.data.startTime)}–{clockTime(clock.data.endTime)}</Text>}
            </Tile>
            <Tile title={main?.name ?? 'Leave'} link="/leave" linkLabel="Request leave">
              {main?.balance && <Text fw={600} fz={22}>{days(main.balance.available)}</Text>}
              <Text size="xs" c="dimmed">{main?.balance ? 'available now' : ''}{pendingLeave > 0 && ` · ${pendingLeave} request${pendingLeave === 1 ? '' : 's'} pending`}</Text>
            </Tile>
            <Tile title="Latest payslip" link="/payslips" linkLabel="My payslips">
              {latest ? <>
                <Text fw={600} fz={22}>{m(latest.net)}</Text>
                <Text size="xs" c="dimmed">{monthName(latest.year, latest.month)} · paid {date(latest.payDate)}</Text>
              </> : payslips.isSuccess && <Text size="sm" c="dimmed">None yet</Text>}
            </Tile>
          </SimpleGrid>
        )}

        <Group justify="space-between" mt="sm">
          <Text fw={600}>Announcements</Text>
          {canAnnounce && <Anchor component={Link} to="/settings/announcements" size="sm">Manage</Anchor>}
        </Group>
        {announcements.data?.length === 0 && <Text size="sm" c="dimmed">Nothing new.</Text>}
        {announcements.data?.map((a) => <AnnouncementCard key={a.id} a={a} />)}
      </Stack>
    </>
  )
}

function Tile({ title, link, linkLabel, children }: { title: string; link: string; linkLabel: string; children: ReactNode }) {
  return (
    <Card withBorder padding="md">
      <Stack gap={4} h="100%" justify="space-between">
        <Stack gap={2}>
          <Text size="sm" c="dimmed">{title}</Text>
          {children}
        </Stack>
        <Anchor component={Link} to={link} size="sm" mt="xs">{linkLabel} →</Anchor>
      </Stack>
    </Card>
  )
}
