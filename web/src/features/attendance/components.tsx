import { ActionIcon, Badge, Button, Card, Group, Menu, Modal, Select, SimpleGrid, Stack, Table, Text, TextInput, Tooltip } from '@mantine/core'
import { DateInput, TimeInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconChevronLeft, IconChevronRight, IconMapPin, IconPlus, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { ClockDirection, DayStatus, Punch, Schedule, Timesheet, TimesheetDay, TimesheetTotals } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'

export const useSchedules = () => useQuery({ queryKey: ['attendance', 'schedules'], queryFn: () => api.get<Schedule[]>('/attendance/schedules') })

/** 487 → "8h 7m"; 0 → "–". */
export function hm(minutes: number) {
  if (!minutes) return '–'
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  return h === 0 ? `${m}m` : m === 0 ? `${h}h` : `${h}h ${m}m`
}

/** "2026-11-23T08:30:00" or "08:30:00" → "08:30". */
export const clockTime = (value: string | null | undefined) => (value ? (value.includes('T') ? value.split('T')[1] : value).slice(0, 5) : '')

export const statusLabel: Record<DayStatus, string> = {
  Present: 'Present', Incomplete: 'Missing punch', Absent: 'Absent', OnLeave: 'On leave', Holiday: 'Holiday', RestDay: 'Rest day',
  Upcoming: '', NotEmployed: 'Not yet employed',
}
export const statusColor: Record<DayStatus, string> = {
  Present: 'green', Incomplete: 'orange', Absent: 'red', OnLeave: 'blue', Holiday: 'grape', RestDay: 'gray', Upcoming: 'gray', NotEmployed: 'gray',
}

export function DayBadge({ day }: { day: TimesheetDay }) {
  if (day.status === 'Upcoming') return null
  if (day.status === 'RestDay' && day.punchCount === 0) return <Text size="xs" c="dimmed">Rest day</Text>
  const label = day.status === 'OnLeave' && day.leave ? `${day.leave.leaveType}${day.leave.halfDay ? ' (½)' : ''}`
    : day.status === 'Holiday' ? day.holidayName ?? 'Holiday' : statusLabel[day.status]
  return <Badge size="sm" variant="light" color={statusColor[day.status]}>{label}</Badge>
}

/** A month picker: ‹ November 2026 ›. Returns the first and last day. */
export function useMonth() {
  const [month, setMonth] = useState(() => dayjs().startOf('month'))
  const control = (
    <Group gap="xs" wrap="nowrap">
      <ActionIcon variant="default" onClick={() => setMonth(month.subtract(1, 'month'))} aria-label="Previous month"><IconChevronLeft size={16} /></ActionIcon>
      <Text fw={600} w={130} ta="center">{month.format('MMMM YYYY')}</Text>
      <ActionIcon variant="default" onClick={() => setMonth(month.add(1, 'month'))} aria-label="Next month"><IconChevronRight size={16} /></ActionIcon>
    </Group>
  )
  return { from: month.format('YYYY-MM-DD'), to: month.endOf('month').format('YYYY-MM-DD'), control }
}

export function TotalsCards({ totals: t }: { totals: TimesheetTotals }) {
  const cards: [string, string, string?][] = [
    ['Present', `${t.daysPresent} of ${t.workingDays} days`],
    ['Absent', String(t.daysAbsent), t.daysAbsent ? 'red' : undefined],
    ['Late', t.daysLate ? `${t.daysLate} days, ${hm(t.lateMinutes)}` : '–', t.daysLate ? 'orange' : undefined],
    ['Leave', t.paidLeaveDays + t.unpaidLeaveDays ? `${t.paidLeaveDays} paid${t.unpaidLeaveDays ? `, ${t.unpaidLeaveDays} unpaid` : ''}` : '–'],
    ['Hours worked', hm(t.workedMinutes)],
    ['Overtime', t.restDayOvertimeMinutes ? `${hm(t.overtimeMinutes)} + ${hm(t.restDayOvertimeMinutes)} rest days` : hm(t.overtimeMinutes)],
  ]
  return (
    <SimpleGrid cols={{ base: 2, sm: 3, lg: 6 }}>
      {cards.map(([label, value, color]) => (
        <Card key={label} withBorder padding="sm">
          <Text size="xs" c="dimmed">{label}</Text>
          <Text fw={600} c={color}>{value}</Text>
        </Card>
      ))}
    </SimpleGrid>
  )
}

/** One person's period: totals, then a row per day with its punches; HR can add missed punches and remove wrong ones. */
export function TimesheetView({ employeeId, from, to }: { employeeId?: string; from: string; to: string }) {
  const sheet = useQuery({
    queryKey: ['attendance', 'timesheet', employeeId ?? 'me', from, to],
    queryFn: () => api.get<Timesheet>('/attendance/timesheet', { employeeId, from, to }),
  })
  const [adding, setAdding] = useState<string | null>(null)
  const remove = useApiMutation((p: Punch) => api.delete<void>(`/attendance/punches/${p.id}`), { invalidate: ['attendance'], success: 'Punch removed.' })
  if (sheet.isPending) return <Loading />
  if (sheet.isError) return <LoadError error={sheet.error} />
  const s = sheet.data
  const punchesOn = (day: TimesheetDay) => {
    const start = day.firstIn ?? `${day.date}T00:00:00`
    const end = day.lastOut ?? day.firstIn ?? `${day.date}T23:59:59`
    return s.punches.filter((p) => p.localTime >= start && p.localTime <= end)
  }

  return (
    <Stack>
      <TotalsCards totals={s.totals} />
      <Table.ScrollContainer minWidth={760}>
        <Table striped verticalSpacing={6}>
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={110}>Day</Table.Th><Table.Th w={150}>Status</Table.Th><Table.Th>Punches</Table.Th>
              <Table.Th ta="right">Worked</Table.Th><Table.Th ta="right">Late</Table.Th><Table.Th ta="right">Left early</Table.Th>
              <Table.Th ta="right">Overtime</Table.Th>{s.canCorrect && <Table.Th w={40} />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {s.days.map((d) => {
              const punches = d.punchCount > 0 ? punchesOn(d) : []
              const off = d.status === 'RestDay' || d.status === 'Holiday' || d.status === 'NotEmployed'
              return (
                <Table.Tr key={d.date} c={off && d.punchCount === 0 ? 'dimmed' : undefined}>
                  <Table.Td><Text size="sm" fw={500}>{dayjs(d.date).format('ddd D MMM')}</Text></Table.Td>
                  <Table.Td><DayBadge day={d} /></Table.Td>
                  <Table.Td>
                    <Group gap={6}>
                      {punches.map((p) => <PunchChip key={p.id} punch={p} canRemove={s.canCorrect} onRemove={() => remove.mutate(p)} />)}
                    </Group>
                  </Table.Td>
                  <Table.Td ta="right">{hm(d.workedMinutes)}</Table.Td>
                  <Table.Td ta="right" c={d.lateMinutes ? 'orange' : undefined}>{hm(d.lateMinutes)}</Table.Td>
                  <Table.Td ta="right">{hm(d.earlyLeaveMinutes)}</Table.Td>
                  <Table.Td ta="right">{hm(d.overtimeMinutes + d.restDayOvertimeMinutes)}</Table.Td>
                  {s.canCorrect && (
                    <Table.Td>
                      <ActionIcon variant="subtle" aria-label={`Add a punch on ${d.date}`} onClick={() => setAdding(d.date)}><IconPlus size={16} /></ActionIcon>
                    </Table.Td>
                  )}
                </Table.Tr>
              )
            })}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={adding !== null} onClose={() => setAdding(null)} title={`Add a missed punch for ${s.employeeName}`}>
        {adding && <AddPunchForm employeeId={s.employeeId} date={adding} done={() => setAdding(null)} />}
      </Modal>
    </Stack>
  )
}

const sourceLabel = { Web: 'Clocked on the web', Import: 'From the device', Manual: 'Entered by HR' }

function PunchChip({ punch: p, canRemove, onRemove }: { punch: Punch; canRemove: boolean; onRemove: () => void }) {
  const label = [
    `${p.direction === 'Unknown' ? 'Punch' : `Clocked ${p.direction.toLowerCase()}`} at ${clockTime(p.localTime)}`,
    sourceLabel[p.source] + (p.source === 'Manual' ? ` (${p.recordedBy}): ${p.note}` : ''),
    p.latitude != null ? `Location ±${Math.round(p.accuracyMeters ?? 0)} m` : null,
  ].filter(Boolean).join(' · ')
  const chip = (
    <Badge variant={p.source === 'Manual' ? 'outline' : 'light'} color={p.direction === 'Out' ? 'gray' : 'indigo'} style={{ cursor: canRemove ? 'pointer' : undefined }}
      leftSection={p.latitude != null ? <IconMapPin size={10} /> : undefined}>
      {p.direction === 'In' ? '→ ' : p.direction === 'Out' ? '← ' : ''}{clockTime(p.localTime)}
    </Badge>
  )
  if (!canRemove) return <Tooltip label={label}>{chip}</Tooltip>
  return (
    <Menu position="bottom-start">
      <Menu.Target><Tooltip label={label}>{chip}</Tooltip></Menu.Target>
      <Menu.Dropdown>
        <Menu.Label>{label}</Menu.Label>
        {p.latitude != null && (
          <Menu.Item component="a" href={`https://www.openstreetmap.org/?mlat=${p.latitude}&mlon=${p.longitude}#map=17/${p.latitude}/${p.longitude}`}
            target="_blank" rel="noreferrer" leftSection={<IconMapPin size={14} />}>Show on a map</Menu.Item>
        )}
        <Menu.Item color="red" onClick={onRemove} leftSection={<IconTrash size={14} />}>Remove this punch</Menu.Item>
      </Menu.Dropdown>
    </Menu>
  )
}

function AddPunchForm({ employeeId, date, done }: { employeeId: string; date: string; done: () => void }) {
  const form = useForm({
    initialValues: { date: date as string | null, time: '', direction: 'In' as ClockDirection, note: '' },
    validate: {
      date: (v) => (v ? null : 'Required'),
      time: (v) => (/^\d{2}:\d{2}$/.test(v) ? null : 'Enter the time, e.g. 08:05'),
      note: (v) => (v.trim() ? null : 'Say why it is entered by hand'),
    },
  })
  const add = useApiMutation((v: { localTime: string; direction: ClockDirection; note: string }) =>
    api.post<Punch>('/attendance/punches', { employeeId, ...v }), { invalidate: ['attendance'], success: 'Punch added.', onSuccess: done })
  return (
    <form onSubmit={form.onSubmit((v) => add.mutate({ localTime: `${v.date}T${v.time}:00`, direction: v.direction, note: v.note.trim() }))}>
      <Stack>
        <Group grow>
          <DateInput label="Date" required valueFormat="ddd D MMM YYYY" {...form.getInputProps('date')} />
          <TimeInput label="Time" required {...form.getInputProps('time')} />
        </Group>
        <Select label="Clocking" allowDeselect={false} data={[{ value: 'In', label: 'In' }, { value: 'Out', label: 'Out' }]} {...form.getInputProps('direction')} />
        <TextInput label="Reason" required placeholder="Fingerprint reader was down" {...form.getInputProps('note')} />
        <Text size="xs" c="dimmed">Times are the company's local time. The punch is marked as entered by you.</Text>
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={add.isPending}>Add</Button></Group>
      </Stack>
    </form>
  )
}
