import { Button, Card, Group, Modal, Select, SimpleGrid, Stack, Text, TextInput, Title } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { Employee, EmployeeAttendance } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { TimesheetView, useMonth, useSchedules } from './components'

/** An employee's schedule, device user number and timesheet; HR changes the first two and corrects punches. */
export function EmployeeAttendanceTab({ employee }: { employee: Employee }) {
  const canManage = useCan('attendanceManage')
  const month = useMonth()
  const [dialog, setDialog] = useState<'schedule' | 'device' | null>(null)
  const setup = useQuery({
    queryKey: ['attendance', 'employee', employee.id],
    queryFn: () => api.get<EmployeeAttendance>(`/attendance/employees/${employee.id}`),
  })
  if (setup.isPending) return <Loading />
  if (setup.isError) return <LoadError error={setup.error} />
  const s = setup.data
  const close = () => setDialog(null)

  return (
    <Stack maw={1100}>
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        <Card withBorder>
          <Group justify="space-between" align="flex-start">
            <div>
              <Text size="xs" c="dimmed">Schedule</Text>
              <Text fw={600}>{s.currentScheduleName ?? 'None'}{s.onDefaultSchedule && s.currentScheduleName ? ' (company default)' : ''}</Text>
              {s.assignments.slice(0, 3).map((a) => <Text key={a.id} size="xs" c="dimmed">{a.scheduleName} from {date(a.effectiveFrom)}</Text>)}
            </div>
            {canManage && <Button size="xs" variant="default" onClick={() => setDialog('schedule')}>Change</Button>}
          </Group>
        </Card>
        <Card withBorder>
          <Group justify="space-between" align="flex-start">
            <div>
              <Text size="xs" c="dimmed">User number on attendance devices</Text>
              <Text fw={600}>{s.deviceUserId ?? 'Not set'}</Text>
              <Text size="xs" c="dimmed">Imported punches also match the employee number, {s.employeeNumber}.</Text>
            </div>
            {canManage && <Button size="xs" variant="default" onClick={() => setDialog('device')}>{s.deviceUserId ? 'Change' : 'Set'}</Button>}
          </Group>
        </Card>
      </SimpleGrid>
      <Group justify="space-between"><Title order={5}>Timesheet</Title>{month.control}</Group>
      <TimesheetView employeeId={employee.id} from={month.from} to={month.to} />
      <Modal opened={dialog === 'schedule'} onClose={close} title={`Change ${employee.fullName}'s schedule`}>
        {dialog === 'schedule' && <ScheduleForm employeeId={employee.id} current={s.currentScheduleId} done={close} />}
      </Modal>
      <Modal opened={dialog === 'device'} onClose={close} title="User number on attendance devices">
        {dialog === 'device' && <DeviceForm employeeId={employee.id} current={s.deviceUserId} done={close} />}
      </Modal>
    </Stack>
  )
}

function ScheduleForm({ employeeId, current, done }: { employeeId: string; current: string | null; done: () => void }) {
  const schedules = useSchedules()
  const form = useForm({
    initialValues: { scheduleId: current, effectiveFrom: dayjs().format('YYYY-MM-DD') as string | null },
    validate: { scheduleId: (v) => (v ? null : 'Required'), effectiveFrom: (v) => (v ? null : 'Required') },
  })
  const save = useApiMutation((x: { scheduleId: string; effectiveFrom: string }) => api.post('/attendance/assignments', { employeeIds: [employeeId], ...x }), {
    invalidate: ['attendance'], success: 'Schedule changed.', onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((x) => save.mutate({ scheduleId: x.scheduleId!, effectiveFrom: x.effectiveFrom! }))}>
      <Stack>
        <Select label="Schedule" required allowDeselect={false}
          data={(schedules.data ?? []).filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name }))} {...form.getInputProps('scheduleId')} />
        <DateInput label="From" required valueFormat="ddd D MMM YYYY" {...form.getInputProps('effectiveFrom')} />
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={save.isPending}>Save</Button></Group>
      </Stack>
    </form>
  )
}

function DeviceForm({ employeeId, current, done }: { employeeId: string; current: string | null; done: () => void }) {
  const [value, setValue] = useState(current ?? '')
  const save = useApiMutation(() => api.put<EmployeeAttendance>(`/attendance/employees/${employeeId}/device-user`, { deviceUserId: value.trim() || null }), {
    invalidate: ['attendance'], success: 'Device user number saved.', onSuccess: done,
  })
  return (
    <Stack>
      <TextInput label="User number" description="As enrolled on the fingerprint or face device. Leading zeros don't matter. Empty to clear."
        value={value} onChange={(e) => setValue(e.currentTarget.value)} maxLength={50} />
      <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button></Group>
    </Stack>
  )
}
