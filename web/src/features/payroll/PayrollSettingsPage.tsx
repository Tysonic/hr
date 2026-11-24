import { ActionIcon, Alert, Badge, Button, Group, List, Menu, Modal, SegmentedControl, Stack, Switch, Table, Text, TextInput } from '@mantine/core'
import { useForm } from '@mantine/form'
import { IconDots, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { PayComponent, PayrollSettings, SavePayComponent } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'

export const usePayComponents = () => useQuery({ queryKey: ['payroll', 'components'], queryFn: () => api.get<PayComponent[]>('/payroll/components') })

export function PayrollSettingsPage() {
  const canEdit = useCan('payrollSetup')
  const settings = useQuery({ queryKey: ['payroll', 'settings'], queryFn: () => api.get<PayrollSettings>('/payroll/settings') })
  const components = usePayComponents()
  const [editing, setEditing] = useState<PayComponent | 'new' | null>(null)
  const toggle = useApiMutation((c: PayComponent) => api.post<PayComponent>(`/payroll/components/${c.id}/${c.isActive ? 'deactivate' : 'activate'}`), {
    invalidate: ['payroll'], success: (c) => `${c.name} ${c.isActive ? 'in use again' : 'no longer used'}.`,
  })
  if (components.isPending || settings.isPending) return <Loading />
  if (components.isError) return <LoadError error={components.error} />
  if (settings.isError) return <LoadError error={settings.error} />
  const s = settings.data
  return (
    <>
      <PageHeader title="Payroll settings" description="Earnings and deductions paid besides basic salary, and the statutory rules TyHR applies."
        actions={canEdit && <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>New component</Button>} />
      <Stack maw={1100}>
        <Alert color="blue" title={`${s.countryName} statutory payroll (from ${date(s.supportedFrom)})`}>
          <List size="sm" spacing={2}>{s.notes.map((n) => <List.Item key={n}>{n}</List.Item>)}</List>
          <Text size="xs" mt="xs">Rates change with each budget. Check them against the revenue authority before relying on a payroll.</Text>
        </Alert>
        <Table striped>
          <Table.Thead><Table.Tr><Table.Th>Component</Table.Th><Table.Th>Type</Table.Th><Table.Th>Treatment</Table.Th><Table.Th>Status</Table.Th><Table.Th /></Table.Tr></Table.Thead>
          <Table.Tbody>
            {components.data.map((c) => (
              <Table.Tr key={c.id} opacity={c.isActive ? 1 : 0.6}>
                <Table.Td><Text size="sm" fw={500}>{c.name} <Text span size="xs" c="dimmed">{c.code}</Text></Text>{c.description && <Text size="xs" c="dimmed">{c.description}</Text>}</Table.Td>
                <Table.Td><Badge variant="light" color={c.kind === 'Earning' ? 'green' : 'red'}>{c.kind}</Badge></Table.Td>
                <Table.Td><Text size="sm">{c.kind === 'Deduction' ? 'Taken from net pay' : [c.taxable ? 'Taxed' : 'Not taxed', c.pensionable ? 'counts for NSSF' : 'no NSSF'].join(', ')}</Text></Table.Td>
                <Table.Td><Badge variant="light" color={c.isActive ? 'green' : 'gray'}>{c.isActive ? 'In use' : 'Not in use'}</Badge></Table.Td>
                <Table.Td w={40}>{canEdit && (
                  <Menu position="bottom-end">
                    <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${c.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                    <Menu.Dropdown>
                      <Menu.Item onClick={() => setEditing(c)}>Edit</Menu.Item>
                      <Menu.Item onClick={() => toggle.mutate(c)}>{c.isActive ? 'Stop using' : 'Use again'}</Menu.Item>
                    </Menu.Dropdown>
                  </Menu>
                )}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Stack>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'New pay component' : 'Edit pay component'}>
        {editing !== null && <ComponentForm component={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </>
  )
}

function ComponentForm({ component, done }: { component: PayComponent | null; done: () => void }) {
  const form = useForm<SavePayComponent>({
    initialValues: component ?? { code: '', name: '', kind: 'Earning', taxable: true, pensionable: true, description: null },
    validate: { code: (v) => (v.trim() ? null : 'Required'), name: (v) => (v.trim() ? null : 'Required') },
  })
  const save = useApiMutation((v: SavePayComponent) => component ? api.put<PayComponent>(`/payroll/components/${component.id}`, v) : api.post<PayComponent>('/payroll/components', v), {
    invalidate: ['payroll'], success: (c) => `${c.name} saved.`, onSuccess: done,
  })
  const v = form.getValues()
  return (
    <form onSubmit={form.onSubmit((x) => save.mutate({ ...x, description: x.description?.trim() || null }))}>
      <Stack>
        {!component && <SegmentedControl data={['Earning', 'Deduction']} {...form.getInputProps('kind')} />}
        <Group grow>
          <TextInput label="Name" required placeholder={v.kind === 'Earning' ? 'Lunch allowance' : 'Staff welfare'} {...form.getInputProps('name')} />
          <TextInput label="Code" required maxLength={20} placeholder={v.kind === 'Earning' ? 'LUNCH' : 'WELFARE'} {...form.getInputProps('code')} />
        </Group>
        {v.kind === 'Earning' && <>
          <Switch label="Taxed (PAYE)" {...form.getInputProps('taxable', { type: 'checkbox' })} />
          <Switch label="Counts for NSSF" {...form.getInputProps('pensionable', { type: 'checkbox' })} />
          <Text size="xs" c="dimmed">Most cash allowances are taxed and count for NSSF. Check unusual ones with your tax adviser.</Text>
        </>}
        {v.kind === 'Deduction' && <Text size="xs" c="dimmed">Deductions come off net pay after tax.</Text>}
        <TextInput label="Description" {...form.getInputProps('description')} value={v.description ?? ''} />
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={save.isPending}>Save</Button></Group>
      </Stack>
    </form>
  )
}
