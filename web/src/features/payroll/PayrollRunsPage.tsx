import { Badge, Button, Group, Modal, SegmentedControl, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput, MonthPickerInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { PayrollRun, PayrollRunKind, PayrollSettings } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { kindLabel, monthName, StatusBadge, useMoney } from './components'

export function PayrollRunsPage() {
  const canPrepare = useCan('payrollPrepare')
  const navigate = useNavigate()
  const m = useMoney()
  const [creating, setCreating] = useState(false)
  const runs = useQuery({ queryKey: ['payroll', 'runs'], queryFn: () => api.get<PayrollRun[]>('/payroll/runs') })
  if (runs.isPending) return <Loading />
  if (runs.isError) return <LoadError error={runs.error} />
  return (
    <>
      <PageHeader title="Payroll" description="Each month's payroll: prepared, approved by someone else, then locked."
        actions={canPrepare && <Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>New payroll</Button>} />
      <Table.ScrollContainer minWidth={850}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr><Table.Th>Payroll</Table.Th><Table.Th>Month</Table.Th><Table.Th>Status</Table.Th><Table.Th ta="right">People</Table.Th>
              <Table.Th ta="right">Gross</Table.Th><Table.Th ta="right">Net</Table.Th><Table.Th>Pay date</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {runs.data.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" py="lg">No payrolls yet.</Text></Table.Td></Table.Tr>}
            {runs.data.map((r) => (
              <Table.Tr key={r.id} style={{ cursor: 'pointer' }} onClick={() => navigate(`/payroll/runs/${r.id}`)}>
                <Table.Td>
                  <Group gap={6}><Text fw={500} size="sm">{r.name}</Text>{r.kind !== 'Regular' && <Badge size="sm" variant="outline">{kindLabel[r.kind]}</Badge>}
                    {r.reversedByRunId && <Badge size="sm" color="red" variant="light">Reversed</Badge>}</Group>
                </Table.Td>
                <Table.Td>{monthName(r.year, r.month)}</Table.Td>
                <Table.Td><StatusBadge status={r.status} /></Table.Td>
                <Table.Td ta="right">{r.status === 'Draft' && r.employeeCount === 0 ? '–' : r.employeeCount}</Table.Td>
                <Table.Td ta="right">{m(r.gross)}</Table.Td>
                <Table.Td ta="right">{m(r.net)}</Table.Td>
                <Table.Td>{date(r.payDate)}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={creating} onClose={() => setCreating(false)} title="New payroll">
        {creating && <NewRunForm done={(id) => { setCreating(false); if (id) navigate(`/payroll/runs/${id}`) }} />}
      </Modal>
    </>
  )
}

function NewRunForm({ done }: { done: (id?: string) => void }) {
  const settings = useQuery({ queryKey: ['payroll', 'settings'], queryFn: () => api.get<PayrollSettings>('/payroll/settings') })
  const form = useForm({
    initialValues: { month: dayjs().startOf('month').format('YYYY-MM-DD') as string | null, kind: 'Regular' as PayrollRunKind, name: '', payDate: null as string | null },
    validate: { month: (v) => (v ? null : 'Choose the month') },
  })
  const create = useApiMutation((v: { year: number; month: number; kind: PayrollRunKind; name: string | null; payDate: string | null }) =>
    api.post<PayrollRun>('/payroll/runs', v), { invalidate: ['payroll'], success: (r) => `${r.name} created.`, onSuccess: (r) => done(r.id) })
  const v = form.getValues()
  return (
    <form onSubmit={form.onSubmit((x) => {
      const d = dayjs(x.month)
      create.mutate({ year: d.year(), month: d.month() + 1, kind: x.kind, name: x.name.trim() || null, payDate: x.payDate || null })
    })}>
      <Stack>
        <SegmentedControl data={[{ value: 'Regular', label: 'Regular' }, { value: 'OffCycle', label: 'Off-cycle' }]} {...form.getInputProps('kind')} />
        <Text size="sm" c="dimmed">{v.kind === 'Regular'
          ? 'Pays everyone their salary, allowances and recurring deductions, plus any one-off amounts you add.'
          : 'Pays only the one-off amounts you add (a bonus, arrears), taxed on top of what the month has already paid.'}</Text>
        <MonthPickerInput label="Month" required minDate={settings.data?.supportedFrom} {...form.getInputProps('month')} />
        <TextInput label="Name" placeholder="e.g. November 2026 payroll" {...form.getInputProps('name')} />
        <DateInput label="Pay date" placeholder="The month's last day" clearable valueFormat="D MMM YYYY" {...form.getInputProps('payDate')} />
        <Group justify="flex-end"><Button variant="default" onClick={() => done()}>Cancel</Button><Button type="submit" loading={create.isPending}>Create</Button></Group>
      </Stack>
    </form>
  )
}
