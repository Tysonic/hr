import {
  ActionIcon, Alert, Badge, Button, Card, Checkbox, FileButton, Group, List, Menu, Modal, Progress, Select, SimpleGrid, Stack, Table, Text,
  Textarea, Tooltip,
} from '@mantine/core'
import { DatePickerInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { useDebouncedValue } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconAlertTriangle, IconDots, IconPaperclip } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, download } from '../../api/client'
import type { CreateLeaveRequest, LeavePreview, LeaveRequest, LeaveStatus, LeaveType, LeaveTypeBalance } from '../../api/types'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

export const leaveStatusColor: Record<LeaveStatus, string> = { Pending: 'yellow', Approved: 'green', Rejected: 'red', Cancelled: 'gray' }

export const days = (n: number) => `${Number.isInteger(n) ? n : n.toFixed(1)} day${n === 1 ? '' : 's'}`

export function dateRange(start: string, end: string, halfDay = false) {
  if (start === end) return `${date(start)}${halfDay ? ' (half day)' : ''}`
  return `${date(start)} – ${date(end)}`
}

export const useLeaveTypes = () => useQuery({ queryKey: ['leave', 'types'], queryFn: () => api.get<LeaveType[]>('/leave/types') })

/** Balances as cards: what's available, and how it is made up. Types without a balance show the days used. */
export function BalanceCards({ balances }: { balances: LeaveTypeBalance[] }) {
  return (
    <SimpleGrid cols={{ base: 1, xs: 2, md: 4 }}>
      {balances.map((b) => (
        <Card key={b.leaveTypeId} withBorder padding="sm">
          <Text size="sm" c="dimmed">{b.name}{!b.isPaid && ' (unpaid)'}</Text>
          {b.balance ? (
            <>
              <Text fz={26} fw={700} c={b.balance.available < 0 ? 'red' : undefined}>{days(b.balance.available)}</Text>
              <Text size="xs" c="dimmed">available now</Text>
              <Progress mt="xs" size="sm"
                value={Math.min(100, ((b.balance.taken + b.balance.pending) / Math.max(1, b.balance.fullYear + b.balance.carriedForward + b.balance.adjusted)) * 100)} />
              <Text size="xs" c="dimmed" mt={4}>
                {b.balance.earned} earned of {b.balance.fullYear} this year
                {b.balance.carriedForward > 0 && ` · ${b.balance.carriedForward} carried`}
                {b.balance.adjusted !== 0 && ` · ${b.balance.adjusted > 0 ? '+' : ''}${b.balance.adjusted} adjusted`}
                {` · ${b.balance.taken} taken`}
                {b.balance.pending > 0 && ` · ${b.balance.pending} pending`}
              </Text>
            </>
          ) : (
            <>
              <Text fz={26} fw={700}>{days(b.usedThisYear)}</Text>
              <Text size="xs" c="dimmed">taken this year (no balance kept)</Text>
            </>
          )}
          {b.eligibleFrom && new Date(b.eligibleFrom) > new Date() && <Text size="xs" c="orange" mt={4}>Available from {date(b.eligibleFrom)}</Text>}
        </Card>
      ))}
    </SimpleGrid>
  )
}

type Decision = { request: LeaveRequest; action: 'approve' | 'reject' | 'cancel' }

/** Requests with their actions: decide (approvers), cancel, attach and download supporting documents. */
export function RequestsTable({ requests, showEmployee = false, empty = 'No leave requests.' }:
  { requests: LeaveRequest[]; showEmployee?: boolean; empty?: string }) {
  const [deciding, setDeciding] = useState<Decision | null>(null)
  const attach = useApiMutation(({ r, file }: { r: LeaveRequest; file: File }) => {
    const body = new FormData()
    body.set('file', file)
    return api.upload<LeaveRequest>(`/leave/requests/${r.id}/document`, body)
  }, { invalidate: ['leave', 'documents'], success: 'Document attached.' })
  const getDocument = (r: LeaveRequest) => download(`/leave/requests/${r.id}/document`, `${r.leaveTypeName} ${r.startDate}`)
    .catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))

  return (
    <>
      <Table.ScrollContainer minWidth={720}>
        <Table striped>
          <Table.Thead>
            <Table.Tr>
              {showEmployee && <Table.Th>Employee</Table.Th>}
              <Table.Th>Leave</Table.Th><Table.Th>Dates</Table.Th><Table.Th ta="right">Days</Table.Th><Table.Th>Status</Table.Th><Table.Th>Notes</Table.Th><Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {requests.length === 0 && (
              <Table.Tr><Table.Td colSpan={showEmployee ? 7 : 6}><Text c="dimmed" ta="center" py="md">{empty}</Text></Table.Td></Table.Tr>
            )}
            {requests.map((r) => (
              <Table.Tr key={r.id}>
                {showEmployee && <Table.Td><Text size="sm" fw={500}>{r.employeeName}</Text><Text size="xs" c="dimmed">{r.employeeNumber}</Text></Table.Td>}
                <Table.Td>
                  <Group gap={6} wrap="nowrap">
                    {r.leaveTypeName}
                    {r.needsDocument && <Tooltip label="A supporting document is needed for this length of leave"><IconAlertTriangle size={15} color="orange" /></Tooltip>}
                    {r.documentId && (
                      <Tooltip label="Download the supporting document">
                        <ActionIcon variant="subtle" size="sm" onClick={() => void getDocument(r)} aria-label="Download document"><IconPaperclip size={14} /></ActionIcon>
                      </Tooltip>
                    )}
                  </Group>
                </Table.Td>
                <Table.Td>{dateRange(r.startDate, r.endDate, r.halfDay)}</Table.Td>
                <Table.Td ta="right">{r.days}</Table.Td>
                <Table.Td><Badge variant="light" color={leaveStatusColor[r.status]}>{r.status}</Badge></Table.Td>
                <Table.Td>
                  {r.reason && <Text size="sm">{r.reason}</Text>}
                  {r.decidedBy && (
                    <Text size="xs" c="dimmed">{r.status} by {r.decidedBy}{r.decisionNote ? `: ${r.decisionNote}` : ''}</Text>
                  )}
                </Table.Td>
                <Table.Td w={r.canDecide ? 190 : 48}>
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    {r.canDecide && <>
                      <Button size="xs" onClick={() => setDeciding({ request: r, action: 'approve' })}>Approve</Button>
                      <Button size="xs" variant="default" onClick={() => setDeciding({ request: r, action: 'reject' })}>Decline</Button>
                    </>}
                    {(r.canCancel || (r.needsDocument && r.status !== 'Rejected' && r.status !== 'Cancelled')) && (
                      <Menu position="bottom-end">
                        <Menu.Target><ActionIcon variant="subtle" aria-label="Request actions"><IconDots size={16} /></ActionIcon></Menu.Target>
                        <Menu.Dropdown>
                          <FileButton accept=".pdf,.png,.jpg,.jpeg" onChange={(file) => file && attach.mutate({ r, file })}>
                            {(props) => <Menu.Item {...props} closeMenuOnClick={false}>Attach a document…</Menu.Item>}
                          </FileButton>
                          {r.canCancel && <Menu.Item color="red" onClick={() => setDeciding({ request: r, action: 'cancel' })}>Cancel request</Menu.Item>}
                        </Menu.Dropdown>
                      </Menu>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={deciding !== null} onClose={() => setDeciding(null)}
        title={deciding?.action === 'approve' ? 'Approve leave' : deciding?.action === 'reject' ? 'Decline leave' : 'Cancel leave'}>
        {deciding && <DecideForm {...deciding} done={() => setDeciding(null)} />}
      </Modal>
    </>
  )
}

function DecideForm({ request: r, action, done }: Decision & { done: () => void }) {
  const [note, setNote] = useState('')
  const decide = useApiMutation(() => api.post<LeaveRequest>(`/leave/requests/${r.id}/${action}`, { note: note.trim() || null }), {
    invalidate: ['leave', 'inbox'],
    success: (x) => `${x.employeeName}'s ${x.leaveTypeName.toLowerCase()} is ${x.status === 'Rejected' ? 'declined' : x.status.toLowerCase()}.`,
    onSuccess: done,
  })
  const needsNote = action === 'reject'
  return (
    <Stack>
      <Text size="sm"><b>{r.employeeName}</b>: {r.leaveTypeName.toLowerCase()}, {dateRange(r.startDate, r.endDate, r.halfDay)} ({days(r.days)})</Text>
      {r.reason && <Text size="sm" c="dimmed">“{r.reason}”</Text>}
      {r.needsDocument && action === 'approve' && <Alert color="orange" icon={<IconAlertTriangle size={16} />}>No supporting document is attached yet.</Alert>}
      {action === 'cancel' && r.status === 'Approved' && <Text size="sm">The days go back to the balance.</Text>}
      <Textarea label={needsNote ? 'Why is it declined?' : 'Note (optional)'} required={needsNote} autosize minRows={2} data-autofocus
        value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Back</Button>
        <Button color={action === 'approve' ? undefined : 'red'} disabled={needsNote && !note.trim()} loading={decide.isPending}
          onClick={() => decide.mutate(undefined)}>
          {action === 'approve' ? 'Approve' : action === 'reject' ? 'Decline' : 'Cancel leave'}
        </Button>
      </Group>
    </Stack>
  )
}

/**
 * Asks for leave. The request is checked as it's filled in (days, balance left, any problem); HR recording leave for
 * someone else (<paramref name="employeeId"/>) can mark it approved straight away.
 */
export function RequestLeaveForm({ employeeId, gender, canRecordApproved = false, done }:
  { employeeId?: string; gender?: string; canRecordApproved?: boolean; done: () => void }) {
  const types = useLeaveTypes()
  const form = useForm({
    initialValues: { leaveTypeId: '', range: [null, null] as [string | null, string | null], halfDay: false, reason: '', approve: canRecordApproved },
  })
  const v = form.getValues()
  const [start, end] = v.range
  const request: CreateLeaveRequest | null = v.leaveTypeId && start
    ? { employeeId: employeeId ?? null, leaveTypeId: v.leaveTypeId, startDate: start, endDate: end ?? start, halfDay: v.halfDay && (end ?? start) === start, reason: v.reason.trim() || null, approve: v.approve }
    : null
  const [debounced] = useDebouncedValue(request, 300)
  const preview = useQuery({
    queryKey: ['leave', 'preview', debounced],
    queryFn: () => api.post<LeavePreview>('/leave/requests/preview', debounced),
    enabled: debounced !== null,
  })
  const create = useApiMutation((r: CreateLeaveRequest) => api.post<LeaveRequest>('/leave/requests', r), {
    invalidate: ['leave'],
    success: (r) => r.status === 'Approved' ? `${r.leaveTypeName} recorded for ${r.employeeName}.` : 'Request sent for approval.',
    onSuccess: done,
  })
  const options = (types.data ?? []).filter((t) => t.isActive && (!t.eligibleGender || !gender || t.eligibleGender === gender))
  const type = options.find((t) => t.id === v.leaveTypeId)
  const p = preview.data
  const ok = request !== null && p !== undefined && p.problems.length === 0 && !preview.isFetching

  return (
    <form onSubmit={form.onSubmit(() => { if (request) create.mutate(request) })}>
      <Stack>
        <Select label="Type of leave" required data={options.map((t) => ({ value: t.id, label: t.name }))} {...form.getInputProps('leaveTypeId')} />
        {type?.description && <Text size="xs" c="dimmed" mt={-8}>{type.description}</Text>}
        <DatePickerInput type="range" allowSingleDateInRange label="Dates" required valueFormat="ddd D MMM YYYY" placeholder="First and last day off"
          {...form.getInputProps('range')} />
        {start && (end === null || end === start) && <Checkbox label="Half day only" {...form.getInputProps('halfDay', { type: 'checkbox' })} />}
        <Textarea label="Reason" placeholder="Optional for most leave" autosize minRows={2} {...form.getInputProps('reason')} />
        {canRecordApproved && <Checkbox label="Record it as approved (no approval step)" {...form.getInputProps('approve', { type: 'checkbox' })} />}

        {p && request && (
          p.problems.length > 0 ? (
            <Alert color="red" title="This can't be requested">
              <List size="sm">{p.problems.map((x) => <List.Item key={x}>{x}</List.Item>)}</List>
            </Alert>
          ) : (
            <Alert color="blue">
              <Text size="sm"><b>{days(p.days)}</b>{type?.countsCalendarDays ? ' (calendar days)' : ' (working days, without holidays)'}
                {p.availableAfter !== null && <> · <b>{days(p.availableAfter)}</b> left afterwards</>}</Text>
              {p.needsDocument && <Text size="sm" c="orange" mt={4}>For this length of {type?.name.toLowerCase()}, attach a supporting document (e.g. a medical certificate) after sending, from the request's menu.</Text>}
            </Alert>
          )
        )}
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" disabled={!ok} loading={create.isPending}>{v.approve && canRecordApproved ? 'Record leave' : 'Send request'}</Button>
        </Group>
      </Stack>
    </form>
  )
}
