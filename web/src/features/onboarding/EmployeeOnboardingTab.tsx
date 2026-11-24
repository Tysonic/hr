import { Alert, Button, Group, Modal, NumberInput, Select, Stack, Text, TextInput } from '@mantine/core'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { ChecklistItem, OnboardingStartResult, OnboardingTask, StartOnboardingRequest } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { owners, StartOnboardingFields, TaskTable, useActiveEmployees } from './components'

/** An employee's onboarding checklist, with starting it and adding tasks for HR. */
export function EmployeeOnboarding({ employeeId }: { employeeId: string }) {
  const manage = useCan('employeesManage')
  const [starting, setStarting] = useState(false)
  const [adding, setAdding] = useState(false)
  const [warnings, setWarnings] = useState<string[]>([])
  const tasks = useQuery({ queryKey: ['onboarding', 'employee', employeeId], queryFn: () => api.get<OnboardingTask[]>(`/employees/${employeeId}/onboarding`) })
  if (tasks.isPending) return <Loading />
  if (tasks.isError) return <LoadError error={tasks.error} />
  const open = tasks.data.some((t) => !t.isDone)
  return (
    <Stack>
      {warnings.length > 0 && <Alert color="yellow" withCloseButton onClose={() => setWarnings([])}>{warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>}
      {manage && (
        <Group>
          {!open && <Button onClick={() => setStarting(true)}>{tasks.data.length ? 'Start another checklist' : 'Start onboarding'}</Button>}
          {tasks.data.length > 0 && <Button variant="default" leftSection={<IconPlus size={14} />} onClick={() => setAdding(true)}>Add a task</Button>}
        </Group>
      )}
      {tasks.data.length === 0 ? <Text c="dimmed" size="sm">No onboarding tasks.</Text> : <TaskTable tasks={tasks.data} manage={manage} />}
      <Modal opened={starting} onClose={() => setStarting(false)} title="Start onboarding" size="lg">
        {starting && <Start employeeId={employeeId} done={(w) => { setStarting(false); setWarnings(w) }} />}
      </Modal>
      <Modal opened={adding} onClose={() => setAdding(false)} title="Add a task">
        {adding && <AddTask employeeId={employeeId} done={() => setAdding(false)} />}
      </Modal>
    </Stack>
  )
}

function Start({ employeeId, done }: { employeeId: string; done: (warnings: string[]) => void }) {
  const [value, setValue] = useState<StartOnboardingRequest>({ templateId: null, itContactId: null })
  const start = useApiMutation(() => api.post<OnboardingStartResult>(`/onboarding/employees/${employeeId}/start`, value), {
    invalidate: ['onboarding', 'me'], success: (r) => `${r.tasks} tasks created. Everyone with a task has been emailed.`, onSuccess: (r) => done(r.warnings),
  })
  return (
    <Stack>
      <Text size="sm">Manager tasks go to their line manager, new-hire tasks to them, and IT tasks to the IT contact. Due dates count from their hire date.</Text>
      <StartOnboardingFields value={value} onChange={setValue} />
      <Group justify="flex-end">
        <Button variant="default" onClick={() => done([])}>Cancel</Button>
        <Button loading={start.isPending} onClick={() => start.mutate(undefined)}>Start</Button>
      </Group>
    </Stack>
  )
}

function AddTask({ employeeId, done }: { employeeId: string; done: () => void }) {
  const employees = useActiveEmployees()
  const [item, setItem] = useState<ChecklistItem>({ title: '', owner: 'Hr', dueDays: 0, description: null })
  const [assignee, setAssignee] = useState<string | null>(null)
  const add = useApiMutation(() => api.post<OnboardingTask>(`/onboarding/employees/${employeeId}/tasks`, { item: { ...item, title: item.title.trim() }, assigneeId: assignee }), {
    invalidate: ['onboarding', 'me'], success: 'Task added.', onSuccess: done,
  })
  return (
    <Stack>
      <TextInput label="Task" required value={item.title} onChange={(e) => setItem({ ...item, title: e.currentTarget.value })} data-autofocus />
      <Group grow>
        <Select label="Kind" data={owners} allowDeselect={false} value={item.owner} onChange={(v) => setItem({ ...item, owner: v as ChecklistItem['owner'] })} />
        <NumberInput label="Days from start" min={-90} max={365} value={item.dueDays} onChange={(v) => setItem({ ...item, dueDays: Number(v) || 0 })} />
      </Group>
      <Select label="Done by" placeholder="HR" clearable searchable value={assignee} onChange={setAssignee}
        data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: e.fullName }))} />
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button disabled={!item.title.trim()} loading={add.isPending} onClick={() => add.mutate(undefined)}>Add</Button>
      </Group>
    </Stack>
  )
}
