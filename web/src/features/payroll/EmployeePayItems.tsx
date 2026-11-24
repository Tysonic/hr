import { ActionIcon, Badge, Button, Group, Menu, Modal, NumberInput, Select, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconDots, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { EmployeePay, PayItem, SavePayItem } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { useMoney } from './components'
import { usePayComponents } from './PayrollSettingsPage'

/** On the employee's Pay tab: recurring allowances and deductions, and how they're taxed. */
export function EmployeePayItems({ employeeId }: { employeeId: string }) {
  const canEdit = useCan('payrollSetup')
  const m = useMoney()
  const [editing, setEditing] = useState<PayItem | 'new' | null>(null)
  const pay = useQuery({ queryKey: ['payroll', 'employee', employeeId], queryFn: () => api.get<EmployeePay>(`/payroll/employees/${employeeId}`) })
  const remove = useApiMutation((i: PayItem) => api.delete<EmployeePay>(`/payroll/employees/${employeeId}/items/${i.id}`), { invalidate: ['payroll'], success: 'Removed.' })
  const tax = useApiMutation((v: { resident: boolean; secondaryEmployment: boolean }) => api.put<EmployeePay>(`/payroll/employees/${employeeId}/tax-profile`, v), {
    invalidate: ['payroll'], success: 'Tax status saved.',
  })
  if (pay.isPending) return <Loading />
  if (pay.isError) return <LoadError error={pay.error} />
  const p = pay.data
  return (
    <Stack>
      <Group justify="space-between">
        <Title order={5}>Allowances and deductions</Title>
        {canEdit && <Button size="xs" variant="default" leftSection={<IconPlus size={14} />} onClick={() => setEditing('new')}>Add</Button>}
      </Group>
      <Table striped>
        <Table.Tbody>
          {p.items.length === 0 && <Table.Tr><Table.Td><Text c="dimmed" size="sm">Basic salary only.</Text></Table.Td></Table.Tr>}
          {p.items.map((i) => (
            <Table.Tr key={i.id} opacity={i.current ? 1 : 0.6}>
              <Table.Td><Text size="sm" fw={500}>{i.componentName}</Text>{i.note && <Text size="xs" c="dimmed">{i.note}</Text>}</Table.Td>
              <Table.Td><Badge size="sm" variant="light" color={i.kind === 'Earning' ? 'green' : 'red'}>{i.kind}</Badge></Table.Td>
              <Table.Td ta="right">{m(i.amount)} a month</Table.Td>
              <Table.Td><Text size="sm">{date(i.startDate)} – {i.endDate ? date(i.endDate) : 'ongoing'}</Text></Table.Td>
              <Table.Td w={40}>{canEdit && (
                <Menu position="bottom-end">
                  <Menu.Target><ActionIcon variant="subtle" aria-label="Actions"><IconDots size={16} /></ActionIcon></Menu.Target>
                  <Menu.Dropdown>
                    <Menu.Item onClick={() => setEditing(i)}>Edit or end</Menu.Item>
                    <Menu.Item color="red" onClick={() => remove.mutate(i)}>Remove (entered by mistake)</Menu.Item>
                  </Menu.Dropdown>
                </Menu>
              )}</Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      <Group>
        <Switch label="Tax resident" disabled={!canEdit} checked={p.resident} onChange={(e) => tax.mutate({ resident: e.currentTarget.checked, secondaryEmployment: p.secondaryEmployment })} />
        <Switch label="Secondary employment (another employer gives the tax-free band)" disabled={!canEdit} checked={p.secondaryEmployment}
          onChange={(e) => tax.mutate({ resident: p.resident, secondaryEmployment: e.currentTarget.checked })} />
      </Group>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'Add an allowance or deduction' : 'Edit'}>
        {editing !== null && <ItemForm employeeId={employeeId} item={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </Stack>
  )
}

function ItemForm({ employeeId, item, done }: { employeeId: string; item: PayItem | null; done: () => void }) {
  const components = usePayComponents()
  const form = useForm({
    initialValues: {
      componentId: item?.componentId ?? null as string | null, amount: item?.amount ?? ('' as number | ''),
      startDate: item?.startDate ?? dayjs().startOf('month').format('YYYY-MM-DD') as string | null, endDate: item?.endDate ?? null as string | null, note: item?.note ?? '',
    },
    validate: { componentId: (v) => (v ? null : 'Required'), amount: (v) => (v ? null : 'Required'), startDate: (v) => (v ? null : 'Required') },
  })
  const save = useApiMutation((v: SavePayItem) => item
    ? api.put<EmployeePay>(`/payroll/employees/${employeeId}/items/${item.id}`, v)
    : api.post<EmployeePay>(`/payroll/employees/${employeeId}/items`, v), { invalidate: ['payroll'], success: 'Saved.', onSuccess: done })
  return (
    <form onSubmit={form.onSubmit((x) => save.mutate({ componentId: x.componentId!, amount: Number(x.amount), startDate: x.startDate!, endDate: x.endDate || null, note: x.note.trim() || null }))}>
      <Stack>
        <Select label="What" required disabled={!!item} data={['Earning', 'Deduction'].map((k) => ({ group: k === 'Earning' ? 'Earnings' : 'Deductions',
          items: (components.data ?? []).filter((c) => (c.isActive || c.id === item?.componentId) && c.kind === k).map((c) => ({ value: c.id, label: c.name })) }))}
          {...form.getInputProps('componentId')} />
        <NumberInput label="Amount a month" required min={0} thousandSeparator="," {...form.getInputProps('amount')} />
        <Group grow>
          <DateInput label="From" required valueFormat="D MMM YYYY" {...form.getInputProps('startDate')} />
          <DateInput label="Until" clearable placeholder="Ongoing" valueFormat="D MMM YYYY" {...form.getInputProps('endDate')} />
        </Group>
        <TextInput label="Note" placeholder="Loan of 1,200,000 over 12 months" {...form.getInputProps('note')} />
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={save.isPending}>Save</Button></Group>
      </Stack>
    </form>
  )
}
