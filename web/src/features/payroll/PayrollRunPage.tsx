import {
  ActionIcon, Alert, Anchor, Badge, Button, Card, Drawer, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Stepper, Table, Tabs, Text, Textarea, TextInput,
} from '@mantine/core'
import { useForm } from '@mantine/form'
import { notifications } from '@mantine/notifications'
import { IconAlertTriangle, IconArrowBackUp, IconBuildingBank, IconCalculator, IconCheck, IconDeviceMobile, IconDownload, IconFileSpreadsheet, IconLock, IconMail, IconSend, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api, download } from '../../api/client'
import type { EmailPayslipsResult, EmployeeListItem, PagedResult, PayComponent, PayrollInput, PayrollRun, PayrollVariance, Payslip, PayslipSummary } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { kindLabel, monthName, PayslipView, StatusBadge, useMoney } from './components'

const steps = ['Draft', 'Calculated', 'Submitted', 'Approved', 'Locked'] as const

export function PayrollRunPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const m = useMoney()
  const canApproveRole = useCan('payrollApprove')
  const [dialog, setDialog] = useState<'send-back' | 'delete' | null>(null)
  const run = useQuery({ queryKey: ['payroll', 'run', id], queryFn: () => api.get<PayrollRun>(`/payroll/runs/${id}`) })
  const act = useApiMutation((action: 'calculate' | 'submit' | 'approve' | 'lock' | 'reverse') => api.post<PayrollRun>(`/payroll/runs/${id}/${action}`), {
    invalidate: ['payroll', 'inbox'],
    success: (r) => r.id !== id ? 'Reversal created; check it and submit it for approval.' : `${r.name}: ${r.status === 'Submitted' ? 'sent for approval' : r.status.toLowerCase()}.`,
    onSuccess: (r) => { if (r.id !== id) navigate(`/payroll/runs/${r.id}`) },
  })
  if (run.isPending) return <Loading />
  if (run.isError) return <LoadError error={run.error} />
  const r = run.data
  const editable = r.status === 'Draft' || r.status === 'Calculated'
  const by = (who: string | null, when: string | null) => (who ? `${who}, ${dateTime(when)}` : undefined)

  return (
    <>
      <PageHeader title={r.name}
        description={<Group gap="xs"><Text size="sm" c="dimmed">{monthName(r.year, r.month)} · pay date {date(r.payDate)}</Text>
          <StatusBadge status={r.status} />{r.kind !== 'Regular' && <Badge variant="outline">{kindLabel[r.kind]}</Badge>}</Group>}
        actions={<Group>
          {r.canPrepare && editable && <Button variant={r.status === 'Draft' ? 'filled' : 'default'} leftSection={<IconCalculator size={16} />}
            loading={act.isPending && act.variables === 'calculate'} onClick={() => act.mutate('calculate')}>{r.status === 'Draft' && !r.calculatedAt ? 'Calculate' : 'Recalculate'}</Button>}
          {r.canPrepare && r.status === 'Calculated' && <Button leftSection={<IconSend size={16} />} loading={act.isPending && act.variables === 'submit'} onClick={() => act.mutate('submit')}>Submit for approval</Button>}
          {r.canApprove && <Button color="teal" leftSection={<IconCheck size={16} />} loading={act.isPending && act.variables === 'approve'} onClick={() => act.mutate('approve')}>Approve</Button>}
          {canApproveRole && (r.status === 'Submitted' || r.status === 'Approved') && <Button variant="default" leftSection={<IconArrowBackUp size={16} />} onClick={() => setDialog('send-back')}>Send back</Button>}
          {r.canPrepare && r.status === 'Approved' && <Button leftSection={<IconLock size={16} />} loading={act.isPending && act.variables === 'lock'} onClick={() => act.mutate('lock')}>Lock</Button>}
          {r.canReverse && <Button variant="default" color="red" onClick={() => act.mutate('reverse')} loading={act.isPending && act.variables === 'reverse'}>Reverse</Button>}
          {r.canPrepare && editable && <ActionIcon variant="subtle" color="red" size="lg" aria-label="Delete payroll" onClick={() => setDialog('delete')}><IconTrash size={18} /></ActionIcon>}
        </Group>} />

      <Stack gap="lg">
        <Stepper active={steps.indexOf(r.status) + (r.status === 'Locked' ? 1 : 0)} size="sm">
          <Stepper.Step label="Draft" description={by(r.createdBy, r.createdAt)} />
          <Stepper.Step label="Calculated" description={by(r.calculatedBy, r.calculatedAt)} />
          <Stepper.Step label="Submitted" description={by(r.submittedBy, r.submittedAt)} />
          <Stepper.Step label="Approved" description={by(r.approvedBy, r.approvedAt)} />
          <Stepper.Step label="Locked" description={by(r.lockedBy, r.lockedAt)} />
        </Stepper>
        {r.status === 'Submitted' && !r.canApprove && canApproveRole && <Alert color="blue">You prepared this payroll, so another HR admin has to approve it.</Alert>}
        {r.returnNote && <Alert color="orange" title="Sent back">{r.returnNote}</Alert>}
        {r.status === 'Draft' && r.calculatedAt && <Alert color="yellow">Amounts changed since the last calculation. Recalculate before submitting.</Alert>}
        {r.reversesRunId && <Alert color="red">This reverses <Anchor component={Link} to={`/payroll/runs/${r.reversesRunId}`}>the original payroll</Anchor>: every amount is the negative of the original's.</Alert>}
        {r.reversedByRunId && <Alert color="red">This payroll is <Anchor component={Link} to={`/payroll/runs/${r.reversedByRunId}`}>being reversed</Anchor>.</Alert>}
        {r.warnings.length > 0 && <Alert color="orange" icon={<IconAlertTriangle size={18} />}>{r.warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>}

        {(r.status === 'Approved' || r.status === 'Locked') && <Outputs run={r} />}

        <SimpleGrid cols={{ base: 2, md: 5 }}>
          {([['People paid', String(r.employeeCount)], ['Gross pay', m(r.gross)], ['Deductions', m(r.deductions)], ['Net pay', m(r.net)], ['Employer contributions', m(r.employerCost)]] as const)
            .map(([label, value]) => <Card key={label} withBorder padding="sm"><Text size="xs" c="dimmed">{label}</Text><Text fw={600}>{value}</Text></Card>)}
        </SimpleGrid>

        <Tabs defaultValue="payslips" keepMounted={false}>
          <Tabs.List mb="md">
            <Tabs.Tab value="payslips">Payslips</Tabs.Tab>
            {r.kind !== 'Reversal' && <Tabs.Tab value="inputs">One-off amounts</Tabs.Tab>}
            <Tabs.Tab value="variance">Changes since last month</Tabs.Tab>
          </Tabs.List>
          <Tabs.Panel value="payslips"><Payslips runId={r.id} /></Tabs.Panel>
          {r.kind !== 'Reversal' && <Tabs.Panel value="inputs"><Inputs run={r} /></Tabs.Panel>}
          <Tabs.Panel value="variance"><Variance runId={r.id} /></Tabs.Panel>
        </Tabs>
      </Stack>

      <Modal opened={dialog === 'send-back'} onClose={() => setDialog(null)} title="Send back to the preparer">
        {dialog === 'send-back' && <SendBack runId={r.id} done={() => setDialog(null)} />}
      </Modal>
      <Modal opened={dialog === 'delete'} onClose={() => setDialog(null)} title={`Delete ${r.name}?`}>
        {dialog === 'delete' && <DeleteRun run={r} done={(deleted) => { setDialog(null); if (deleted) navigate('/payroll') }} />}
      </Modal>
    </>
  )
}

function Payslips({ runId }: { runId: string }) {
  const m = useMoney()
  const [open, setOpen] = useState<string | null>(null)
  const slips = useQuery({ queryKey: ['payroll', 'payslips', runId], queryFn: () => api.get<PayslipSummary[]>(`/payroll/runs/${runId}/payslips`) })
  const slip = useQuery({ queryKey: ['payroll', 'payslip', open], queryFn: () => api.get<Payslip>(`/payroll/runs/payslips/${open}`), enabled: !!open })
  if (slips.isPending) return <Loading />
  if (slips.isError) return <LoadError error={slips.error} />
  return (
    <>
      <Table.ScrollContainer minWidth={850}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr><Table.Th>Employee</Table.Th><Table.Th ta="right">Gross</Table.Th><Table.Th ta="right">PAYE</Table.Th>
              <Table.Th ta="right">All deductions</Table.Th><Table.Th ta="right">Net</Table.Th><Table.Th ta="right">Employer</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {slips.data.length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="lg">No payslips yet. Calculate the payroll.</Text></Table.Td></Table.Tr>}
            {slips.data.map((p) => (
              <Table.Tr key={p.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(p.id)}>
                <Table.Td>
                  <Group gap={6}><Text size="sm" fw={500}>{p.employeeName}</Text>{p.warnings.length > 0 && <IconAlertTriangle size={14} color="var(--mantine-color-orange-6)" />}</Group>
                  <Text size="xs" c="dimmed">{[p.employeeNumber, p.departmentName].filter(Boolean).join(' · ')}</Text>
                </Table.Td>
                <Table.Td ta="right">{m(p.gross)}</Table.Td>
                <Table.Td ta="right">{m(p.paye)}</Table.Td>
                <Table.Td ta="right">{m(p.totalDeductions)}</Table.Td>
                <Table.Td ta="right" fw={600} c={p.net < 0 ? 'red' : undefined}>{m(p.net)}</Table.Td>
                <Table.Td ta="right">{m(p.employerContributions)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Drawer opened={!!open} onClose={() => setOpen(null)} position="right" size="lg" title="Payslip">
        {slip.isPending ? <Loading /> : slip.isError ? <LoadError error={slip.error} /> : (
          <Stack>
            <Group justify="flex-end"><Button size="xs" variant="default" leftSection={<IconDownload size={14} />}
              onClick={() => void save(`/payroll/runs/payslips/${slip.data.id}/pdf`, `payslip-${slip.data.employeeNumber}-${slip.data.year}-${String(slip.data.month).padStart(2, '0')}.pdf`)}>PDF</Button></Group>
            <PayslipView payslip={slip.data} />
          </Stack>
        )}
      </Drawer>
    </>
  )
}

function Inputs({ run }: { run: PayrollRun }) {
  const m = useMoney()
  const editable = run.canPrepare && (run.status === 'Draft' || run.status === 'Calculated')
  const inputs = useQuery({ queryKey: ['payroll', 'inputs', run.id], queryFn: () => api.get<PayrollInput[]>(`/payroll/runs/${run.id}/inputs`) })
  const remove = useApiMutation((i: PayrollInput) => api.delete<PayrollInput[]>(`/payroll/runs/${run.id}/inputs/${i.id}`), { invalidate: ['payroll'], success: 'Removed. Recalculate the payroll.' })
  if (inputs.isPending) return <Loading />
  if (inputs.isError) return <LoadError error={inputs.error} />
  return (
    <Stack>
      <Text size="sm" c="dimmed">Amounts for this payroll only: overtime, a bonus, a one-off deduction. Recurring allowances and deductions belong on the employee's Pay tab.</Text>
      {editable && <AddInput runId={run.id} />}
      <Table striped>
        <Table.Tbody>
          {inputs.data.length === 0 && <Table.Tr><Table.Td><Text c="dimmed" ta="center" py="md">No one-off amounts.</Text></Table.Td></Table.Tr>}
          {inputs.data.map((i) => (
            <Table.Tr key={i.id}>
              <Table.Td><Text size="sm" fw={500}>{i.employeeName}</Text><Text size="xs" c="dimmed">{i.employeeNumber}</Text></Table.Td>
              <Table.Td>{i.componentName}{i.note && <Text size="xs" c="dimmed">{i.note}</Text>}</Table.Td>
              <Table.Td ta="right" c={i.kind === 'Deduction' ? 'red' : undefined}>{i.kind === 'Deduction' ? '−' : ''}{m(i.amount)}</Table.Td>
              <Table.Td w={40}>{editable && <ActionIcon variant="subtle" color="red" aria-label="Remove" onClick={() => remove.mutate(i)}><IconTrash size={16} /></ActionIcon>}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Stack>
  )
}

function AddInput({ runId }: { runId: string }) {
  const employees = useQuery({ queryKey: ['employees', 'all-active'], queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { status: 'Active', pageSize: 200 }) })
  const components = useQuery({ queryKey: ['payroll', 'components'], queryFn: () => api.get<PayComponent[]>('/payroll/components') })
  const form = useForm({
    initialValues: { employeeId: null as string | null, componentId: null as string | null, amount: '' as number | '', note: '' },
    validate: { employeeId: (v) => (v ? null : 'Required'), componentId: (v) => (v ? null : 'Required'), amount: (v) => (v ? null : 'Required') },
  })
  const add = useApiMutation((x: { employeeId: string; componentId: string; amount: number; note: string | null }) => api.post<PayrollInput[]>(`/payroll/runs/${runId}/inputs`, x), {
    invalidate: ['payroll'], success: 'Added. Recalculate the payroll.', onSuccess: () => form.setValues({ amount: '', note: '' }),
  })
  return (
    <form onSubmit={form.onSubmit((x) => add.mutate({ employeeId: x.employeeId!, componentId: x.componentId!, amount: Number(x.amount), note: x.note.trim() || null }))}>
      <Group align="flex-end" wrap="wrap">
        <Select label="Employee" searchable w={240} data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName} (${e.employeeNumber})` }))} {...form.getInputProps('employeeId')} />
        <Select label="For" w={200} data={['Earning', 'Deduction'].map((k) => ({ group: k === 'Earning' ? 'Earnings' : 'Deductions',
          items: (components.data ?? []).filter((c) => c.isActive && c.kind === k).map((c) => ({ value: c.id, label: c.name })) }))} {...form.getInputProps('componentId')} />
        <NumberInput label="Amount" w={150} min={0} thousandSeparator="," {...form.getInputProps('amount')} />
        <TextInput label="Note" w={200} placeholder="12 hours at 1.5×" {...form.getInputProps('note')} />
        <Button type="submit" loading={add.isPending}>Add</Button>
      </Group>
    </form>
  )
}

function Variance({ runId }: { runId: string }) {
  const m = useMoney()
  const v = useQuery({ queryKey: ['payroll', 'variance', runId], queryFn: () => api.get<PayrollVariance>(`/payroll/runs/${runId}/variance`) })
  if (v.isPending) return <Loading />
  if (v.isError) return <LoadError error={v.error} />
  const d = v.data
  return (
    <Stack>
      <Text size="sm">Compared with locked payrolls for {monthName(d.previousYear, d.previousMonth)}: net {m(d.previousNet)} then, {m(d.net)} now
        ({d.net - d.previousNet >= 0 ? '+' : ''}{m(d.net - d.previousNet)}).</Text>
      <Table striped>
        <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Change</Table.Th><Table.Th ta="right">Net before</Table.Th><Table.Th ta="right">Net now</Table.Th><Table.Th ta="right">Difference</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>
          {d.rows.length === 0 && <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" ta="center" py="md">Nobody's pay changed.</Text></Table.Td></Table.Tr>}
          {d.rows.map((row) => (
            <Table.Tr key={row.employeeId}>
              <Table.Td><Text size="sm" fw={500}>{row.employeeName}</Text><Text size="xs" c="dimmed">{row.employeeNumber}</Text></Table.Td>
              <Table.Td><Badge variant="light" color={row.change === 'Pay changed' ? (Math.abs(row.netChangePercent ?? 0) >= 10 ? 'orange' : 'blue') : row.change.startsWith('New') ? 'green' : 'gray'}>{row.change}</Badge></Table.Td>
              <Table.Td ta="right">{m(row.previousNet)}</Table.Td>
              <Table.Td ta="right">{m(row.net)}</Table.Td>
              <Table.Td ta="right" c={row.netChange < 0 ? 'red' : 'green'}>{row.netChange >= 0 ? '+' : ''}{m(row.netChange)}{row.netChangePercent !== null && ` (${row.netChangePercent}%)`}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Stack>
  )
}

function SendBack({ runId, done }: { runId: string; done: () => void }) {
  const [note, setNote] = useState('')
  const send = useApiMutation(() => api.post<PayrollRun>(`/payroll/runs/${runId}/send-back`, { note }), { invalidate: ['payroll'], success: 'Sent back.', onSuccess: done })
  return (
    <Stack>
      <Textarea label="What needs changing" required autosize minRows={3} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button disabled={!note.trim()} loading={send.isPending} onClick={() => send.mutate(undefined)}>Send back</Button></Group>
    </Stack>
  )
}

function DeleteRun({ run, done }: { run: PayrollRun; done: (deleted: boolean) => void }) {
  const remove = useApiMutation(() => api.delete<void>(`/payroll/runs/${run.id}`), { invalidate: ['payroll'], success: `${run.name} deleted.`, onSuccess: () => done(true) })
  return (
    <Stack>
      <Text size="sm">Its payslips and one-off amounts are deleted too. This can't be undone.</Text>
      <Group justify="flex-end"><Button variant="default" onClick={() => done(false)}>Cancel</Button><Button color="red" loading={remove.isPending} onClick={() => remove.mutate(undefined)}>Delete</Button></Group>
    </Stack>
  )
}

/** Downloads a file, showing a toast if it fails. */
export const save = (path: string, fileName: string, query?: Record<string, string | number>) =>
  download(path, fileName, query).catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))

/** Once approved: payment files and the journal; once locked, emailing payslips. */
function Outputs({ run: r }: { run: PayrollRun }) {
  const period = `${r.year}-${String(r.month).padStart(2, '0')}`
  const [progress, setProgress] = useState<{ sent: number; remaining: number } | null>(null)
  // The API sends a batch per request; ask for the next until there are no more. Ones that fail are passed over.
  const sendAll = async () => {
    let total: EmailPayslipsResult | null = null
    try {
      for (;;) {
        const x: EmailPayslipsResult = await api.post(`/payroll/runs/${r.id}/email-payslips${total?.next ? `?after=${total.next}` : ''}`)
        total = total ? { ...x, sent: total.sent + x.sent, alreadySent: total.alreadySent, failed: [...total.failed, ...x.failed] } : x
        setProgress({ sent: total.sent, remaining: x.remaining })
        if (!x.next) return total
      }
    } catch (e) {
      if (total?.sent && e instanceof Error) throw new Error(`${total.sent} payslips were emailed first. ${e.message}`)
      throw e
    } finally {
      setProgress(null)
    }
  }
  const email = useApiMutation(sendAll, {
    invalidate: ['payroll'],
    success: (x) => !x.mailConfigured ? 'No mail server is set up, so nothing was sent.'
      : [`${x.sent} payslips emailed`, x.alreadySent && `${x.alreadySent} already sent`, x.noEmail.length && `${x.noEmail.length} without an email address (${x.noEmail.join(', ')})`,
        x.failed.length && `${x.failed.length} couldn't be sent (${x.failed.slice(0, 5).join(', ')}${x.failed.length > 5 ? '…' : ''}); check their addresses and send again`].filter(Boolean).join('; ') + '.',
  })
  if (r.kind === 'Reversal') return null
  return (
    <Card withBorder padding="sm">
      <Group gap="xs">
        <Text size="sm" fw={500} mr="sm">Outputs</Text>
        {r.canPrepare && <Button size="xs" variant="default" leftSection={<IconBuildingBank size={14} />}
          onClick={() => void save(`/payroll/runs/${r.id}/payment-file`, `bank-payments-${period}.csv`, { method: 'Bank' })}>Bank file</Button>}
        {r.canPrepare && <Button size="xs" variant="default" leftSection={<IconDeviceMobile size={14} />}
          onClick={() => void save(`/payroll/runs/${r.id}/payment-file`, `mobile-money-payments-${period}.csv`, { method: 'MobileMoney' })}>Mobile money file</Button>}
        <Button size="xs" variant="default" leftSection={<IconFileSpreadsheet size={14} />}
          onClick={() => void save(`/payroll/runs/${r.id}/journal`, `payroll-journal-${period}.xlsx`)}>Journal</Button>
        <Button size="xs" variant="default" leftSection={<IconFileSpreadsheet size={14} />}
          onClick={() => void save('/payroll/returns', `statutory-returns-${period}.xlsx`, { year: r.year, month: r.month })}>Statutory returns</Button>
        {r.canPrepare && r.status === 'Locked' && <Button size="xs" leftSection={<IconMail size={14} />} loading={email.isPending}
          onClick={() => email.mutate(undefined)}>Email payslips</Button>}
        {progress && <Text size="xs" c="dimmed">{progress.sent.toLocaleString('en-GB')} sent, {progress.remaining.toLocaleString('en-GB')} to go…</Text>}
      </Group>
      {r.status === 'Approved' && <Text size="xs" c="dimmed" mt={6}>Lock the payroll once people are paid; then payslips can be emailed and employees see them under My payslips.</Text>}
    </Card>
  )
}
