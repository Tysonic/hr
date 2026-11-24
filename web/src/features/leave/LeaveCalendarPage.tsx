import { ActionIcon, Badge, Box, Group, Select, Table, Text, Tooltip } from '@mantine/core'
import { IconChevronLeft, IconChevronRight } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { DayOfWeek, LeaveCalendar, LeaveCalendarEntry } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useCan } from '../../lib/permissions'
import { options, useDepartments } from '../org/hooks'
import { dateRange } from './components'

const weekdays: DayOfWeek[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday']

/**
 * Who is away in a month: a row per person, a column per day. HR and payroll see everyone; managers their team;
 * everyone sees their department's approved leave without its type.
 */
export function LeaveCalendarPage() {
  const [month, setMonth] = useState(() => dayjs().startOf('month'))
  const [departmentId, setDepartmentId] = useState<string | null>(null)
  const canSeeAll = useCan('leaveViewAll')
  const departments = useDepartments()
  const from = month.format('YYYY-MM-DD')
  const to = month.endOf('month').format('YYYY-MM-DD')
  const calendar = useQuery({
    queryKey: ['leave', 'calendar', from, departmentId],
    queryFn: () => api.get<LeaveCalendar>('/leave/calendar', { from, to, departmentId }),
  })

  return (
    <>
      <PageHeader title="Leave calendar" description={canSeeAll ? 'Everyone’s leave.' : 'Your leave, your team’s, and who in your department is away.'} />
      <Group mb="md">
        <ActionIcon variant="default" onClick={() => setMonth(month.subtract(1, 'month'))} aria-label="Previous month"><IconChevronLeft size={16} /></ActionIcon>
        <Text fw={600} w={140} ta="center">{month.format('MMMM YYYY')}</Text>
        <ActionIcon variant="default" onClick={() => setMonth(month.add(1, 'month'))} aria-label="Next month"><IconChevronRight size={16} /></ActionIcon>
        <Select placeholder="All departments" clearable searchable w={200} data={options(departments.data, (d) => d.name)} value={departmentId} onChange={setDepartmentId} />
        <Group gap="xs" ml="auto">
          <Badge color="green" variant="filled">Approved</Badge>
          <Badge color="yellow" variant="light">Pending</Badge>
          <Badge color="gray" variant="light">Weekend / holiday</Badge>
        </Group>
      </Group>
      {calendar.isPending ? <Loading /> : calendar.isError ? <LoadError error={calendar.error} /> : <Grid data={calendar.data} month={month} />}
    </>
  )
}

function Grid({ data, month }: { data: LeaveCalendar; month: dayjs.Dayjs }) {
  const daysInMonth = Array.from({ length: month.daysInMonth() }, (_, i) => month.add(i, 'day'))
  const holidays = new Map(data.holidays.map((h) => [h.date, h.name]))
  const people = [...new Map(data.entries.map((e) => [e.employeeId, e])).values()].sort((a, b) => a.employeeName.localeCompare(b.employeeName))
  const offDay = (d: dayjs.Dayjs) => !data.workingDays.includes(weekdays[d.day()]) || holidays.has(d.format('YYYY-MM-DD'))

  if (people.length === 0 && data.holidays.length === 0) return <Text c="dimmed">Nobody is on leave this month.</Text>
  return (
    <Table.ScrollContainer minWidth={260 + daysInMonth.length * 28}>
      <Table withColumnBorders fz="xs" verticalSpacing={4} horizontalSpacing={0} style={{ tableLayout: 'fixed' }}>
        <Table.Thead>
          <Table.Tr>
            <Table.Th w={200} px="xs">Person</Table.Th>
            {daysInMonth.map((d) => {
              const holiday = holidays.get(d.format('YYYY-MM-DD'))
              return (
                <Table.Th key={d.date()} ta="center" w={28} bg={offDay(d) ? 'var(--mantine-color-default-hover)' : undefined}>
                  <Tooltip label={holiday ?? d.format('dddd D MMMM')} disabled={!holiday && !offDay(d)}>
                    <Box><Text size="xs" c="dimmed">{d.format('dd')[0]}</Text><Text size="xs" fw={holiday ? 700 : 500} c={holiday ? 'red' : undefined}>{d.date()}</Text></Box>
                  </Tooltip>
                </Table.Th>
              )
            })}
          </Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {people.length === 0 && <Table.Tr><Table.Td colSpan={daysInMonth.length + 1} px="xs"><Text c="dimmed" size="sm">Nobody is on leave this month.</Text></Table.Td></Table.Tr>}
          {people.map((p) => {
            const entries = data.entries.filter((e) => e.employeeId === p.employeeId)
            return (
              <Table.Tr key={p.employeeId}>
                <Table.Td px="xs"><Text size="sm" fw={500} truncate>{p.employeeName}</Text>{p.departmentName && <Text size="xs" c="dimmed" truncate>{p.departmentName}</Text>}</Table.Td>
                {daysInMonth.map((d) => {
                  const day = d.format('YYYY-MM-DD')
                  const entry = entries.find((e) => e.startDate <= day && e.endDate >= day)
                  return (
                    <Table.Td key={day} p={2} bg={offDay(d) ? 'var(--mantine-color-default-hover)' : undefined}>
                      {entry && !offDay(d) && <Cell entry={entry} />}
                    </Table.Td>
                  )
                })}
              </Table.Tr>
            )
          })}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}

function Cell({ entry: e }: { entry: LeaveCalendarEntry }) {
  const approved = e.status === 'Approved'
  return (
    <Tooltip label={`${e.leaveTypeName ?? 'On leave'} · ${dateRange(e.startDate, e.endDate, e.halfDay)}${approved ? '' : ' (pending)'}`}>
      <Box h={20} style={{
        borderRadius: 3,
        background: approved ? 'var(--mantine-color-green-filled)' : 'var(--mantine-color-yellow-light)',
        border: approved ? undefined : '1px dashed var(--mantine-color-yellow-filled)',
        width: e.halfDay ? '50%' : '100%',
      }} />
    </Tooltip>
  )
}
