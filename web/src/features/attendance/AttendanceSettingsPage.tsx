import { ActionIcon, Alert, Badge, Button, Checkbox, Group, Menu, Modal, MultiSelect, NumberInput, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput, TimeInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconDots, IconPlus, IconUsersPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { DayOfWeek, EmployeeListItem, PagedResult, SaveSchedule, Schedule } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'
import { clockTime, hm, useSchedules } from './components'

const week: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const days = (d: DayOfWeek[]) => week.filter((w) => d.includes(w)).map((w) => w.slice(0, 3)).join(' ')

/** Work schedules (shifts) and who works which. */
export function AttendanceSettingsPage() {
  const schedules = useSchedules()
  const [editing, setEditing] = useState<Schedule | 'new' | null>(null)
  const [assigning, setAssigning] = useState<Schedule | null>(null)
  const act = useApiMutation(({ s, action }: { s: Schedule; action: 'default' | 'activate' | 'deactivate' }) =>
    api.post<Schedule>(`/attendance/schedules/${s.id}/${action}`), { invalidate: ['attendance'], success: 'Schedule updated.' })
  if (schedules.isPending) return <Loading />
  if (schedules.isError) return <LoadError error={schedules.error} />
  return (
    <>
      <PageHeader title="Attendance settings" description="Work schedules decide who's late, absent or working overtime."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>New schedule</Button>} />
      <Stack maw={1100}>
        <Alert color="blue">Everyone works the default schedule unless they're put on another one from a date. Public holidays come from Settings → Leave.</Alert>
        <Table.ScrollContainer minWidth={800}>
          <Table striped>
            <Table.Thead>
              <Table.Tr><Table.Th>Schedule</Table.Th><Table.Th>Hours</Table.Th><Table.Th>Days</Table.Th><Table.Th>Rules</Table.Th><Table.Th ta="right">People</Table.Th><Table.Th /></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {schedules.data.map((s) => (
                <Table.Tr key={s.id} opacity={s.isActive ? 1 : 0.6}>
                  <Table.Td>
                    <Group gap={6}><Text fw={500} size="sm">{s.name}</Text>{s.isDefault && <Badge size="sm">Default</Badge>}{!s.isActive && <Badge size="sm" color="gray">Not in use</Badge>}</Group>
                  </Table.Td>
                  <Table.Td><Text size="sm">{clockTime(s.startTime)}–{clockTime(s.endTime)}{s.overnight && ' (overnight)'}</Text>
                    <Text size="xs" c="dimmed">{hm(s.scheduledMinutes)} a day{s.breakMinutes ? `, ${s.breakMinutes} min break` : ''}</Text></Table.Td>
                  <Table.Td><Text size="sm">{days(s.workingDays)}</Text></Table.Td>
                  <Table.Td><Text size="xs">{s.graceMinutes} min grace · overtime from {s.overtimeMinimumMinutes} min</Text></Table.Td>
                  <Table.Td ta="right">{s.employeeCount}</Table.Td>
                  <Table.Td w={40}>
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${s.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item onClick={() => setEditing(s)}>Edit</Menu.Item>
                        {s.isActive && <Menu.Item leftSection={<IconUsersPlus size={14} />} onClick={() => setAssigning(s)}>Put people on it</Menu.Item>}
                        {s.isActive && !s.isDefault && <Menu.Item onClick={() => act.mutate({ s, action: 'default' })}>Make default</Menu.Item>}
                        {!s.isDefault && <Menu.Item onClick={() => act.mutate({ s, action: s.isActive ? 'deactivate' : 'activate' })}>{s.isActive ? 'Stop using' : 'Use again'}</Menu.Item>}
                      </Menu.Dropdown>
                    </Menu>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Stack>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'New schedule' : 'Edit schedule'} size="lg">
        {editing !== null && <ScheduleForm schedule={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
      <Modal opened={assigning !== null} onClose={() => setAssigning(null)} title={`Put people on ${assigning?.name}`}>
        {assigning && <AssignForm schedule={assigning} done={() => setAssigning(null)} />}
      </Modal>
    </>
  )
}

function ScheduleForm({ schedule, done }: { schedule: Schedule | null; done: () => void }) {
  const form = useForm({
    initialValues: {
      name: schedule?.name ?? '', startTime: clockTime(schedule?.startTime ?? '08:00'), endTime: clockTime(schedule?.endTime ?? '17:00'),
      breakMinutes: schedule?.breakMinutes ?? 60, workingDays: schedule?.workingDays ?? week.slice(0, 5),
      graceMinutes: schedule?.graceMinutes ?? 15, overtimeMinimumMinutes: schedule?.overtimeMinimumMinutes ?? 30,
    },
    validate: {
      name: (v) => (v.trim() ? null : 'Required'),
      startTime: (v) => (/^\d{2}:\d{2}$/.test(v) ? null : 'Required'),
      endTime: (v) => (/^\d{2}:\d{2}$/.test(v) ? null : 'Required'),
      workingDays: (v) => (v.length ? null : 'Choose at least one day'),
    },
  })
  const save = useApiMutation((v: SaveSchedule) => (schedule ? api.put<Schedule>(`/attendance/schedules/${schedule.id}`, v) : api.post<Schedule>('/attendance/schedules', v)), {
    invalidate: ['attendance'], success: (s) => `${s.name} saved.`, onSuccess: done,
  })
  const v = form.getValues()
  const overnight = v.endTime && v.startTime && v.endTime <= v.startTime
  return (
    <form onSubmit={form.onSubmit((x) => save.mutate({ ...x, name: x.name.trim(), startTime: `${x.startTime}:00`, endTime: `${x.endTime}:00`, workingDays: x.workingDays as DayOfWeek[] }))}>
      <Stack>
        <TextInput label="Name" required placeholder="Day shift" {...form.getInputProps('name')} />
        <Group grow>
          <TimeInput label="Starts" required {...form.getInputProps('startTime')} />
          <TimeInput label="Ends" required description={overnight ? 'Next morning (overnight shift)' : undefined} {...form.getInputProps('endTime')} />
          <NumberInput label="Unpaid break (minutes)" min={0} max={240} {...form.getInputProps('breakMinutes')} />
        </Group>
        <Checkbox.Group label="Working days" {...form.getInputProps('workingDays')}>
          <Group mt="xs">{week.map((d) => <Checkbox key={d} value={d} label={d.slice(0, 3)} />)}</Group>
        </Checkbox.Group>
        <Group grow>
          <NumberInput label="Grace period (minutes)" description="Arriving within it isn't late" min={0} max={120} {...form.getInputProps('graceMinutes')} />
          <NumberInput label="Overtime counts from (minutes)" description="Smaller extra time is ignored" min={0} max={240} {...form.getInputProps('overtimeMinimumMinutes')} />
        </Group>
        {schedule && <Alert color="yellow">Changes apply to past days too when timesheets are looked at again, so check before changing a schedule mid-month.</Alert>}
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={save.isPending}>Save</Button></Group>
      </Stack>
    </form>
  )
}

function AssignForm({ schedule, done }: { schedule: Schedule; done: () => void }) {
  const employees = useQuery({
    queryKey: ['employees', 'all-active'],
    queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { status: 'Active', pageSize: 200 }),
  })
  const form = useForm({
    initialValues: { employeeIds: [] as string[], effectiveFrom: dayjs().format('YYYY-MM-DD') as string | null },
    validate: { employeeIds: (v) => (v.length ? null : 'Choose who'), effectiveFrom: (v) => (v ? null : 'Required') },
  })
  const assign = useApiMutation((x: { employeeIds: string[]; effectiveFrom: string }) =>
    api.post<{ assigned: number }>('/attendance/assignments', { scheduleId: schedule.id, ...x }), {
    invalidate: ['attendance'], success: (r) => `${r.assigned} ${r.assigned === 1 ? 'person' : 'people'} put on ${schedule.name}.`, onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((x) => assign.mutate({ employeeIds: x.employeeIds, effectiveFrom: x.effectiveFrom! }))}>
      <Stack>
        <MultiSelect label="People" required searchable placeholder={employees.isPending ? 'Loading…' : 'Search by name'}
          data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName} (${e.employeeNumber})` }))} {...form.getInputProps('employeeIds')} />
        <DateInput label="From" required valueFormat="ddd D MMM YYYY" {...form.getInputProps('effectiveFrom')} />
        <Text size="xs" c="dimmed">Their earlier days keep the schedule they had then.</Text>
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={assign.isPending}>Save</Button></Group>
      </Stack>
    </form>
  )
}
