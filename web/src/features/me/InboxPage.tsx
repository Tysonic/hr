import { Anchor, Badge, Button, Card, Group, Modal, Stack, Table, Tabs, Text, Textarea } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { ExitListItem, PayrollRun, ProfileChange, Requisition } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { RequestList } from '../leave/ApprovalsPage'
import { useMoney } from '../payroll/components'
import { ChangeTable } from './components'
import { ProbationList } from '../onboarding/ProbationPage'
import { useInbox } from './hooks'

/** Everything waiting for the user's decision: leave, changes employees asked for, and payrolls to approve. */
export function InboxPage() {
  const canLeave = useCan('leaveApprove')
  const canChanges = useCan('profileChangesDecide')
  const canPayroll = useCan('payrollApprove')
  const canRequisitions = useCan('recruitmentApprove')
  const canProbation = useCan('employeesView')
  const canExits = useCan('exitsManage')
  const inbox = useInbox()
  const count = (n: number | undefined) => (n ? <Badge size="sm" circle>{n}</Badge> : null)
  const first = canLeave ? 'leave' : canChanges ? 'changes' : canPayroll ? 'payroll' : canRequisitions ? 'requisitions' : 'probation'
  return (
    <>
      <PageHeader title="Approvals" description="What's waiting for your decision. Nobody decides their own requests." />
      <Tabs defaultValue={first} keepMounted={false}>
        <Tabs.List mb="md">
          {canLeave && <Tabs.Tab value="leave" rightSection={count(inbox.data?.leaveRequests)}>Leave</Tabs.Tab>}
          {canChanges && <Tabs.Tab value="changes" rightSection={count(inbox.data?.profileChanges)}>Profile changes</Tabs.Tab>}
          {canPayroll && <Tabs.Tab value="payroll" rightSection={count(inbox.data?.payrollApprovals)}>Payroll</Tabs.Tab>}
          {canRequisitions && <Tabs.Tab value="requisitions" rightSection={count(inbox.data?.requisitions)}>Requisitions</Tabs.Tab>}
          {canProbation && <Tabs.Tab value="probation" rightSection={count(inbox.data?.probations)}>Probation</Tabs.Tab>}
          {canExits && <Tabs.Tab value="resignations" rightSection={count(inbox.data?.resignations)}>Resignations</Tabs.Tab>}
        </Tabs.List>
        {canLeave && <Tabs.Panel value="leave"><RequestList scope="approvals" /></Tabs.Panel>}
        {canChanges && <Tabs.Panel value="changes"><ProfileChanges /></Tabs.Panel>}
        {canPayroll && <Tabs.Panel value="payroll"><Payrolls /></Tabs.Panel>}
        {canRequisitions && <Tabs.Panel value="requisitions"><Requisitions /></Tabs.Panel>}
        {canProbation && <Tabs.Panel value="probation"><ProbationList /></Tabs.Panel>}
        {canExits && <Tabs.Panel value="resignations"><Resignations /></Tabs.Panel>}
      </Tabs>
    </>
  )
}

function ProfileChanges() {
  const [declining, setDeclining] = useState<ProfileChange | null>(null)
  const [note, setNote] = useState('')
  const list = useQuery({ queryKey: ['profile-changes', 'pending'], queryFn: () => api.get<ProfileChange[]>('/profile-changes') })
  const approve = useApiMutation((c: ProfileChange) => api.post<ProfileChange>(`/profile-changes/${c.id}/approve`, { note: null }), {
    invalidate: ['profile-changes', 'inbox', 'employees'], success: (c) => `${c.employeeName}'s ${c.what.toLowerCase()} updated.`,
  })
  const reject = useApiMutation((c: ProfileChange) => api.post<ProfileChange>(`/profile-changes/${c.id}/reject`, { note }), {
    invalidate: ['profile-changes', 'inbox'], success: 'Declined; the employee has been told why.', onSuccess: () => setDeclining(null),
  })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  if (list.data.length === 0) return <Text c="dimmed" ta="center" py="xl">No changes waiting.</Text>
  return (
    <Stack maw={900}>
      {list.data.map((c) => (
        <Card key={c.id} withBorder>
          <Group justify="space-between" mb="xs" wrap="wrap">
            <Stack gap={0}>
              <Text fw={600}>
                <Anchor component={Link} to={`/employees/${c.employeeId}`}>{c.employeeName}</Anchor> · {c.what}
              </Text>
              <Text size="xs" c="dimmed">{c.employeeNumber} · asked {dateTime(c.requestedAt)}</Text>
            </Stack>
            {c.canDecide && (
              <Group gap="xs">
                <Button size="xs" variant="default" onClick={() => { setNote(''); setDeclining(c) }}>Decline</Button>
                <Button size="xs" color="green" loading={approve.isPending && approve.variables?.id === c.id} onClick={() => approve.mutate(c)}>Approve</Button>
              </Group>
            )}
          </Group>
          {c.reason && <Text size="sm" mb="xs"><b>Reason:</b> {c.reason}</Text>}
          <ChangeTable changes={c.changes} showCurrent />
          {c.kind === 'Payment' && <Text size="xs" c="dimmed" mt="xs">Check the account is in the employee's name before approving. They're emailed at every address on file when it changes.</Text>}
        </Card>
      ))}
      <Modal opened={!!declining} onClose={() => setDeclining(null)} title={`Decline ${declining?.employeeName}'s change`}>
        <Textarea label="Why" description="The employee sees this." required autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} data-autofocus />
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setDeclining(null)}>Cancel</Button>
          <Button color="red" disabled={!note.trim()} loading={reject.isPending} onClick={() => declining && reject.mutate(declining)}>Decline</Button>
        </Group>
      </Modal>
    </Stack>
  )
}

function Payrolls() {
  const m = useMoney()
  const runs = useQuery({ queryKey: ['payroll', 'runs'], queryFn: () => api.get<PayrollRun[]>('/payroll/runs') })
  if (runs.isPending) return <Loading />
  if (runs.isError) return <LoadError error={runs.error} />
  const waiting = runs.data.filter((r) => r.status === 'Submitted')
  if (waiting.length === 0) return <Text c="dimmed" ta="center" py="xl">No payrolls waiting for approval.</Text>
  return (
    <Table.ScrollContainer minWidth={560}>
      <Table maw={900} highlightOnHover>
        <Table.Thead><Table.Tr><Table.Th>Payroll</Table.Th><Table.Th>Submitted</Table.Th><Table.Th ta="right">People</Table.Th><Table.Th ta="right">Net</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>
          {waiting.map((r) => (
            <Table.Tr key={r.id}>
              <Table.Td>
                <Anchor component={Link} to={`/payroll/runs/${r.id}`} fw={500}>{r.name}</Anchor>
                {!r.canApprove && <Text size="xs" c="dimmed">You prepared it, so someone else approves it.</Text>}
              </Table.Td>
              <Table.Td>{r.submittedBy} · {dateTime(r.submittedAt)}</Table.Td>
              <Table.Td ta="right">{r.employeeCount}</Table.Td>
              <Table.Td ta="right">{m(r.net)}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}

function Requisitions() {
  const list = useQuery({ queryKey: ['recruitment', 'requisitions', 'Pending'], queryFn: () => api.get<Requisition[]>('/recruitment/requisitions', { status: 'Pending' }) })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  const waiting = list.data.filter((r) => r.canDecide)
  if (waiting.length === 0) return <Text c="dimmed" ta="center" py="xl">No requisitions waiting for approval.</Text>
  return (
    <Stack maw={900}>
      {waiting.map((r) => (
        <Card key={r.id} withBorder padding="sm">
          <Group justify="space-between">
            <Stack gap={0}>
              <Text fw={500}>{r.number} · {r.title}{r.openings > 1 && ` (${r.openings})`}</Text>
              <Text size="xs" c="dimmed">{r.requestedBy} · {dateTime(r.requestedAt)}{r.departmentName && ` · ${r.departmentName}`}</Text>
            </Stack>
            <Button size="xs" component={Link} to="/recruitment/requisitions">Review</Button>
          </Group>
        </Card>
      ))}
    </Stack>
  )
}

function Resignations() {
  const q = useQuery({ queryKey: ['exits', 'open'], queryFn: () => api.get<ExitListItem[]>('/exits') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const waiting = q.data.filter((x) => x.status === 'Requested')
  if (waiting.length === 0) return <Text c="dimmed">No resignations waiting.</Text>
  return (
    <Stack gap="xs">
      {waiting.map((x) => (
        <Card key={x.id} withBorder padding="sm">
          <Group justify="space-between">
            <Stack gap={0}>
              <Anchor component={Link} to={`/exits/${x.id}`} fw={500}>{x.employeeName}</Anchor>
              <Text size="xs" c="dimmed">Handed in {date(x.noticeGivenOn)} · proposed last day {date(x.lastWorkingDay)}</Text>
            </Stack>
            <Button size="xs" component={Link} to={`/exits/${x.id}`}>Review</Button>
          </Group>
        </Card>
      ))}
    </Stack>
  )
}
