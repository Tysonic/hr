import { Alert, Badge, Button, Checkbox, Group, Modal, Select, SegmentedControl, Stack, Table, Text, Textarea } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { ExitActionResult, ExitListItem, ExitType, Notice } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useActiveEmployees } from '../onboarding/components'
import { ExitBadge, exitTypeLabel } from './components'

export function ExitsPage() {
  const navigate = useNavigate()
  const [closed, setClosed] = useState('open')
  const [recording, setRecording] = useState(false)
  const q = useQuery({ queryKey: ['exits', closed], queryFn: () => api.get<ExitListItem[]>('/exits', { closed: closed === 'closed' }) })
  return (
    <>
      <PageHeader title="Exits" description="Resignations to accept, people working their notice, clearance and exit interviews."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setRecording(true)}>Record an exit</Button>} />
      <SegmentedControl mb="md" value={closed} onChange={setClosed} data={[{ value: 'open', label: 'In progress' }, { value: 'closed', label: 'Left or withdrawn (last year)' }]} />
      {q.isPending ? <Loading /> : q.isError ? <LoadError error={q.error} /> : q.data.length === 0 ? <Text c="dimmed">No exits.</Text> : (
        <Table.ScrollContainer minWidth={720}>
          <Table highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Type</Table.Th><Table.Th>Last working day</Table.Th><Table.Th>Clearance</Table.Th><Table.Th>Interview</Table.Th><Table.Th>Status</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {q.data.map((x) => (
                <Table.Tr key={x.id} style={{ cursor: 'pointer' }} onClick={() => navigate(`/exits/${x.id}`)}>
                  <Table.Td><Text size="sm">{x.employeeName}</Text><Text size="xs" c="dimmed">{x.employeeNumber}{x.departmentName && ` · ${x.departmentName}`}</Text></Table.Td>
                  <Table.Td>{exitTypeLabel[x.type]}</Table.Td>
                  <Table.Td>{date(x.lastWorkingDay)}</Table.Td>
                  <Table.Td>{x.items ? `${x.cleared} of ${x.items}` : '—'}</Table.Td>
                  <Table.Td>{x.interviewed ? <Badge variant="light" color="green">Done</Badge> : '—'}</Table.Td>
                  <Table.Td><ExitBadge value={x.status} /></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Modal opened={recording} onClose={() => setRecording(false)} size="lg" title="Record an exit">
        {recording && <RecordExit done={(id) => { setRecording(false); if (id) navigate(`/exits/${id}`) }} />}
      </Modal>
    </>
  )
}

/** The statutory notice for an employee given notice on a date. */
export function useNotice(employeeId: string | null, givenOn: string | null) {
  return useQuery({
    queryKey: ['exits', 'notice', employeeId, givenOn],
    queryFn: () => api.get<Notice>('/exits/notice', { employeeId: employeeId!, givenOn: givenOn! }),
    enabled: !!employeeId && !!givenOn,
  })
}

export const NoticeHint = ({ notice }: { notice?: Notice }) => notice
  ? <Text size="xs" c="dimmed">Statutory notice: {notice.days} days ({notice.basis}; {notice.source}).</Text> : null

function RecordExit({ done }: { done: (id?: string) => void }) {
  const employees = useActiveEmployees()
  const [warnings, setWarnings] = useState<string[]>([])
  const [employeeId, setEmployeeId] = useState<string | null>(null)
  const [type, setType] = useState<ExitType>('Resignation')
  const [givenOn, setGivenOn] = useState<string | null>(dayjs().format('YYYY-MM-DD'))
  const [lastDay, setLastDay] = useState<string | null>(null)
  const [payInLieu, setPayInLieu] = useState(false)
  const [reason, setReason] = useState('')
  const [itContactId, setItContactId] = useState<string | null>(null)
  const notice = useNotice(employeeId, givenOn)
  const noNotice = type === 'Dismissal' || type === 'Death'
  useEffect(() => {
    if (notice.data) setLastDay(noNotice ? givenOn : notice.data.suggestedLastDay)
  }, [notice.data, noNotice, givenOn])
  const save = useApiMutation(() => api.post<ExitActionResult>('/exits', {
    employeeId, type, reason: reason.trim() || null, noticeGivenOn: givenOn, lastWorkingDay: lastDay, payInLieu, itContactId,
  }), { invalidate: ['exits', 'employees'], success: 'Exit recorded. Clearance has started.', onSuccess: (r) => r.warnings.length ? setWarnings(r.warnings) : done(r.exit.id) })
  const people = (employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName} (${e.employeeNumber})` }))
  if (warnings.length) return (
    <Stack>
      <Alert color="yellow" title="Exit recorded">{warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>
      <Group justify="flex-end"><Button onClick={() => done(save.data?.exit.id)}>Open the exit</Button></Group>
    </Stack>
  )
  const needsReason = type === 'Termination' || type === 'Dismissal' || type === 'Redundancy'
  return (
    <Stack>
      <Select label="Employee" required searchable data={people} value={employeeId} onChange={setEmployeeId} />
      <Select label="Type" required value={type} onChange={(v) => setType((v ?? 'Resignation') as ExitType)}
        data={(Object.keys(exitTypeLabel) as ExitType[]).map((k) => ({ value: k, label: exitTypeLabel[k] }))} />
      <Group grow align="flex-start">
        <DateInput label={type === 'Death' ? 'Date' : 'Notice given on'} required valueFormat="D MMM YYYY" value={givenOn} onChange={setGivenOn} />
        <DateInput label="Last working day" required valueFormat="D MMM YYYY" value={lastDay} onChange={setLastDay} minDate={givenOn ?? undefined} />
      </Group>
      {!noNotice && <NoticeHint notice={notice.data} />}
      {!noNotice && <Checkbox label="Pay notice not worked in lieu" checked={payInLieu} onChange={(e) => setPayInLieu(e.currentTarget.checked)} />}
      <Textarea label="Reason" required={needsReason} autosize minRows={2} description="For HR's records; the employee doesn't see this."
        value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
      <Select label="IT contact" description="Who disables their accounts. Leave empty for HR to do it." clearable searchable
        data={people.filter((p) => p.value !== employeeId)} value={itContactId} onChange={setItContactId} />
      <Group justify="flex-end">
        <Button variant="default" onClick={() => done()}>Cancel</Button>
        <Button disabled={!employeeId || !givenOn || !lastDay || (needsReason && !reason.trim())} loading={save.isPending} onClick={() => save.mutate(undefined)}>Record exit</Button>
      </Group>
    </Stack>
  )
}
