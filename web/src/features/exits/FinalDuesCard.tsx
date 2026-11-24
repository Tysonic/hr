import { ActionIcon, Alert, Anchor, Badge, Button, Card, Group, NumberInput, Select, SimpleGrid, Stack, Table, Text, TextInput, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { FinalDues, SettlementLine } from '../../api/types'
import { useApiMutation } from '../../lib/mutation'
import { useMoney } from '../payroll/components'
import { usePayComponents } from '../payroll/PayrollSettingsPage'

/** A leaver's final dues: worked out from salary, leave and notice, editable, then sent to an off-cycle payroll. */
export function FinalDuesCard({ exitId }: { exitId: string }) {
  const q = useQuery({ queryKey: ['exits', exitId, 'dues'], queryFn: () => api.get<FinalDues>(`/exits/${exitId}/final-dues`) })
  if (q.isPending || q.isError) return null
  return <Dues d={q.data} key={JSON.stringify(q.data.lines) + q.data.status} />
}

function Dues({ d }: { d: FinalDues }) {
  const money = useMoney()
  const components = usePayComponents()
  const [lines, setLines] = useState<SettlementLine[]>(d.lines.map(({ componentId, label, amount, basis }) => ({ componentId, label, amount, basis })))
  const opts = { invalidate: ['exits', 'payroll'] }
  const save = useApiMutation(() => api.put<FinalDues>(`/exits/${d.exitId}/final-dues`, { lines }), { ...opts, success: 'Final dues saved.' })
  const recalc = useApiMutation(() => api.post<FinalDues>(`/exits/${d.exitId}/final-dues/recalculate`), { ...opts, success: 'Worked out again.' })
  const send = useApiMutation(() => api.post<FinalDues>(`/exits/${d.exitId}/final-dues/payroll`), { ...opts, success: 'Sent to a new off-cycle payroll.' })
  const editable = d.status === 'Draft' && d.exitStatus !== 'Withdrawn'
  const kind = (id: string) => components.data?.find((c) => c.id === id)?.kind
  const changed = JSON.stringify(lines) !== JSON.stringify(d.lines.map(({ componentId, label, amount, basis }) => ({ componentId, label, amount, basis })))
  const set = (i: number, patch: Partial<SettlementLine>) => setLines(lines.map((l, k) => (k === i ? { ...l, ...patch } : l)))
  const earnings = lines.filter((l) => kind(l.componentId) === 'Earning').reduce((s, l) => s + (l.amount || 0), 0)
  const deductions = lines.filter((l) => kind(l.componentId) === 'Deduction').reduce((s, l) => s + (l.amount || 0), 0)
  return (
    <Card withBorder>
      <Group justify="space-between" mb="xs">
        <Title order={5}>Final dues</Title>
        {d.status === 'InPayroll' && d.runId
          ? <Group gap="xs"><Badge variant="light" color="green">In payroll</Badge><Anchor component={Link} to={`/payroll/runs/${d.runId}`} size="sm">{d.runName}</Anchor></Group>
          : !d.saved && <Badge variant="light" color="gray">Suggested, not saved</Badge>}
      </Group>
      <Text size="xs" c="dimmed" mb="sm">The last month's salary is paid by the regular payroll, up to the last working day. These are the extras.</Text>
      <SimpleGrid cols={{ base: 2, sm: 4 }} mb="sm">
        <Fact label="Monthly basic" value={money(d.monthlyBasic)} />
        <Fact label="A working day" value={money(d.dailyRate)} />
        <Fact label="Unused annual leave" value={`${d.leaveDays} days`} />
        <Fact label="Statutory notice" value={`${d.noticeDays} days${d.payInLieu ? ', in lieu' : ''}`} />
      </SimpleGrid>
      {d.notes.map((n) => <Text key={n} size="xs" c="dimmed">{n}</Text>)}
      <Table.ScrollContainer minWidth={700} mt="sm">
        <Table verticalSpacing="xs">
          <Table.Thead><Table.Tr><Table.Th w={200}>Pay component</Table.Th><Table.Th>Description</Table.Th><Table.Th w={160} ta="right">Amount</Table.Th>{editable && <Table.Th w={40} />}</Table.Tr></Table.Thead>
          <Table.Tbody>
            {lines.map((l, i) => (
              <Table.Tr key={i}>
                <Table.Td>{editable
                  ? <Select size="xs" searchable value={l.componentId || null} onChange={(v) => set(i, { componentId: v ?? '' })}
                      data={(components.data ?? []).filter((c) => c.isActive).map((c) => ({ value: c.id, label: `${c.name} (${c.kind === 'Earning' ? '+' : '−'})` }))} />
                  : <Text size="sm">{d.lines[i]?.componentName}</Text>}</Table.Td>
                <Table.Td>
                  {editable ? <TextInput size="xs" value={l.label} onChange={(e) => set(i, { label: e.currentTarget.value })} /> : <Text size="sm">{l.label}</Text>}
                  {l.basis && <Text size="xs" c="dimmed">{l.basis}</Text>}
                </Table.Td>
                <Table.Td ta="right">{editable
                  ? <NumberInput size="xs" min={0} thousandSeparator="," value={l.amount} onChange={(v) => set(i, { amount: Number(v) || 0 })} />
                  : <Text size="sm">{kind(l.componentId) === 'Deduction' ? '−' : ''}{money(l.amount)}</Text>}</Table.Td>
                {editable && <Table.Td><ActionIcon variant="subtle" color="red" aria-label="Remove line" onClick={() => setLines(lines.filter((_, k) => k !== i))}><IconTrash size={14} /></ActionIcon></Table.Td>}
              </Table.Tr>
            ))}
            {lines.length === 0 && <Table.Tr><Table.Td colSpan={4}><Text size="sm" c="dimmed">Nothing due.</Text></Table.Td></Table.Tr>}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      {editable && <Button variant="subtle" size="xs" mt={4} leftSection={<IconPlus size={14} />}
        onClick={() => setLines([...lines, { componentId: '', label: '', amount: 0, basis: null }])}>Add line (e.g. an advance to recover)</Button>}
      <Text size="sm" mt="sm">Earnings <b>{money(earnings)}</b> · deductions <b>{money(deductions)}</b> · before tax, which the payroll works out</Text>
      {d.status === 'InPayroll' && <Alert mt="sm" variant="light">Calculate, approve and lock the payroll as usual. To change these, delete that payroll while it's a draft.</Alert>}
      {editable && (
        <Group justify="flex-end" mt="md">
          <Button variant="subtle" color="gray" loading={recalc.isPending} onClick={() => modals.openConfirmModal({
            title: 'Work the dues out again?', children: <Text size="sm">Your changes are replaced by amounts from the current salary, leave and exit details.</Text>,
            labels: { confirm: 'Work out again', cancel: 'Cancel' }, onConfirm: () => recalc.mutate(undefined),
          })}>Work out again</Button>
          <Button variant="default" disabled={d.saved && !changed} loading={save.isPending}
            onClick={() => save.mutate(undefined)} >{d.saved ? 'Save changes' : 'Save'}</Button>
          <Button disabled={!d.canSend || changed} loading={send.isPending} title={changed ? 'Save your changes first' : undefined}
            onClick={() => modals.openConfirmModal({
              title: 'Send to payroll?', children: <Text size="sm">This creates an off-cycle payroll, “Final dues: {d.employeeName}”, with these amounts, for calculating and approval.</Text>,
              labels: { confirm: 'Send to payroll', cancel: 'Cancel' }, onConfirm: () => send.mutate(undefined),
            })}>Send to payroll</Button>
        </Group>
      )}
    </Card>
  )
}

const Fact = ({ label, value }: { label: string; value: string }) => <Stack gap={0}><Text size="xs" c="dimmed">{label}</Text><Text size="sm" fw={500}>{value}</Text></Stack>
