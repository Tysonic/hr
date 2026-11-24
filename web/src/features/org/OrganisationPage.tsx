import { ActionIcon, Badge, Button, Group, Menu, Modal, NumberInput, Select, Stack, Table, Tabs, Text, TextInput, Textarea } from '@mantine/core'
import { useForm } from '@mantine/form'
import { modals } from '@mantine/modals'
import { IconDots, IconPlus } from '@tabler/icons-react'
import type { UseQueryResult } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { useSearchParams } from 'react-router'
import { api } from '../../api/client'
import type {
  Branch, Department, JobGrade, Position, SaveBranchRequest, SaveDepartmentRequest, SaveJobGradeRequest, SavePositionRequest,
} from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { money } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan, useMe } from '../../lib/permissions'
import { options, useBranches, useDepartments, useGrades, usePositions } from './hooks'

const tabs = ['departments', 'positions', 'grades', 'branches'] as const

/** Branches, departments, job grades and positions: everyone can look; HR admins and officers change them. */
export function OrganisationPage() {
  const [params, setParams] = useSearchParams()
  const tab = tabs.find((t) => t === params.get('tab')) ?? 'departments'
  return (
    <>
      <PageHeader title="Organisation" description="How the company is structured. Things in use can be deactivated but not deleted." />
      <Tabs value={tab} onChange={(t) => setParams({ tab: t ?? 'departments' }, { replace: true })} keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="departments">Departments</Tabs.Tab>
          <Tabs.Tab value="positions">Positions</Tabs.Tab>
          <Tabs.Tab value="grades">Job grades</Tabs.Tab>
          <Tabs.Tab value="branches">Branches</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="departments"><DepartmentsTab /></Tabs.Panel>
        <Tabs.Panel value="positions"><PositionsTab /></Tabs.Panel>
        <Tabs.Panel value="grades"><GradesTab /></Tabs.Panel>
        <Tabs.Panel value="branches"><BranchesTab /></Tabs.Panel>
      </Tabs>
    </>
  )
}

// One tab: a table of items with add/edit in a modal and activate/deactivate/delete in a row menu.

interface Column<T> { header: string; cell: (item: T) => ReactNode }

function OrgTab<T extends { id: string; isActive: boolean }>({ noun, path, query, columns, name, form }: {
  noun: string
  path: string
  query: UseQueryResult<T[]>
  columns: Column<T>[]
  name: (item: T) => string
  form: (item: T | null, done: () => void) => ReactNode
}) {
  const canManage = useCan('orgManage')
  const [editing, setEditing] = useState<T | null | 'new'>(null)
  const done = () => setEditing(null)
  const act = useApiMutation(({ item, action }: { item: T; action: 'activate' | 'deactivate' | 'delete' }) =>
    action === 'delete' ? api.delete(`${path}/${item.id}`) : api.post(`${path}/${item.id}/${action}`), {
    invalidate: ['org'],
    success: (_, { item, action }) => `${name(item)} ${action === 'delete' ? 'deleted' : `${action}d`}.`,
  })
  const confirmDelete = (item: T) => modals.openConfirmModal({
    title: `Delete ${name(item)}?`,
    children: <Text size="sm">This can't be undone. Anything still using it must be moved first.</Text>,
    labels: { confirm: 'Delete', cancel: 'Cancel' },
    confirmProps: { color: 'red' },
    onConfirm: () => act.mutate({ item, action: 'delete' }),
  })

  if (query.isPending) return <Loading />
  if (query.isError) return <LoadError error={query.error} />
  return (
    <>
      {canManage && (
        <Group justify="flex-end" mb="sm">
          <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>Add {noun}</Button>
        </Group>
      )}
      <Table.ScrollContainer minWidth={640}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              {columns.map((c) => <Table.Th key={c.header}>{c.header}</Table.Th>)}
              <Table.Th>Status</Table.Th>
              {canManage && <Table.Th />}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {query.data.length === 0 && (
              <Table.Tr><Table.Td colSpan={columns.length + 2}><Text c="dimmed" ta="center" py="md">No {noun}s yet.</Text></Table.Td></Table.Tr>
            )}
            {query.data.map((item) => (
              <Table.Tr key={item.id} opacity={item.isActive ? 1 : 0.6}>
                {columns.map((c) => <Table.Td key={c.header}>{c.cell(item)}</Table.Td>)}
                <Table.Td><Badge variant="light" color={item.isActive ? 'green' : 'gray'}>{item.isActive ? 'Active' : 'Inactive'}</Badge></Table.Td>
                {canManage && (
                  <Table.Td w={40}>
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${name(item)}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item onClick={() => setEditing(item)}>Edit</Menu.Item>
                        {item.isActive
                          ? <Menu.Item onClick={() => act.mutate({ item, action: 'deactivate' })}>Deactivate</Menu.Item>
                          : <Menu.Item onClick={() => act.mutate({ item, action: 'activate' })}>Activate</Menu.Item>}
                        <Menu.Item color="red" onClick={() => confirmDelete(item)}>Delete</Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  </Table.Td>
                )}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={editing !== null} onClose={done} title={editing === 'new' ? `New ${noun}` : `Edit ${noun}`}>
        {editing !== null && form(editing === 'new' ? null : editing, done)}
      </Modal>
    </>
  )
}

function useSave<R>(path: string, id: string | undefined, noun: string, done: () => void) {
  return useApiMutation((r: R) => (id ? api.put(`${path}/${id}`, r) : api.post(path, r)), {
    invalidate: ['org'],
    success: `${noun[0].toUpperCase()}${noun.slice(1)} saved.`,
    onSuccess: done,
  })
}

const blank = (v: string) => (v.trim() ? v.trim() : null)
const required = (v: string) => (v.trim() ? null : 'Required')

function FormButtons({ done, loading }: { done: () => void; loading: boolean }) {
  return (
    <Group justify="flex-end">
      <Button variant="default" onClick={done}>Cancel</Button>
      <Button type="submit" loading={loading}>Save</Button>
    </Group>
  )
}

// Departments

function DepartmentsTab() {
  const departments = useDepartments()
  return (
    <OrgTab noun="department" path="/org/departments" query={departments} name={(d) => d.name}
      columns={[
        { header: 'Department', cell: (d) => <Text fw={500}>{d.name}</Text> },
        { header: 'Code', cell: (d) => d.code ?? '—' },
        { header: 'Part of', cell: (d) => d.parentName ?? '—' },
        { header: 'Head', cell: (d) => d.headName ?? '—' },
        { header: 'Employees', cell: (d) => d.employeeCount },
      ]}
      form={(d, done) => <DepartmentForm department={d} all={departments.data ?? []} done={done} />} />
  )
}

function DepartmentForm({ department, all, done }: { department: Department | null; all: Department[]; done: () => void }) {
  const form = useForm({
    initialValues: { name: department?.name ?? '', code: department?.code ?? '', parentId: department?.parentId ?? null },
    validate: { name: required },
  })
  const save = useSave<SaveDepartmentRequest>('/org/departments', department?.id, 'department', done)
  const parents = options(all.filter((d) => d.id !== department?.id), (d) => d.name, department?.parentId)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({
      name: v.name.trim(), code: blank(v.code), parentId: v.parentId, headEmployeeId: department?.headEmployeeId ?? null,
    }))}>
      <Stack>
        <TextInput label="Name" required data-autofocus {...form.getInputProps('name')} />
        <TextInput label="Code" description="Optional short code, e.g. FIN" {...form.getInputProps('code')} />
        <Select label="Part of" description="Leave empty for a top-level department" clearable searchable data={parents}
          {...form.getInputProps('parentId')} />
        <FormButtons done={done} loading={save.isPending} />
      </Stack>
    </form>
  )
}

// Positions

function PositionsTab() {
  const positions = usePositions()
  return (
    <OrgTab noun="position" path="/org/positions" query={positions} name={(p) => p.title}
      columns={[
        { header: 'Position', cell: (p) => <Text fw={500}>{p.title}</Text> },
        { header: 'Department', cell: (p) => p.departmentName },
        { header: 'Grade', cell: (p) => p.jobGradeCode ?? '—' },
        { header: 'Employees', cell: (p) => p.employeeCount },
      ]}
      form={(p, done) => <PositionForm position={p} done={done} />} />
  )
}

function PositionForm({ position, done }: { position: Position | null; done: () => void }) {
  const departments = useDepartments()
  const grades = useGrades()
  const form = useForm({
    initialValues: { title: position?.title ?? '', departmentId: position?.departmentId ?? null, jobGradeId: position?.jobGradeId ?? null },
    validate: { title: required, departmentId: (v: string | null) => (v ? null : 'Required') },
  })
  const save = useSave<SavePositionRequest>('/org/positions', position?.id, 'position', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ title: v.title.trim(), departmentId: v.departmentId!, jobGradeId: v.jobGradeId }))}>
      <Stack>
        <TextInput label="Title" required data-autofocus {...form.getInputProps('title')} />
        <Select label="Department" required searchable data={options(departments.data, (d) => d.name, position?.departmentId)}
          {...form.getInputProps('departmentId')} />
        <Select label="Job grade" clearable data={options(grades.data, (g) => `${g.code} · ${g.name}`, position?.jobGradeId)}
          {...form.getInputProps('jobGradeId')} />
        <FormButtons done={done} loading={save.isPending} />
      </Stack>
    </form>
  )
}

// Job grades

function GradesTab() {
  const grades = useGrades()
  const { data: me } = useMe()
  const country = me?.company?.country
  const range = (g: JobGrade) =>
    g.minSalary === null && g.maxSalary === null || !country ? '—' : `${money(g.minSalary, country)} – ${money(g.maxSalary, country)}`
  return (
    <OrgTab noun="job grade" path="/org/grades" query={grades} name={(g) => g.code}
      columns={[
        { header: 'Grade', cell: (g) => <Text fw={500}>{g.code}</Text> },
        { header: 'Name', cell: (g) => g.name },
        { header: 'Rank', cell: (g) => g.rank },
        { header: 'Monthly salary range', cell: range },
        { header: 'Positions', cell: (g) => g.positionCount },
      ]}
      form={(g, done) => <GradeForm grade={g} done={done} />} />
  )
}

function GradeForm({ grade, done }: { grade: JobGrade | null; done: () => void }) {
  const { data: me } = useMe()
  const country = me?.company?.country
  const form = useForm({
    initialValues: {
      code: grade?.code ?? '', name: grade?.name ?? '', rank: grade?.rank ?? 1,
      minSalary: grade?.minSalary ?? ('' as number | ''), maxSalary: grade?.maxSalary ?? ('' as number | ''),
    },
    validate: { code: required, name: required },
  })
  const save = useSave<SaveJobGradeRequest>('/org/grades', grade?.id, 'job grade', done)
  const amount = { thousandSeparator: ',', decimalScale: country?.currencyMinorUnits ?? 0, min: 0, prefix: country ? `${country.currencyCode} ` : '' }
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({
      code: v.code.trim(), name: v.name.trim(), rank: Number(v.rank),
      minSalary: v.minSalary === '' ? null : Number(v.minSalary), maxSalary: v.maxSalary === '' ? null : Number(v.maxSalary),
    }))}>
      <Stack>
        <Group grow>
          <TextInput label="Code" required data-autofocus placeholder="G1" {...form.getInputProps('code')} />
          <NumberInput label="Rank" description="1 = most junior" min={0} {...form.getInputProps('rank')} />
        </Group>
        <TextInput label="Name" required placeholder="Officer" {...form.getInputProps('name')} />
        <Group grow>
          <NumberInput label="Minimum monthly salary" {...amount} {...form.getInputProps('minSalary')} />
          <NumberInput label="Maximum monthly salary" {...amount} {...form.getInputProps('maxSalary')} />
        </Group>
        <FormButtons done={done} loading={save.isPending} />
      </Stack>
    </form>
  )
}

// Branches

function BranchesTab() {
  const branches = useBranches()
  return (
    <OrgTab noun="branch" path="/org/branches" query={branches} name={(b) => b.name}
      columns={[
        { header: 'Branch', cell: (b) => <Text fw={500}>{b.name}</Text> },
        { header: 'Code', cell: (b) => b.code ?? '—' },
        { header: 'Address', cell: (b) => b.address ?? '—' },
        { header: 'Employees', cell: (b) => b.employeeCount },
      ]}
      form={(b, done) => <BranchForm branch={b} done={done} />} />
  )
}

function BranchForm({ branch, done }: { branch: Branch | null; done: () => void }) {
  const form = useForm({
    initialValues: { name: branch?.name ?? '', code: branch?.code ?? '', address: branch?.address ?? '' },
    validate: { name: required },
  })
  const save = useSave<SaveBranchRequest>('/org/branches', branch?.id, 'branch', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ name: v.name.trim(), code: blank(v.code), address: blank(v.address) }))}>
      <Stack>
        <TextInput label="Name" required data-autofocus placeholder="Head office" {...form.getInputProps('name')} />
        <TextInput label="Code" placeholder="KLA" {...form.getInputProps('code')} />
        <Textarea label="Address" autosize minRows={2} {...form.getInputProps('address')} />
        <FormButtons done={done} loading={save.isPending} />
      </Stack>
    </form>
  )
}
