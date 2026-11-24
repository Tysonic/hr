import { Badge, Button, Group, Menu, Modal, Select, Stack, Table, Text, TextInput, ActionIcon } from '@mantine/core'
import { useForm } from '@mantine/form'
import { useDisclosure } from '@mantine/hooks'
import { modals } from '@mantine/modals'
import { IconDots, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Country, CreateTenantRequest, Tenant } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, slugPattern, toSlug } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

export function TenantsPage() {
  const tenants = useQuery({ queryKey: ['tenants'], queryFn: () => api.get<Tenant[]>('/platform/tenants') })
  const [opened, { open, close }] = useDisclosure()

  const setStatus = useApiMutation(
    ({ slug, action }: { slug: string; action: 'suspend' | 'reactivate' }) => api.post<Tenant>(`/platform/tenants/${slug}/${action}`),
    { invalidate: ['tenants'], success: (t) => `${t.name} is now ${t.status.toLowerCase()}.` },
  )

  const confirmSuspend = (t: Tenant) => modals.openConfirmModal({
    title: `Suspend ${t.name}?`,
    children: <Text size="sm">Its users will be refused until the company is reactivated. No data is deleted.</Text>,
    labels: { confirm: 'Suspend', cancel: 'Cancel' },
    confirmProps: { color: 'red' },
    onConfirm: () => setStatus.mutate({ slug: t.slug, action: 'suspend' }),
  })

  return (
    <>
      <PageHeader title="Companies" description="Customer companies on TyHR and their country packs."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={open}>New company</Button>} />
      {tenants.isPending ? <Loading /> : tenants.isError ? <LoadError error={tenants.error} /> : (
        <Table.ScrollContainer minWidth={720}>
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Company</Table.Th><Table.Th>Code</Table.Th><Table.Th>Country</Table.Th>
                <Table.Th>HR admin</Table.Th><Table.Th>Created</Table.Th><Table.Th>Status</Table.Th><Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {tenants.data.length === 0 && (
                <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" py="md">No companies yet.</Text></Table.Td></Table.Tr>
              )}
              {tenants.data.map((t) => (
                <Table.Tr key={t.id}>
                  <Table.Td fw={500}>{t.name}</Table.Td>
                  <Table.Td><Text ff="monospace" size="sm">{t.slug}</Text></Table.Td>
                  <Table.Td>{t.country.name} ({t.country.currencyCode})</Table.Td>
                  <Table.Td>{t.adminEmail}</Table.Td>
                  <Table.Td>{date(t.createdAt)}</Table.Td>
                  <Table.Td><Badge color={t.status === 'Active' ? 'green' : 'red'} variant="light">{t.status}</Badge></Table.Td>
                  <Table.Td>
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${t.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        {t.status === 'Active'
                          ? <Menu.Item color="red" onClick={() => confirmSuspend(t)}>Suspend</Menu.Item>
                          : <Menu.Item onClick={() => setStatus.mutate({ slug: t.slug, action: 'reactivate' })}>Reactivate</Menu.Item>}
                      </Menu.Dropdown>
                    </Menu>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Modal opened={opened} onClose={close} title="New company" size="lg">
        <NewTenantForm onDone={close} />
      </Modal>
    </>
  )
}

function NewTenantForm({ onDone }: { onDone: () => void }) {
  const countries = useQuery({ queryKey: ['countries'], queryFn: () => api.get<Country[]>('/countries'), staleTime: Infinity })
  const form = useForm<CreateTenantRequest>({
    initialValues: { name: '', slug: '', countryCode: 'UG', adminEmail: '', adminFirstName: '', adminLastName: '' },
    validate: {
      name: (v) => (v.trim() ? null : 'Required'),
      slug: (v) => (slugPattern.test(v) ? null : '2-63 lowercase letters, digits or hyphens; no hyphen at either end'),
      countryCode: (v) => (v ? null : 'Required'),
      adminEmail: (v) => (/^\S+@\S+\.\S+$/.test(v) ? null : 'Enter an email address'),
      adminFirstName: (v) => (v.trim() ? null : 'Required'),
      adminLastName: (v) => (v.trim() ? null : 'Required'),
    },
  })
  const create = useApiMutation((values: CreateTenantRequest) => api.post<Tenant>('/platform/tenants', values), {
    invalidate: ['tenants'],
    success: (t) => `${t.name} created. ${t.adminEmail} has been emailed a link to set their password.`,
    onSuccess: onDone,
  })

  return (
    <form onSubmit={form.onSubmit((v) => create.mutate(v))}>
      <Stack>
        <TextInput label="Company name" required {...form.getInputProps('name')}
          onChange={(e) => {
            const name = e.currentTarget.value
            const autoSlug = form.values.slug === '' || form.values.slug === toSlug(form.values.name)
            form.setValues({ name, ...(autoSlug ? { slug: toSlug(name) } : {}) })
          }} />
        <TextInput label="Company code" description="Its web address and sign-in key; can't be changed later." required
          {...form.getInputProps('slug')} onChange={(e) => form.setFieldValue('slug', toSlug(e.currentTarget.value))} />
        <Select label="Country pack" description="Statutory payroll rules, currency and holidays; can't be changed later." required
          data={(countries.data ?? []).map((c) => ({ value: c.code, label: `${c.name} (${c.currencyCode})` }))}
          {...form.getInputProps('countryCode')} />
        <Text fw={600} size="sm" mt="xs">First HR admin</Text>
        <Group grow>
          <TextInput label="First name" required {...form.getInputProps('adminFirstName')} />
          <TextInput label="Last name" required {...form.getInputProps('adminLastName')} />
        </Group>
        <TextInput label="Email" type="email" required {...form.getInputProps('adminEmail')} />
        <Group justify="flex-end">
          <Button variant="default" onClick={onDone}>Cancel</Button>
          <Button type="submit" loading={create.isPending}>Create and invite</Button>
        </Group>
      </Stack>
    </form>
  )
}
