import { Button, Group, Modal, NumberInput, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { useForm } from '@mantine/form'
import { IconAdjustments, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { Employee, EmployeeLeave } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { BalanceCards, RequestLeaveForm, RequestsTable } from './components'

/** An employee's leave on their record: balances and requests; HR records leave for them and adjusts balances. */
export function EmployeeLeaveTab({ employee }: { employee: Employee }) {
  const [year, setYear] = useState<number | null>(null)
  const [dialog, setDialog] = useState<'record' | 'adjust' | null>(null)
  const leave = useQuery({
    queryKey: ['leave', 'balances', employee.id, year],
    queryFn: () => api.get<EmployeeLeave>('/leave/balances', { employeeId: employee.id, year }),
  })
  if (leave.isPending) return <Loading />
  if (leave.isError) return <LoadError error={leave.error} />
  const l = leave.data
  const close = () => setDialog(null)

  return (
    <Stack maw={1100}>
      <Group justify="space-between">
        <Select w={130} allowDeselect={false} value={String(l.year)} onChange={(v) => setYear(Number(v))}
          data={[l.year + 1, l.year, l.year - 1, l.year - 2].map(String).filter((v, i, a) => a.indexOf(v) === i)} />
        {l.canManage && (
          <Group>
            <Button variant="default" leftSection={<IconAdjustments size={16} />} onClick={() => setDialog('adjust')}>Adjust balance</Button>
            <Button leftSection={<IconPlus size={16} />} onClick={() => setDialog('record')}>Record leave</Button>
          </Group>
        )}
      </Group>
      <BalanceCards balances={l.balances} />
      <Title order={5}>Requests ({date(l.yearStart)} – {date(l.yearEnd)})</Title>
      <RequestsTable requests={l.requests} empty="No leave in this year." />
      {l.adjustments.length > 0 && (
        <>
          <Title order={5}>Adjustments</Title>
          <Table>
            <Table.Tbody>
              {l.adjustments.map((a) => (
                <Table.Tr key={a.id}>
                  <Table.Td w={90} fw={600} c={a.days < 0 ? 'red' : 'green'}>{a.days > 0 ? '+' : ''}{a.days}</Table.Td>
                  <Table.Td>{a.leaveTypeName}</Table.Td>
                  <Table.Td>{a.reason}</Table.Td>
                  <Table.Td><Text size="xs" c="dimmed">{a.recordedBy}, {date(a.recordedAt)}</Text></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </>
      )}
      <Modal opened={dialog === 'record'} onClose={close} title={`Record leave for ${employee.fullName}`} size="lg">
        {dialog === 'record' && <RequestLeaveForm employeeId={employee.id} gender={l.gender} canRecordApproved done={close} />}
      </Modal>
      <Modal opened={dialog === 'adjust'} onClose={close} title={`Adjust ${employee.fullName}'s balance`}>
        {dialog === 'adjust' && <AdjustForm leave={l} done={close} />}
      </Modal>
    </Stack>
  )
}

function AdjustForm({ leave: l, done }: { leave: EmployeeLeave; done: () => void }) {
  const form = useForm({
    initialValues: { leaveTypeId: '', days: '' as number | '', reason: '' },
    validate: {
      leaveTypeId: (v) => (v ? null : 'Required'),
      days: (v) => (v === '' || v === 0 ? 'Enter the days to add (or take away, with a minus)' : null),
      reason: (v) => (v.trim() ? null : 'Required'),
    },
  })
  const save = useApiMutation((v: { leaveTypeId: string; days: number; reason: string }) =>
    api.post<EmployeeLeave>('/leave/adjustments', { employeeId: l.employeeId, leaveYear: l.year, ...v }), {
    invalidate: ['leave'], success: 'Balance adjusted.', onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ leaveTypeId: v.leaveTypeId, days: Number(v.days), reason: v.reason.trim() }))}>
      <Stack>
        <Select label="Leave type" required data={l.balances.filter((b) => b.balance).map((b) => ({ value: b.leaveTypeId, label: b.name }))} {...form.getInputProps('leaveTypeId')} />
        <NumberInput label={`Days for ${l.year}`} description="Positive adds days, negative takes them away; whole or half days" required step={0.5} decimalScale={1} {...form.getInputProps('days')} />
        <TextInput label="Reason" required placeholder="Opening balance from the old system" {...form.getInputProps('reason')} />
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={save.isPending}>Adjust</Button></Group>
      </Stack>
    </form>
  )
}
