import { ActionIcon, Badge, Button, Checkbox, Group, Menu, Modal, Select, Stack, Table, Text, Textarea, Tooltip } from '@mantine/core'
import { IconDots } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { EmployeeListItem, OnboardingSetup, OnboardingTask, PagedResult, ProbationOutcome, StartOnboardingRequest, TaskOwner } from '../../api/types'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

export const owners: { value: TaskOwner; label: string }[] = [
  { value: 'Hr', label: 'HR' }, { value: 'It', label: 'IT' }, { value: 'Manager', label: 'Line manager' }, { value: 'NewHire', label: 'New hire' },
]
export const ownerLabel = (o: TaskOwner) => owners.find((x) => x.value === o)!.label
export const ownerColor: Record<TaskOwner, string> = { Hr: 'blue', It: 'grape', Manager: 'teal', NewHire: 'orange' }

export const outcomeLabel: Record<ProbationOutcome, string> = { Confirm: 'Confirm', Extend: 'Extend probation', NotConfirm: 'Do not confirm' }
export const outcomeColor: Record<ProbationOutcome, string> = { Confirm: 'green', Extend: 'yellow', NotConfirm: 'red' }

/** "5 days before start", "on the start date", "14 days after start". */
export const dueText = (days: number) =>
  days === 0 ? 'On the start date' : `${Math.abs(days)} day${Math.abs(days) === 1 ? '' : 's'} ${days < 0 ? 'before' : 'after'} start`

export const useActiveEmployees = () => useQuery({
  queryKey: ['employees', 'managers'],
  queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { status: 'Active', pageSize: 200 }),
})

export const useOnboardingSetup = (enabled = true) =>
  useQuery({ queryKey: ['onboarding', 'templates'], queryFn: () => api.get<OnboardingSetup>('/onboarding/templates'), enabled })

/** Which checklist, and who looks after IT tasks. */
export function StartOnboardingFields({ value, onChange }: { value: StartOnboardingRequest; onChange: (v: StartOnboardingRequest) => void }) {
  const setup = useOnboardingSetup()
  const employees = useActiveEmployees()
  const templates = setup.data?.templates ?? []
  return (
    <Group grow align="flex-start">
      <Select label="Checklist" value={value.templateId ?? ''} allowDeselect={false}
        data={[{ value: '', label: templates.find((t) => t.isDefault)?.name ? `${templates.find((t) => t.isDefault)!.name} (default)` : "TyHR's standard checklist" },
          ...templates.filter((t) => !t.isDefault).map((t) => ({ value: t.id, label: t.name }))]}
        onChange={(v) => onChange({ ...value, templateId: v || null })} />
      <Select label="IT contact" placeholder="HR does IT tasks" clearable searchable value={value.itContactId}
        data={(employees.data?.items ?? []).filter((e) => e.workEmail).map((e) => ({ value: e.id, label: e.fullName }))}
        onChange={(v) => onChange({ ...value, itContactId: v })} />
    </Group>
  )
}

/** A checklist of tasks; owners tick off their own, HR any (and can reassign or remove with <paramref name="manage"/>). */
export function TaskTable({ tasks, showEmployee, manage }: { tasks: OnboardingTask[]; showEmployee?: boolean; manage?: boolean }) {
  const [completing, setCompleting] = useState<OnboardingTask | null>(null)
  const [reassigning, setReassigning] = useState<OnboardingTask | null>(null)
  const [note, setNote] = useState('')
  const [assignee, setAssignee] = useState<string | null>(null)
  const employees = useActiveEmployees()
  const opts = { invalidate: ['onboarding', 'me'] }
  const complete = useApiMutation(() => api.post<OnboardingTask>(`/onboarding/tasks/${completing!.id}/complete`, { note: note.trim() || null }), {
    ...opts, success: 'Done.', onSuccess: () => setCompleting(null),
  })
  const reopen = useApiMutation((t: OnboardingTask) => api.post<OnboardingTask>(`/onboarding/tasks/${t.id}/reopen`), opts)
  const reassign = useApiMutation(() => api.put<OnboardingTask>(`/onboarding/tasks/${reassigning!.id}/assignee`, { assigneeId: assignee }), {
    ...opts, success: 'Reassigned.', onSuccess: () => setReassigning(null),
  })
  const remove = useApiMutation((t: OnboardingTask) => api.delete(`/onboarding/tasks/${t.id}`), { ...opts, success: 'Task removed.' })
  return (
    <>
      <Table.ScrollContainer minWidth={560}>
        <Table verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th w={40} />
              {showEmployee && <Table.Th>For</Table.Th>}
              <Table.Th>Task</Table.Th><Table.Th>Who</Table.Th><Table.Th>Due</Table.Th>{manage && <Table.Th w={40} />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {tasks.map((t) => (
              <Table.Tr key={t.id} opacity={t.isDone ? 0.6 : 1}>
                <Table.Td>
                  <Tooltip label={t.isDone ? `Done by ${t.completedBy}${t.note ? `: ${t.note}` : ''}` : t.canComplete ? 'Mark done' : 'Not yours to tick off'}>
                    <Checkbox checked={t.isDone} disabled={!t.canComplete} readOnly
                      onClick={() => { if (!t.canComplete) return; if (t.isDone) reopen.mutate(t); else { setNote(''); setCompleting(t) } }} />
                  </Tooltip>
                </Table.Td>
                {showEmployee && <Table.Td><Text size="sm">{t.employeeName}</Text><Text size="xs" c="dimmed">starts {date(t.startDate)}</Text></Table.Td>}
                <Table.Td>
                  <Text size="sm" td={t.isDone ? 'line-through' : undefined}>{t.title}</Text>
                  {t.description && <Text size="xs" c="dimmed">{t.description}</Text>}
                  {t.isDone && t.note && <Text size="xs" c="dimmed">Note: {t.note}</Text>}
                </Table.Td>
                <Table.Td>
                  <Badge size="sm" variant="light" color={ownerColor[t.owner]}>{ownerLabel(t.owner)}</Badge>
                  <Text size="xs" c="dimmed">{t.assigneeName ?? 'HR'}</Text>
                </Table.Td>
                <Table.Td><Text size="sm" c={t.isOverdue ? 'red' : undefined}>{date(t.dueOn)}{t.isOverdue && ' (late)'}</Text></Table.Td>
                {manage && (
                  <Table.Td>
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" color="gray" aria-label="Task actions"><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item onClick={() => { setAssignee(t.assigneeId); setReassigning(t) }}>Give to someone else</Menu.Item>
                        <Menu.Item color="red" onClick={() => remove.mutate(t)}>Remove task</Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={!!completing} onClose={() => setCompleting(null)} title={completing?.title}>
        <Stack>
          <Textarea label="Note (optional)" placeholder="e.g. laptop serial number" autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} data-autofocus />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setCompleting(null)}>Cancel</Button>
            <Button loading={complete.isPending} onClick={() => complete.mutate(undefined)}>Mark done</Button>
          </Group>
        </Stack>
      </Modal>
      <Modal opened={!!reassigning} onClose={() => setReassigning(null)} title="Give the task to">
        <Stack>
          <Select placeholder="HR" clearable searchable value={assignee} onChange={setAssignee}
            data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName}${e.workEmail ? '' : ' (no sign-in)'}` }))} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setReassigning(null)}>Cancel</Button>
            <Button loading={reassign.isPending} onClick={() => reassign.mutate(undefined)}>Save</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}
