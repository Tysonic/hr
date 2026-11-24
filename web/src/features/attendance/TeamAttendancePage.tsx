import { Anchor, Badge, Button, Drawer, Group, Select, Stack, Table, Tabs, Text } from '@mantine/core'
import { DatePickerInput } from '@mantine/dates'
import { notifications } from '@mantine/notifications'
import { IconDownload } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api, download } from '../../api/client'
import type { AttendanceSummaryRow, DayBoardRow, DayStatus } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useCan } from '../../lib/permissions'
import { options, useDepartments } from '../org/hooks'
import { clockTime, DayBadge, hm, TimesheetView } from './components'

type Person = { employeeId: string; employeeName: string }

/** Who's in today, and a period's totals per person for payroll. Managers see their team; HR and payroll everyone. */
export function TeamAttendancePage() {
  const canSeeAll = useCan('attendanceViewAll')
  const departments = useDepartments()
  const [departmentId, setDepartmentId] = useState<string | null>(null)
  const [person, setPerson] = useState<{ who: Person; from: string; to: string } | null>(null)
  const open = (who: Person, from: string, to: string) => setPerson({ who, from, to })
  return (
    <>
      <PageHeader title={canSeeAll ? 'Attendance' : 'Team attendance'} description={canSeeAll ? undefined : 'You and the people who report to you.'}
        actions={<Select placeholder="All departments" clearable searchable w={220} data={options(departments.data, (d) => d.name)} value={departmentId} onChange={setDepartmentId} />} />
      <Tabs defaultValue="day" keepMounted={false}>
        <Tabs.List mb="md"><Tabs.Tab value="day">Day</Tabs.Tab><Tabs.Tab value="period">Period totals</Tabs.Tab></Tabs.List>
        <Tabs.Panel value="day"><DayBoard departmentId={departmentId} open={open} /></Tabs.Panel>
        <Tabs.Panel value="period"><PeriodTotals departmentId={departmentId} open={open} /></Tabs.Panel>
      </Tabs>
      <Drawer opened={person !== null} onClose={() => setPerson(null)} position="right" size="xl" title={person?.who.employeeName}>
        {person && <TimesheetView employeeId={person.who.employeeId} from={person.from} to={person.to} />}
      </Drawer>
    </>
  )
}

const order: DayStatus[] = ['Absent', 'Incomplete', 'Present', 'OnLeave', 'Holiday', 'RestDay', 'Upcoming', 'NotEmployed']

function DayBoard({ departmentId, open }: { departmentId: string | null; open: (p: Person, from: string, to: string) => void }) {
  const [date, setDate] = useState<string | null>(dayjs().format('YYYY-MM-DD'))
  const day = useQuery({
    queryKey: ['attendance', 'day', date, departmentId],
    queryFn: () => api.get<DayBoardRow[]>('/attendance/day', { date, departmentId }),
    enabled: !!date,
  })
  const rows = [...(day.data ?? [])].sort((a, b) => order.indexOf(a.day.status) - order.indexOf(b.day.status) || b.day.lateMinutes - a.day.lateMinutes)
  const count = (s: DayStatus) => rows.filter((r) => r.day.status === s).length
  const isToday = date === dayjs().format('YYYY-MM-DD')
  return (
    <Stack>
      <Group>
        <DatePickerInput w={200} value={date} onChange={setDate} valueFormat="ddd D MMM YYYY" maxDate={dayjs().format('YYYY-MM-DD')} />
        {day.data && (
          <Group gap="xs">
            <Badge color="green" variant="light">{count('Present') + count('Incomplete')} in</Badge>
            <Badge color="orange" variant="light">{rows.filter((r) => r.day.lateMinutes > 0).length} late</Badge>
            <Badge color="blue" variant="light">{count('OnLeave')} on leave</Badge>
            {!isToday && <Badge color="red" variant="light">{count('Absent')} absent</Badge>}
            {isToday && <Badge color="gray" variant="light">{count('Upcoming')} not in yet</Badge>}
          </Group>
        )}
      </Group>
      {day.isPending ? <Loading /> : day.isError ? <LoadError error={day.error} /> : (
        <Table.ScrollContainer minWidth={700}>
          <Table striped>
            <Table.Thead>
              <Table.Tr><Table.Th>Person</Table.Th><Table.Th>Status</Table.Th><Table.Th>In</Table.Th><Table.Th>Out</Table.Th><Table.Th ta="right">Late</Table.Th><Table.Th ta="right">Worked</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {rows.length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="md">Nobody to show.</Text></Table.Td></Table.Tr>}
              {rows.map((r) => (
                <Table.Tr key={r.employeeId}>
                  <Table.Td>
                    <Anchor component="button" size="sm" fw={500} onClick={() => open(r, dayjs(date).startOf('week').format('YYYY-MM-DD'), dayjs(date).endOf('week').format('YYYY-MM-DD'))}>{r.employeeName}</Anchor>
                    <Text size="xs" c="dimmed">{[r.employeeNumber, r.departmentName].filter(Boolean).join(' · ')}</Text>
                  </Table.Td>
                  <Table.Td>{r.day.status === 'Upcoming' ? <Text size="xs" c="dimmed">Not in yet</Text> : <DayBadge day={r.day} />}</Table.Td>
                  <Table.Td>{clockTime(r.day.firstIn)}</Table.Td>
                  <Table.Td>{clockTime(r.day.lastOut)}</Table.Td>
                  <Table.Td ta="right" c={r.day.lateMinutes ? 'orange' : undefined}>{hm(r.day.lateMinutes)}</Table.Td>
                  <Table.Td ta="right">{hm(r.day.workedMinutes)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </Stack>
  )
}

function PeriodTotals({ departmentId, open }: { departmentId: string | null; open: (p: Person, from: string, to: string) => void }) {
  const lastMonth = dayjs().subtract(1, 'month')
  const [range, setRange] = useState<[string | null, string | null]>([lastMonth.startOf('month').format('YYYY-MM-DD'), lastMonth.endOf('month').format('YYYY-MM-DD')])
  const [from, to] = range
  const summary = useQuery({
    queryKey: ['attendance', 'summary', from, to, departmentId],
    queryFn: () => api.get<AttendanceSummaryRow[]>('/attendance/summary', { from, to, departmentId }),
    enabled: !!from && !!to,
  })
  const exportFile = () => download('/attendance/summary/export', `attendance-${from}-to-${to}.xlsx`, { from, to, departmentId })
    .catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))
  return (
    <Stack>
      <Group justify="space-between">
        <DatePickerInput type="range" w={280} value={range} onChange={setRange} valueFormat="D MMM YYYY" allowSingleDateInRange
          description="Up to 62 days, e.g. a pay month" />
        <Button variant="default" leftSection={<IconDownload size={16} />} disabled={!summary.data} onClick={() => void exportFile()}>Export to Excel</Button>
      </Group>
      {!from || !to ? <Text c="dimmed">Choose the first and last day.</Text> : summary.isPending ? <Loading /> : summary.isError ? <LoadError error={summary.error} /> : (
        <Table.ScrollContainer minWidth={900}>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Person</Table.Th><Table.Th ta="right">Working days</Table.Th><Table.Th ta="right">Present</Table.Th><Table.Th ta="right">Absent</Table.Th>
                <Table.Th ta="right">Missing punch</Table.Th><Table.Th ta="right">Late</Table.Th><Table.Th ta="right">Paid leave</Table.Th>
                <Table.Th ta="right">Unpaid leave</Table.Th><Table.Th ta="right">Worked</Table.Th><Table.Th ta="right">Overtime</Table.Th><Table.Th ta="right">Rest day / holiday</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {summary.data.map(({ totals: t, ...r }) => (
                <Table.Tr key={r.employeeId}>
                  <Table.Td>
                    <Anchor component="button" size="sm" fw={500} onClick={() => open(r, from, to)}>{r.employeeName}</Anchor>
                    <Text size="xs" c="dimmed">{[r.employeeNumber, r.departmentName].filter(Boolean).join(' · ')}</Text>
                  </Table.Td>
                  <Table.Td ta="right">{t.workingDays}</Table.Td>
                  <Table.Td ta="right">{t.daysPresent}</Table.Td>
                  <Table.Td ta="right" c={t.daysAbsent ? 'red' : undefined}>{t.daysAbsent || '–'}</Table.Td>
                  <Table.Td ta="right" c={t.daysIncomplete ? 'orange' : undefined}>{t.daysIncomplete || '–'}</Table.Td>
                  <Table.Td ta="right">{t.daysLate ? `${t.daysLate} (${hm(t.lateMinutes)})` : '–'}</Table.Td>
                  <Table.Td ta="right">{t.paidLeaveDays || '–'}</Table.Td>
                  <Table.Td ta="right">{t.unpaidLeaveDays || '–'}</Table.Td>
                  <Table.Td ta="right">{hm(t.workedMinutes)}</Table.Td>
                  <Table.Td ta="right">{hm(t.overtimeMinutes)}</Table.Td>
                  <Table.Td ta="right">{hm(t.restDayOvertimeMinutes)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Text size="xs" c="dimmed">Missing punches count as present but without hours; fix them on the person's timesheet before payroll.</Text>
    </Stack>
  )
}
