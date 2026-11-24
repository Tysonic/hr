import { Alert, Anchor, Button, Card, Checkbox, Group, Modal, Select, SimpleGrid, Stack, Text, Textarea, TextInput, Title, NumberInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { modals } from '@mantine/modals'
import { IconArrowLeft, IconDownload, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, download } from '../../api/client'
import type { ClearanceOwner, EmployeeExit, ExitActionResult, ExitType } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useActiveEmployees } from '../onboarding/components'
import { ClearanceTable, ExitBadge, exitTypeLabel, InterviewForm, InterviewView, ownerOptions } from './components'
import { NoticeHint } from './ExitsPage'
import { FinalDuesCard } from './FinalDuesCard'
import { useCan } from '../../lib/permissions'

type Dialog = 'accept' | 'edit' | 'complete' | 'withdraw' | 'item' | 'interview' | null

export function ExitPage() {
  const { id } = useParams()
  const [dialog, setDialog] = useState<Dialog>(null)
  const [warnings, setWarnings] = useState<string[]>([])
  const canPay = useCan('employeesPay')
  const q = useQuery({ queryKey: ['exits', id], queryFn: () => api.get<EmployeeExit>(`/exits/${id}`) })
  const removeItem = useApiMutation((itemId: string) => api.delete(`/exits/clearance/${itemId}`), { invalidate: ['exits', 'me'], success: 'Item removed.' })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const x = q.data
  const open = x.status === 'Requested' || x.status === 'InProgress'
  const close = () => setDialog(null)
  const done = (r?: ExitActionResult) => { setWarnings(r?.warnings ?? []); close() }
  return (
    <>
      <Anchor component={Link} to="/exits" size="sm"><Group gap={4}><IconArrowLeft size={14} />Exits</Group></Anchor>
      <PageHeader title={`${x.employeeName}: ${exitTypeLabel[x.type].toLowerCase()}`}
        description={`${x.employeeNumber}${x.positionTitle ? ` · ${x.positionTitle}` : ''}${x.departmentName ? ` · ${x.departmentName}` : ''} · joined ${date(x.hireDate)}`}
        actions={
          <Group>
            <ExitBadge value={x.status} />
            {x.status === 'Completed' && <Button variant="default" leftSection={<IconDownload size={16} />}
              onClick={() => void download(`/exits/${x.id}/certificate`, `Certificate of service - ${x.employeeName}.pdf`)}>Certificate of service</Button>}
            {x.status === 'Requested' && <Button onClick={() => setDialog('accept')}>Accept resignation</Button>}
            {open && <Button variant="default" onClick={() => setDialog('edit')}>Edit</Button>}
            {open && <Button variant="default" color="red" onClick={() => setDialog('withdraw')}>Withdraw</Button>}
            {x.status === 'InProgress' && <Button color="dark" disabled={!x.canComplete} onClick={() => setDialog('complete')}
              title={x.canComplete ? undefined : 'Once the last working day has passed and clearance is done'}>Complete exit</Button>}
          </Group>
        } />
      <Stack maw={1000}>
        {warnings.length > 0 && <Alert color="yellow" withCloseButton onClose={() => setWarnings([])}>{warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>}
        {x.warnings.map((w) => <Alert key={w} color="yellow" variant="light">{w}</Alert>)}
        <SimpleGrid cols={{ base: 1, sm: 3 }}>
          <Card withBorder><Text size="xs" c="dimmed">Notice given</Text><Text fw={600}>{date(x.noticeGivenOn)}</Text>
            <Text size="xs" c="dimmed">{x.noticeDaysGiven} days{x.payInLieu ? ', rest paid in lieu' : ''}</Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Last working day</Text><Text fw={600}>{date(x.lastWorkingDay)}</Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Statutory notice</Text><Text fw={600}>{x.notice.days} days</Text><Text size="xs" c="dimmed">{x.notice.basis}</Text></Card>
        </SimpleGrid>
        <Card withBorder>
          <Title order={5}>Details</Title>
          {x.reason && <Text size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{x.reason}</Text>}
          <Text size="xs" c="dimmed" mt={4}>
            {x.type === 'Resignation' && x.requestedBy !== x.acceptedBy ? `Handed in by ${x.requestedBy} on ${dateTime(x.requestedAt)}` : `Recorded by ${x.requestedBy} on ${dateTime(x.requestedAt)}`}
            {x.acceptedAt && x.requestedBy !== x.acceptedBy && ` · accepted by ${x.acceptedBy} on ${dateTime(x.acceptedAt)}`}
          </Text>
          {x.status === 'Completed' && <Text size="sm" mt="xs">Left; completed by {x.completedBy} on {dateTime(x.completedAt)}. Eligible for rehire: <b>{x.eligibleForRehire ? 'yes' : 'no'}</b>{x.completionNote && ` (${x.completionNote})`}</Text>}
          {x.status === 'Withdrawn' && <Text size="sm" mt="xs">Withdrawn by {x.withdrawnBy} on {dateTime(x.withdrawnAt)}{x.withdrawalNote && `: ${x.withdrawalNote}`}</Text>}
        </Card>
        {x.status !== 'Requested' && (
          <Card withBorder>
            <Group justify="space-between" mb="xs">
              <Title order={5}>Clearance</Title>
              {x.status === 'InProgress' && <Button size="xs" variant="subtle" leftSection={<IconPlus size={14} />} onClick={() => setDialog('item')}>Add item</Button>}
            </Group>
            {x.clearance.length === 0 ? <Text size="sm" c="dimmed">No clearance items.</Text>
              : <ClearanceTable items={x.clearance} onDelete={x.status === 'InProgress' ? (i) => modals.openConfirmModal({
                title: `Remove “${i.title}”?`, labels: { confirm: 'Remove', cancel: 'Cancel' }, confirmProps: { color: 'red' }, onConfirm: () => removeItem.mutate(i.id),
              }) : undefined} />}
          </Card>
        )}
        {canPay && (x.status === 'InProgress' || x.status === 'Completed') && <FinalDuesCard exitId={x.id} />}
        <Card withBorder>
          <Group justify="space-between" mb="xs">
            <Title order={5}>Exit interview</Title>
            {x.status === 'InProgress' && !x.interview?.byEmployee && <Button size="xs" variant="subtle" onClick={() => setDialog('interview')}>{x.interview ? 'Edit' : 'Record interview'}</Button>}
          </Group>
          <Text size="xs" c="dimmed" mb="xs">Confidential to HR. {x.employeeName} can fill it in under My profile while working their notice.</Text>
          {x.interview ? <InterviewView interview={x.interview} /> : <Text size="sm" c="dimmed">Not done yet.</Text>}
        </Card>
      </Stack>
      <Modal opened={dialog === 'accept'} onClose={close} title="Accept the resignation">{dialog === 'accept' && <Accept x={x} done={done} />}</Modal>
      <Modal opened={dialog === 'edit'} onClose={close} title="Edit the exit">{dialog === 'edit' && <Edit x={x} done={close} />}</Modal>
      <Modal opened={dialog === 'complete'} onClose={close} title={`Complete ${x.employeeName}'s exit`}>{dialog === 'complete' && <Complete x={x} done={done} />}</Modal>
      <Modal opened={dialog === 'withdraw'} onClose={close} title="Withdraw the exit">{dialog === 'withdraw' && <Withdraw x={x} done={close} />}</Modal>
      <Modal opened={dialog === 'item'} onClose={close} title="Add a clearance item">{dialog === 'item' && <AddItem x={x} done={close} />}</Modal>
      <Modal opened={dialog === 'interview'} onClose={close} size="lg" title="Exit interview">
        {dialog === 'interview' && <InterviewForm initial={x.interview?.answers} path={`/exits/${x.id}/interview`} submitLabel="Save interview" done={close} />}
      </Modal>
    </>
  )
}

function Accept({ x, done }: { x: EmployeeExit; done: (r: ExitActionResult) => void }) {
  const employees = useActiveEmployees()
  const [lastDay, setLastDay] = useState<string | null>(x.lastWorkingDay)
  const [payInLieu, setPayInLieu] = useState(false)
  const [itContactId, setItContactId] = useState<string | null>(null)
  const save = useApiMutation(() => api.post<ExitActionResult>(`/exits/${x.id}/accept`, { lastWorkingDay: lastDay, payInLieu, itContactId }), {
    invalidate: ['exits', 'me'], success: `Accepted. ${x.employeeName} has been emailed and clearance has started.`, onSuccess: done,
  })
  return (
    <Stack>
      <Text size="sm">Handed in on {date(x.noticeGivenOn)}, proposing {date(x.lastWorkingDay)} as the last working day.</Text>
      <DateInput label="Last working day" valueFormat="D MMM YYYY" value={lastDay} onChange={setLastDay} minDate={x.noticeGivenOn} />
      <NoticeHint notice={x.notice} />
      <Checkbox label="Pay notice not worked in lieu" checked={payInLieu} onChange={(e) => setPayInLieu(e.currentTarget.checked)} />
      <Select label="IT contact" description="Who disables their accounts. Leave empty for HR to do it." clearable searchable value={itContactId} onChange={setItContactId}
        data={(employees.data?.items ?? []).filter((e) => e.id !== x.employeeId && e.workEmail).map((e) => ({ value: e.id, label: e.fullName }))} />
      <Group justify="flex-end"><Button disabled={!lastDay} loading={save.isPending} onClick={() => save.mutate(undefined)}>Accept</Button></Group>
    </Stack>
  )
}

function Edit({ x, done }: { x: EmployeeExit; done: () => void }) {
  const [type, setType] = useState<ExitType>(x.type)
  const [givenOn, setGivenOn] = useState<string | null>(x.noticeGivenOn)
  const [lastDay, setLastDay] = useState<string | null>(x.lastWorkingDay)
  const [payInLieu, setPayInLieu] = useState(x.payInLieu)
  const [reason, setReason] = useState(x.reason ?? '')
  const save = useApiMutation(() => api.put<EmployeeExit>(`/exits/${x.id}`, { type, reason: reason.trim() || null, noticeGivenOn: givenOn, lastWorkingDay: lastDay, payInLieu }), {
    invalidate: ['exits', 'me'], success: 'Saved. Open clearance items moved with the last working day.', onSuccess: done,
  })
  return (
    <Stack>
      <Select label="Type" value={type} disabled={x.status === 'Requested'} onChange={(v) => setType((v ?? x.type) as ExitType)}
        data={(Object.keys(exitTypeLabel) as ExitType[]).map((k) => ({ value: k, label: exitTypeLabel[k] }))} />
      <Group grow>
        <DateInput label="Notice given on" valueFormat="D MMM YYYY" value={givenOn} onChange={setGivenOn} />
        <DateInput label="Last working day" valueFormat="D MMM YYYY" value={lastDay} onChange={setLastDay} minDate={givenOn ?? undefined} />
      </Group>
      <Checkbox label="Pay notice not worked in lieu" checked={payInLieu} onChange={(e) => setPayInLieu(e.currentTarget.checked)} />
      <Textarea label="Reason" autosize minRows={2} value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
      <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button disabled={!givenOn || !lastDay} loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button></Group>
    </Stack>
  )
}

function Complete({ x, done }: { x: EmployeeExit; done: (r: ExitActionResult) => void }) {
  const [rehire, setRehire] = useState(true)
  const [note, setNote] = useState('')
  const save = useApiMutation(() => api.post<ExitActionResult>(`/exits/${x.id}/complete`, { eligibleForRehire: rehire, note: note.trim() || null }), {
    invalidate: ['exits', 'employees', 'me', 'leave'], success: `${x.employeeName} is now recorded as having left.`, onSuccess: done,
  })
  return (
    <Stack>
      <Text size="sm">{x.employeeName} will be marked as exited from {date(x.lastWorkingDay)}: their contract ends that day, they can no longer sign in, and leave booked after it is cancelled.</Text>
      {!x.interview && <Alert color="yellow" variant="light">There's no exit interview. You can still complete the exit.</Alert>}
      <Checkbox label="Eligible for rehire" checked={rehire} onChange={(e) => setRehire(e.currentTarget.checked)} />
      <TextInput label="Note (optional)" value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end"><Button variant="default" onClick={() => done({ exit: x, warnings: [] })}>Cancel</Button>
        <Button color="dark" loading={save.isPending} onClick={() => save.mutate(undefined)}>Complete exit</Button></Group>
    </Stack>
  )
}

function Withdraw({ x, done }: { x: EmployeeExit; done: () => void }) {
  const [note, setNote] = useState('')
  const save = useApiMutation(() => api.post<EmployeeExit>(`/exits/${x.id}/withdraw`, { note: note.trim() || null }), {
    invalidate: ['exits', 'me'], success: 'Exit withdrawn. Open clearance items were removed.', onSuccess: done,
  })
  return (
    <Stack>
      <Text size="sm">{x.employeeName} stays employed. Use this when a resignation is taken back or an exit was recorded by mistake.</Text>
      <Textarea label="Why" autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button color="red" loading={save.isPending} onClick={() => save.mutate(undefined)}>Withdraw</Button></Group>
    </Stack>
  )
}

function AddItem({ x, done }: { x: EmployeeExit; done: () => void }) {
  const employees = useActiveEmployees()
  const [title, setTitle] = useState('')
  const [owner, setOwner] = useState<ClearanceOwner>('Hr')
  const [dueDays, setDueDays] = useState<number | string>(0)
  const [assigneeId, setAssigneeId] = useState<string | null>(null)
  const save = useApiMutation(() => api.post<EmployeeExit>(`/exits/${x.id}/clearance`, {
    step: { title: title.trim(), owner, dueDays: Number(dueDays) || 0, description: null }, assigneeId,
  }), { invalidate: ['exits', 'me'], success: 'Item added.', onSuccess: done })
  return (
    <Stack>
      <TextInput label="Item" required placeholder="e.g. Return the company car" value={title} onChange={(e) => setTitle(e.currentTarget.value)} />
      <Group grow>
        <Select label="Who" data={ownerOptions} value={owner} onChange={(v) => setOwner((v ?? 'Hr') as ClearanceOwner)} />
        <NumberInput label="Due (days from last day)" description="Negative is before it" min={-90} max={90} value={dueDays} onChange={setDueDays} />
      </Group>
      {(owner === 'It' || owner === 'Manager') && <Select label="Person" required={owner === 'It'} clearable searchable value={assigneeId} onChange={setAssigneeId}
        description={owner === 'Manager' ? 'Leave empty for their line manager.' : undefined}
        data={(employees.data?.items ?? []).filter((e) => e.id !== x.employeeId && e.workEmail).map((e) => ({ value: e.id, label: e.fullName }))} />}
      <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button>
        <Button disabled={!title.trim() || (owner === 'It' && !assigneeId)} loading={save.isPending} onClick={() => save.mutate(undefined)}>Add</Button></Group>
    </Stack>
  )
}
