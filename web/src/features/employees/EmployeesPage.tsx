import { Badge, Button, Checkbox, Divider, Group, Modal, NumberInput, Pagination, Select, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { useDebouncedValue, useDisclosure } from '@mantine/hooks'
import { notifications } from '@mantine/notifications'
import { IconFileExport, IconFileImport, IconPlus, IconSearch } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { api, download } from '../../api/client'
import type { Country, CreateEmployeeRequest, Employee, EmployeeListItem, EmployeeStatus, PagedResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan, useMe } from '../../lib/permissions'
import { options, useBranches, useDepartments } from '../org/hooks'
import {
  cleanEmployment, cleanPersonal, ContractFields, emptyContract, emptyPersonal, EmploymentFields, PersonalFields, personalValidation,
} from './fields'
import { ImportEmployees } from './ImportEmployees'

export const statusColor: Record<EmployeeStatus, string> = { Active: 'green', Suspended: 'orange', Exited: 'gray' }

/** HR and payroll see everyone; line managers see their team (the API decides). */
export function EmployeesPage() {
  const canAdd = useCan('employeesManage')
  const [opened, { open, close }] = useDisclosure()
  const [importing, importer] = useDisclosure()
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 300)
  const [departmentId, setDepartmentId] = useState<string | null>(null)
  const [branchId, setBranchId] = useState<string | null>(null)
  const [status, setStatus] = useState<string | null>('Active')
  const [page, setPage] = useState(1)
  const departments = useDepartments()
  const branches = useBranches()
  const { data: me } = useMe()
  const list = useQuery({
    queryKey: ['employees', debounced, departmentId, branchId, status, page],
    queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { search: debounced, departmentId, branchId, status, page, pageSize: 50 }),
    placeholderData: keepPreviousData,
  })
  const reset = <T,>(set: (v: T) => void) => (v: T) => { set(v); setPage(1) }
  // Same filters as the list; the API leaves out people and columns the user may not see.
  const exportList = () => download('/employees/export', `employees-${new Date().toISOString().slice(0, 10)}.xlsx`,
    { search: debounced, departmentId, branchId, status })
    .catch((e: Error) => notifications.show({ color: 'red', title: 'Not exported', message: e.message }))

  return (
    <>
      <PageHeader title="Employees" description={list.data ? `${list.data.totalCount} matching` : undefined}
        actions={
          <Group gap="xs">
            <Button variant="default" leftSection={<IconFileExport size={16} />} onClick={() => void exportList()}>Export</Button>
            {canAdd && <Button variant="default" leftSection={<IconFileImport size={16} />} onClick={importer.open}>Import</Button>}
            {canAdd && <Button leftSection={<IconPlus size={16} />} onClick={open}>New employee</Button>}
          </Group>
        } />
      <Group mb="md" align="flex-end" wrap="wrap">
        <TextInput placeholder="Name, number or work email" leftSection={<IconSearch size={16} />} w={280}
          value={search} onChange={(e) => reset(setSearch)(e.currentTarget.value)} />
        <Select placeholder="All departments" clearable searchable w={200} data={options(departments.data, (d) => d.name)}
          value={departmentId} onChange={reset(setDepartmentId)} />
        <Select placeholder="All branches" clearable w={180} data={options(branches.data, (b) => b.name)} value={branchId} onChange={reset(setBranchId)} />
        <Select placeholder="Any status" clearable w={150} data={['Active', 'Suspended', 'Exited']} value={status} onChange={reset(setStatus)} />
      </Group>
      {list.isPending ? <Loading /> : list.isError ? <LoadError error={list.error} /> : (
        <Stack>
          <Table.ScrollContainer minWidth={860}>
            <Table striped highlightOnHover>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>No.</Table.Th><Table.Th>Name</Table.Th><Table.Th>Position</Table.Th><Table.Th>Department</Table.Th>
                  <Table.Th>Branch</Table.Th><Table.Th>Manager</Table.Th><Table.Th>Hired</Table.Th><Table.Th>Status</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {list.data.items.length === 0 && (
                  <Table.Tr><Table.Td colSpan={8}><Text c="dimmed" ta="center" py="md">No employees match.</Text></Table.Td></Table.Tr>
                )}
                {list.data.items.map((e) => (
                  <Table.Tr key={e.id}>
                    <Table.Td><Text ff="monospace" size="sm">{e.employeeNumber}</Text></Table.Td>
                    <Table.Td><Text component={Link} to={`/employees/${e.id}`} fw={500} c="var(--mantine-primary-color-filled)">{e.fullName}</Text></Table.Td>
                    <Table.Td>{e.positionTitle ?? '—'}</Table.Td>
                    <Table.Td>{e.departmentName ?? '—'}</Table.Td>
                    <Table.Td>{e.branchName ?? '—'}</Table.Td>
                    <Table.Td>{e.managerName ?? '—'}</Table.Td>
                    <Table.Td>{date(e.hireDate)}</Table.Td>
                    <Table.Td><Badge variant="light" color={statusColor[e.status]}>{e.status}</Badge></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
          {list.data.totalPages > 1 && <Pagination value={page} onChange={setPage} total={list.data.totalPages} />}
        </Stack>
      )}
      <Modal opened={importing} onClose={importer.close} title="Import employees from Excel" size="lg">
        {importing && <ImportEmployees onDone={importer.close} />}
      </Modal>
      <Modal opened={opened} onClose={close} title="New employee" size="xl">
        {me?.company && <NewEmployeeForm country={me.company.country} onDone={close} />}
      </Modal>
    </>
  )
}

function NewEmployeeForm({ country, onDone }: { country: Country; onDone: () => void }) {
  const navigate = useNavigate()
  const canSetPay = useCan('employeesPay')
  const today = new Date().toISOString().slice(0, 10)
  const form = useForm({
    initialValues: {
      personal: { ...emptyPersonal, nationality: country.code },
      employment: { hireDate: today, workEmail: '', workPhone: '', branchId: null, departmentId: null, positionId: null, managerId: null },
      withContract: true,
      contract: { ...emptyContract, startDate: today },
      withSalary: canSetPay,
      salary: { effectiveDate: today, monthlyBasic: '' as number | '', reason: 'Starting salary' },
    },
    validate: {
      personal: personalValidation,
      employment: { hireDate: (v: string) => (v ? null : 'Required') },
      contract: { startDate: (v: string, all) => (all.withContract && !v ? 'Required' : null) },
      salary: { monthlyBasic: (v: number | '', all) => (all.withSalary && (v === '' || v <= 0) ? 'Enter the monthly basic salary' : null) },
    },
  })
  const create = useApiMutation((r: CreateEmployeeRequest) => api.post<Employee>('/employees', r), {
    invalidate: ['employees', 'org', 'directory'],
    success: (e) => `${e.fullName} added as ${e.employeeNumber}.`,
    onSuccess: (e) => { onDone(); void navigate(`/employees/${e.id}`) },
  })
  const v = form.getValues()

  return (
    <form onSubmit={form.onSubmit((v) => create.mutate({
      personal: cleanPersonal(v.personal),
      employment: cleanEmployment(v.employment),
      contract: v.withContract ? { ...v.contract, endDate: v.contract.endDate || null, probationEndDate: v.contract.probationEndDate || null } : null,
      salary: v.withSalary ? { ...v.salary, monthlyBasic: Number(v.salary.monthlyBasic) } : null,
    }))}>
      <Stack>
        <Text fw={600}>Personal</Text>
        <PersonalFields form={form} country={country} />
        <Divider />
        <Text fw={600}>Employment</Text>
        <EmploymentFields form={form} />
        <Divider />
        <Checkbox label="Add their first contract" {...form.getInputProps('withContract', { type: 'checkbox' })} />
        {v.withContract && <ContractFields form={form} path="contract" />}
        {canSetPay && <>
          <Checkbox label="Set their starting salary" {...form.getInputProps('withSalary', { type: 'checkbox' })} />
          {v.withSalary && (
            <Group grow align="flex-start">
              <NumberInput label="Monthly basic salary" required thousandSeparator="," min={0} decimalScale={country.currencyMinorUnits}
                prefix={`${country.currencyCode} `} {...form.getInputProps('salary.monthlyBasic')} />
              <DateInput label="Effective from" required valueFormat="D MMM YYYY" {...form.getInputProps('salary.effectiveDate')} />
            </Group>
          )}
        </>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onDone}>Cancel</Button>
          <Button type="submit" loading={create.isPending}>Add employee</Button>
        </Group>
      </Stack>
    </form>
  )
}
