import { Button, Card, Drawer, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Table, Text, Textarea, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { Posting, PostingDetails, Requisition, RequisitionDetails, RequisitionStatus } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { contractTypes, label } from '../employees/fields'
import { options, useBranches, useDepartments, usePositions } from '../org/hooks'
import { useMoney } from '../payroll/components'
import { RequisitionBadge, requisitionLabel } from './components'
import { PostingForm } from './PostingsPage'

type Action = { requisition: Requisition; kind: 'approve' | 'reject' | 'close' }

/** Requests to hire: managers raise them, HR admins approve, HR advertises the approved ones. */
export function RequisitionsPage() {
  const [status, setStatus] = useState<RequisitionStatus | null>(null)
  const [editing, setEditing] = useState<Requisition | 'new' | null>(null)
  const [open, setOpen] = useState<string | null>(null)
  const m = useMoney()
  const list = useQuery({ queryKey: ['recruitment', 'requisitions', status], queryFn: () => api.get<Requisition[]>('/recruitment/requisitions', { status }) })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  const selected = list.data.find((r) => r.id === open) ?? null
  return (
    <>
      <PageHeader title="Requisitions" description="Requests to hire. An HR admin other than the person who raised it approves; then HR can advertise the job."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>New requisition</Button>} />
      <Group mb="md">
        <Select placeholder="Any status" clearable w={220} value={status} onChange={(v) => setStatus(v as RequisitionStatus | null)}
          data={(Object.keys(requisitionLabel) as RequisitionStatus[]).map((s) => ({ value: s, label: requisitionLabel[s] }))} />
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr><Table.Th>Number</Table.Th><Table.Th>Job</Table.Th><Table.Th>Openings</Table.Th><Table.Th>Salary</Table.Th><Table.Th>Raised</Table.Th><Table.Th>Status</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {list.data.length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="lg">No requisitions.</Text></Table.Td></Table.Tr>}
            {list.data.map((r) => (
              <Table.Tr key={r.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(r.id)}>
                <Table.Td>{r.number}</Table.Td>
                <Table.Td><Text size="sm" fw={500}>{r.title}</Text><Text size="xs" c="dimmed">{[r.departmentName, r.branchName].filter(Boolean).join(' · ')}</Text></Table.Td>
                <Table.Td>{r.hired > 0 ? `${r.hired} of ${r.openings} hired` : r.openings}</Table.Td>
                <Table.Td>{salary(r, m)}</Table.Td>
                <Table.Td><Text size="sm">{r.requestedBy}</Text><Text size="xs" c="dimmed">{date(r.requestedAt.slice(0, 10))}</Text></Table.Td>
                <Table.Td><RequisitionBadge status={r.status} /></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Drawer opened={!!selected} onClose={() => setOpen(null)} position="right" size="lg" title={selected ? `${selected.number} · ${selected.title}` : ''}>
        {selected && <RequisitionView r={selected} onEdit={() => setEditing(selected)} />}
      </Drawer>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} size="lg" title={editing === 'new' ? 'New requisition' : 'Edit requisition'}>
        {editing && <RequisitionForm requisition={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </>
  )
}

const salary = (r: Requisition, m: (n: number) => string) =>
  r.salaryMin && r.salaryMax ? `${m(r.salaryMin)} – ${m(r.salaryMax)}` : r.salaryMin ? `from ${m(r.salaryMin)}` : r.salaryMax ? `up to ${m(r.salaryMax)}` : '—'

function RequisitionView({ r, onEdit }: { r: Requisition; onEdit: () => void }) {
  const m = useMoney()
  const navigate = useNavigate()
  const canAdvertise = useCan('recruitmentManage') && r.status === 'Approved'
  const [action, setAction] = useState<Action | null>(null)
  const [advertising, setAdvertising] = useState(false)
  const [note, setNote] = useState('')
  const decide = useApiMutation((a: Action) => api.post<Requisition>(`/recruitment/requisitions/${a.requisition.id}/${a.kind}`, { note: note.trim() || null }), {
    invalidate: ['recruitment', 'inbox'], success: (x) => `${x.number}: ${requisitionLabel[x.status].toLowerCase()}.`, onSuccess: () => setAction(null),
  })
  const advertise = useApiMutation((d: PostingDetails) => api.post<Posting>('/recruitment/postings', { requisitionId: r.id, details: d }), {
    invalidate: ['recruitment'], success: 'Draft posting created. Publish it to show it on the careers page.',
    onSuccess: (p) => navigate(`/recruitment/postings/${p.id}`),
  })
  return (
    <Stack>
      <Group justify="space-between"><RequisitionBadge status={r.status} />
        <Group gap="xs">
          {r.canEdit && <Button size="xs" variant="default" onClick={onEdit}>Edit</Button>}
          {r.canClose && <Button size="xs" variant="default" onClick={() => { setNote(''); setAction({ requisition: r, kind: 'close' }) }}>Close</Button>}
          {r.canDecide && <Button size="xs" variant="default" color="red" onClick={() => { setNote(''); setAction({ requisition: r, kind: 'reject' }) }}>Decline</Button>}
          {r.canDecide && <Button size="xs" color="green" onClick={() => { setNote(''); setAction({ requisition: r, kind: 'approve' }) }}>Approve</Button>}
          {canAdvertise && <Button size="xs" onClick={() => setAdvertising(true)}>Advertise</Button>}
        </Group>
      </Group>
      <Card withBorder>
        <SimpleGrid cols={2} spacing="sm" verticalSpacing="xs">
          <Field label="Department" value={r.departmentName} />
          <Field label="Position" value={r.positionTitle} />
          <Field label="Branch" value={r.branchName} />
          <Field label="Type" value={label(contractTypes, r.employmentType)} />
          <Field label="Openings" value={r.openings} />
          <Field label="Monthly salary" value={salary(r, m)} />
          <Field label="Wanted from" value={date(r.targetStartDate)} />
          <Field label="Postings" value={r.postings} />
        </SimpleGrid>
        <Text size="xs" c="dimmed" mt="sm">Reason</Text>
        <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{r.justification}</Text>
      </Card>
      <Text size="xs" c="dimmed">
        Raised by {r.requestedBy} {dateTime(r.requestedAt)}
        {r.decidedBy && ` · ${r.status === 'Closed' ? 'closed' : requisitionLabel[r.status].toLowerCase()} by ${r.decidedBy} ${dateTime(r.decidedAt)}`}
      </Text>
      {r.decisionNote && <Text size="sm"><b>Note:</b> {r.decisionNote}</Text>}

      <Modal opened={!!action} onClose={() => setAction(null)} title={action?.kind === 'approve' ? 'Approve requisition' : action?.kind === 'reject' ? 'Decline requisition' : 'Close requisition'}>
        <Textarea label={action?.kind === 'reject' ? 'Why' : 'Note (optional)'} required={action?.kind === 'reject'} autosize minRows={2} value={note}
          onChange={(e) => setNote(e.currentTarget.value)} data-autofocus
          description={action?.kind === 'close' ? 'Closing also closes its job postings to new applications.' : undefined} />
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setAction(null)}>Cancel</Button>
          <Button color={action?.kind === 'approve' ? 'green' : 'red'} disabled={action?.kind === 'reject' && !note.trim()} loading={decide.isPending}
            onClick={() => action && decide.mutate(action)}>
            {action?.kind === 'approve' ? 'Approve' : action?.kind === 'reject' ? 'Decline' : 'Close'}
          </Button>
        </Group>
      </Modal>
      <Modal opened={advertising} onClose={() => setAdvertising(false)} size="xl" title={`Advertise ${r.title}`}>
        <PostingForm initial={{ title: r.title, description: '', location: r.branchName, closingDate: null, showSalary: false }}
          loading={advertise.isPending} submit={(d) => advertise.mutate(d)} done={() => setAdvertising(false)} />
      </Modal>
    </Stack>
  )
}

function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Stack gap={0}>
      <Text size="xs" c="dimmed">{label}</Text>
      <Text size="sm">{value === null || value === undefined || value === '' ? '—' : value}</Text>
    </Stack>
  )
}

function RequisitionForm({ requisition: r, done }: { requisition: Requisition | null; done: () => void }) {
  const departments = useDepartments()
  const positions = usePositions()
  const branches = useBranches()
  const form = useForm<RequisitionDetails>({
    initialValues: r ? {
      title: r.title, departmentId: r.departmentId, positionId: r.positionId, branchId: r.branchId, openings: r.openings, employmentType: r.employmentType,
      salaryMin: r.salaryMin, salaryMax: r.salaryMax, targetStartDate: r.targetStartDate, justification: r.justification,
    } : {
      title: '', departmentId: null, positionId: null, branchId: null, openings: 1, employmentType: 'Permanent', salaryMin: null, salaryMax: null,
      targetStartDate: null, justification: '',
    },
    validate: { title: (v) => (v.trim() ? null : 'Required'), justification: (v) => (v.trim() ? null : 'Required') },
  })
  const save = useApiMutation((d: RequisitionDetails) => r ? api.put<Requisition>(`/recruitment/requisitions/${r.id}`, d) : api.post<Requisition>('/recruitment/requisitions', d), {
    invalidate: ['recruitment', 'inbox'], success: (x) => r ? 'Saved.' : `${x.number} sent for approval.`, onSuccess: done,
  })
  const v = form.getValues()
  const num = (n: number | string | null) => (n === '' || n === null ? null : Number(n))
  return (
    <form onSubmit={form.onSubmit((d) => save.mutate({
      ...d, title: d.title.trim(), justification: d.justification.trim(), salaryMin: num(d.salaryMin), salaryMax: num(d.salaryMax), targetStartDate: d.targetStartDate || null,
    }))}>
      <Stack>
        <TextInput label="Job title" required data-autofocus {...form.getInputProps('title')} />
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <Select label="Department" clearable searchable data={options(departments.data, (d) => d.name, v.departmentId)} {...form.getInputProps('departmentId')} />
          <Select label="Position" clearable searchable
            data={options(positions.data?.filter((p) => !v.departmentId || p.departmentId === v.departmentId), (p) => p.title, v.positionId)}
            {...form.getInputProps('positionId')} />
          <Select label="Branch" clearable data={options(branches.data, (b) => b.name, v.branchId)} {...form.getInputProps('branchId')} />
          <Select label="Type" data={contractTypes} allowDeselect={false} {...form.getInputProps('employmentType')} />
          <NumberInput label="Openings" min={1} max={500} {...form.getInputProps('openings')} />
          <DateInput label="Wanted from" valueFormat="D MMM YYYY" clearable {...form.getInputProps('targetStartDate')} />
          <NumberInput label="Monthly salary from" thousandSeparator="," min={0} {...form.getInputProps('salaryMin')} />
          <NumberInput label="Monthly salary to" thousandSeparator="," min={0} {...form.getInputProps('salaryMax')} />
        </SimpleGrid>
        <Textarea label="Reason for hiring" required autosize minRows={2} placeholder="New role, replacement for…, workload" {...form.getInputProps('justification')} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>{r ? 'Save' : 'Send for approval'}</Button>
        </Group>
      </Stack>
    </form>
  )
}
