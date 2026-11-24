import { ActionIcon, Badge, Button, Group, Modal, Select, Stack, Table, Text, Textarea, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { ImprovementPlan, PlanDetails, PlanListItem } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useActiveEmployees } from '../onboarding/components'
import { PlanBadge, ProgressBadge } from './components'

/** Improvement plans for HR (everyone's) and line managers (their team's). */
export function PlansPage() {
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  const q = useQuery({ queryKey: ['plans'], queryFn: () => api.get<PlanListItem[]>('/improvement-plans') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const rows = q.data.filter((p) => !p.isMine)
  return (
    <>
      <PageHeader title="Improvement plans" description="Plans to help someone get back on track: objectives, support, check-ins and an outcome."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>New plan</Button>} />
      {rows.length === 0 ? <Text c="dimmed">No improvement plans.</Text> : (
        <Table.ScrollContainer minWidth={680}>
          <Table highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Owner</Table.Th><Table.Th>Period</Table.Th><Table.Th>Latest check-in</Table.Th><Table.Th>Status</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {rows.map((p) => (
                <Table.Tr key={p.id} style={{ cursor: 'pointer' }} onClick={() => navigate(`/plans/${p.id}`)}>
                  <Table.Td>{p.employeeName}</Table.Td>
                  <Table.Td>{p.ownerName ?? 'HR'}</Table.Td>
                  <Table.Td>{date(p.startDate)} – {date(p.endDate)}</Table.Td>
                  <Table.Td>{p.lastProgress ? <ProgressBadge value={p.lastProgress} /> : '—'}</Table.Td>
                  <Table.Td><Group gap={4}><PlanBadge value={p.status} />{p.status === 'Active' && !p.acknowledged && <Badge variant="outline" color="gray">Not yet read</Badge>}</Group></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Modal opened={creating} onClose={() => setCreating(false)} size="lg" title="New improvement plan">
        {creating && <PlanForm done={(id) => { setCreating(false); if (id) navigate(`/plans/${id}`) }} />}
      </Modal>
    </>
  )
}

/** Creates a plan, or edits one when given. */
export function PlanForm({ plan, done }: { plan?: ImprovementPlan; done: (id?: string) => void }) {
  const employees = useActiveEmployees()
  const today = dayjs().format('YYYY-MM-DD')
  const [employeeId, setEmployeeId] = useState<string | null>(plan?.employeeId ?? null)
  const [ownerId, setOwnerId] = useState<string | null>(plan?.ownerId ?? null)
  const [d, setD] = useState<PlanDetails>(plan
    ? { reason: plan.reason, objectives: plan.objectives, support: plan.support, startDate: plan.startDate, endDate: plan.endDate }
    : { reason: '', objectives: [{ objective: '', measure: null }], support: null, startDate: today, endDate: dayjs().add(90, 'day').format('YYYY-MM-DD') })
  const clean = (): PlanDetails => ({ ...d, reason: d.reason.trim(), support: d.support?.trim() || null,
    objectives: d.objectives.filter((o) => o.objective.trim()).map((o) => ({ objective: o.objective.trim(), measure: o.measure?.trim() || null })) })
  const save = useApiMutation(() => plan
    ? api.put<ImprovementPlan>(`/improvement-plans/${plan.id}`, { details: clean(), ownerId })
    : api.post<ImprovementPlan>('/improvement-plans', { employeeId, ownerId, appraisalId: null, details: clean() }), {
    invalidate: ['plans', 'appraisals'], success: plan ? 'Plan saved.' : 'Plan created. The employee has been emailed.', onSuccess: (p) => done(p.id),
  })
  const setObjective = (i: number, patch: Partial<PlanDetails['objectives'][number]>) =>
    setD({ ...d, objectives: d.objectives.map((o, k) => (k === i ? { ...o, ...patch } : o)) })
  const people = (employees.data?.items ?? []).map((e) => ({ value: e.id, label: e.fullName }))
  return (
    <Stack>
      {!plan && <Select label="Employee" required searchable data={people} value={employeeId} onChange={setEmployeeId} />}
      <Select label="Owner" description="Who runs the check-ins. Leave empty for their line manager." clearable searchable
        data={people.filter((p) => p.value !== (employeeId ?? plan?.employeeId))} value={ownerId} onChange={setOwnerId} />
      <Textarea label="Why a plan is needed" required autosize minRows={3} placeholder="The specific performance concerns, with examples"
        value={d.reason} onChange={(e) => setD({ ...d, reason: e.currentTarget.value })} />
      <Stack gap={6}>
        <Text size="sm" fw={500}>Objectives</Text>
        {d.objectives.map((o, i) => (
          <Group key={i} gap="xs" wrap="nowrap" align="flex-start">
            <Stack gap={4} style={{ flex: 1 }}>
              <TextInput placeholder="What must improve" value={o.objective} onChange={(e) => setObjective(i, { objective: e.currentTarget.value })} />
              <TextInput size="xs" placeholder="How it's measured" value={o.measure ?? ''} onChange={(e) => setObjective(i, { measure: e.currentTarget.value })} />
            </Stack>
            <ActionIcon variant="subtle" color="red" mt={6} aria-label="Remove objective" disabled={d.objectives.length === 1}
              onClick={() => setD({ ...d, objectives: d.objectives.filter((_, k) => k !== i) })}><IconTrash size={16} /></ActionIcon>
          </Group>
        ))}
        <Button variant="subtle" w="fit-content" size="xs" leftSection={<IconPlus size={14} />}
          onClick={() => setD({ ...d, objectives: [...d.objectives, { objective: '', measure: null }] })}>Add objective</Button>
      </Stack>
      <Textarea label="Support offered" autosize minRows={2} placeholder="Coaching, training, resources" value={d.support ?? ''}
        onChange={(e) => setD({ ...d, support: e.currentTarget.value })} />
      <Group grow>
        <DateInput label="Starts" required valueFormat="D MMM YYYY" value={d.startDate} onChange={(v) => setD({ ...d, startDate: v ?? '' })} />
        <DateInput label="Ends" required valueFormat="D MMM YYYY" value={d.endDate} onChange={(v) => setD({ ...d, endDate: v ?? '' })} />
      </Group>
      <Group justify="flex-end">
        <Button variant="default" onClick={() => done()}>Cancel</Button>
        <Button disabled={(!plan && !employeeId) || !d.reason.trim() || !d.objectives.some((o) => o.objective.trim()) || !d.startDate || !d.endDate}
          loading={save.isPending} onClick={() => save.mutate(undefined)}>{plan ? 'Save' : 'Create plan'}</Button>
      </Group>
    </Stack>
  )
}
