import {
  ActionIcon, Anchor, Badge, Button, Card, Group, Menu, Modal, NumberInput, SimpleGrid, Stack, Table, Tabs, Text, TextInput,
  Tooltip,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { modals } from '@mantine/modals'
import { IconAlertTriangle, IconArrowLeft, IconDots, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import type {
  AddSalaryRequest, Contract, Country, Employee, EmployeeStatus, NextOfKin, PaymentDetails, PersonalDetails, SaveContractRequest,
  SaveNextOfKinRequest,
} from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date, dateTime, money } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan, useMe } from '../../lib/permissions'
import { EmployeeAttendanceTab } from '../attendance/EmployeeAttendanceTab'
import { EmployeePayItems } from '../payroll/EmployeePayItems'
import { EmployeeLeaveTab } from '../leave/EmployeeLeaveTab'
import { EmployeeOnboarding } from '../onboarding/EmployeeOnboardingTab'
import { DocumentsTab } from './DocumentsTab'
import { statusColor } from './EmployeesPage'
import {
  cleanEmployment, cleanKin, cleanPayment, cleanPersonal, ContractFields, contractTypes, EmploymentFields, genders, idTypes, KinFields, kinInitial,
  kinValidation, label, orNull, PaymentFields, paymentMethods, PersonalFields, personalValidation,
} from './fields'

type Editing = 'personal' | 'employment' | 'payment' | 'salary' | { contract: Contract | null } | { kin: NextOfKin | null } | null

export function EmployeePage() {
  const { id } = useParams()
  const employee = useQuery({ queryKey: ['employees', id], queryFn: () => api.get<Employee>(`/employees/${id}`) })
  const { data: me } = useMe()
  if (employee.isPending || !me?.company) return <Loading />
  if (employee.isError) return <LoadError error={employee.error} />
  return <EmployeeRecord employee={employee.data} country={me.company.country} />
}

function EmployeeRecord({ employee: e, country }: { employee: Employee; country: Country }) {
  const [editing, setEditing] = useState<Editing>(null)
  const canSeeDocuments = useCan('documentsView')
  const canSeeAllLeave = useCan('leaveViewAll')
  const canApproveLeave = useCan('leaveApprove')
  const canSeeLeave = canSeeAllLeave || canApproveLeave
  const canSeeAttendance = useCan('attendanceView')
  const canSeePayItems = useCan('payrollSetup')
  const done = () => setEditing(null)
  const { access } = e
  const path = `/employees/${e.id}`
  const setStatus = useApiMutation((status: EmployeeStatus) => api.post<Employee>(`${path}/status`, { status }), {
    invalidate: ['employees', 'org', 'directory'],
    success: (r) => `${r.fullName} is now ${r.status.toLowerCase()}.`,
  })
  const confirmSuspend = () => modals.openConfirmModal({
    title: `Suspend ${e.fullName}?`,
    children: <Text size="sm">They stay on the books but are marked suspended until reactivated.</Text>,
    labels: { confirm: 'Suspend', cancel: 'Cancel' },
    confirmProps: { color: 'orange' },
    onConfirm: () => setStatus.mutate('Suspended'),
  })

  return (
    <>
      <Anchor component={Link} to="/employees" size="sm"><Group gap={4}><IconArrowLeft size={14} />Employees</Group></Anchor>
      <Group justify="space-between" align="flex-start" my="md" wrap="wrap">
        <Stack gap={2}>
          <Group gap="sm">
            <Text fz={26} fw={700}>{e.fullName}</Text>
            <Badge variant="light" color={statusColor[e.status]}>{e.status}</Badge>
          </Group>
          <Text c="dimmed" size="sm">
            {e.employeeNumber} · {[e.employment.positionTitle, e.employment.departmentName, e.employment.branchName].filter(Boolean).join(' · ') || 'No position set'}
          </Text>
        </Stack>
        {access.canEdit && e.status !== 'Exited' && (
          <Group>
            {e.status === 'Active'
              ? <Button variant="default" color="orange" onClick={confirmSuspend}>Suspend</Button>
              : <Button variant="default" onClick={() => setStatus.mutate('Active')}>Reactivate</Button>}
          </Group>
        )}
      </Group>

      <Tabs defaultValue="employment" keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="employment">Employment</Tabs.Tab>
          {access.canSeePersonal && <Tabs.Tab value="personal">Personal</Tabs.Tab>}
          <Tabs.Tab value="contracts">Contracts ({e.contracts.length})</Tabs.Tab>
          {access.canSeePay && <Tabs.Tab value="pay">Pay</Tabs.Tab>}
          {access.canSeePersonal && <Tabs.Tab value="kin">Next of kin ({e.nextOfKin?.length ?? 0})</Tabs.Tab>}
          {canSeeLeave && <Tabs.Tab value="leave">Leave</Tabs.Tab>}
          {canSeeAttendance && <Tabs.Tab value="attendance">Attendance</Tabs.Tab>}
          {canSeeDocuments && <Tabs.Tab value="documents">Documents</Tabs.Tab>}
          <Tabs.Tab value="onboarding">Onboarding</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="employment">
          <Section title="Employment" onEdit={access.canEdit ? () => setEditing('employment') : undefined}>
            <Field label="Hire date" value={date(e.employment.hireDate)} />
            <Field label="Branch" value={e.employment.branchName} />
            <Field label="Department" value={e.employment.departmentName} />
            <Field label="Position" value={e.employment.positionTitle} />
            <Field label="Job grade" value={e.employment.jobGradeCode} />
            <Field label="Line manager" value={e.employment.managerId
              ? <Anchor component={Link} to={`/employees/${e.employment.managerId}`}>{e.employment.managerName}</Anchor> : null} />
            <Field label="Work email" value={e.employment.workEmail} />
            <Field label="Work phone" value={e.employment.workPhone} />
          </Section>
        </Tabs.Panel>

        {e.personal && (
          <Tabs.Panel value="personal">
            <Section title="Personal details" onEdit={access.canEdit ? () => setEditing('personal') : undefined}>
              <Field label="Gender" value={label(genders, e.personal.gender)} />
              <Field label="Date of birth" value={date(e.personal.dateOfBirth)} />
              <Field label="Marital status" value={e.personal.maritalStatus} />
              <Field label="Nationality" value={e.personal.nationality} />
              <Field label={e.personal.idType === 'NationalId' ? country.nationalIdLabel : label(idTypes, e.personal.idType)} value={e.personal.idNumber} />
              <Field label={country.taxNumberLabel} value={e.personal.taxNumber} />
              <Field label={country.socialSecurityLabel} value={e.personal.socialSecurityNumber} />
              <Field label="Personal email" value={e.personal.personalEmail} />
              <Field label="Personal phone" value={e.personal.personalPhone} />
              <Field label="Home address" value={e.personal.homeAddress} />
            </Section>
          </Tabs.Panel>
        )}

        <Tabs.Panel value="contracts">
          <ContractsTab employee={e} onAdd={() => setEditing({ contract: null })} onEdit={(c) => setEditing({ contract: c })} />
        </Tabs.Panel>

        {access.canSeePay && (
          <Tabs.Panel value="pay">
            <Stack>
              <SalaryTab employee={e} country={country} onAdd={() => setEditing('salary')} />
              {canSeePayItems && <EmployeePayItems employeeId={e.id} />}
              {e.payment && (
                <Section title="Payment details" onEdit={access.canEditPay ? () => setEditing('payment') : undefined}>
                  <Field label="Paid by" value={label(paymentMethods, e.payment.method)} />
                  {e.payment.method === 'Bank' && <>
                    <Field label="Bank" value={e.payment.bankName} />
                    <Field label="Branch" value={e.payment.bankBranch} />
                    <Field label="Account number" value={e.payment.bankAccountNumber} />
                    <Field label="Account name" value={e.payment.bankAccountName} />
                  </>}
                  {e.payment.method === 'MobileMoney' && <>
                    <Field label="Provider" value={e.payment.mobileMoneyProvider} />
                    <Field label="Number" value={e.payment.mobileMoneyNumber} />
                  </>}
                </Section>
              )}
            </Stack>
          </Tabs.Panel>
        )}

        {e.nextOfKin && (
          <Tabs.Panel value="kin">
            <KinTab employee={e} onAdd={() => setEditing({ kin: null })} onEdit={(k) => setEditing({ kin: k })} />
          </Tabs.Panel>
        )}

        {canSeeLeave && <Tabs.Panel value="leave"><EmployeeLeaveTab employee={e} /></Tabs.Panel>}
        {canSeeAttendance && <Tabs.Panel value="attendance"><EmployeeAttendanceTab employee={e} /></Tabs.Panel>}
        {canSeeDocuments && <Tabs.Panel value="documents"><DocumentsTab employee={e} /></Tabs.Panel>}
        <Tabs.Panel value="onboarding"><EmployeeOnboarding employeeId={e.id} /></Tabs.Panel>
      </Tabs>

      <Modal opened={editing !== null} onClose={done} size="xl" title={
        editing === 'personal' ? 'Personal details' : editing === 'employment' ? 'Employment' : editing === 'payment' ? 'Payment details'
          : editing === 'salary' ? 'Salary change' : editing && 'contract' in editing ? (editing.contract ? 'Edit contract' : 'New contract')
            : editing && 'kin' in editing ? (editing.kin ? 'Edit next of kin' : 'New next of kin') : ''}>
        {editing === 'personal' && <PersonalForm employee={e} country={country} done={done} />}
        {editing === 'employment' && <EmploymentForm employee={e} done={done} />}
        {editing === 'payment' && <PaymentForm employee={e} country={country} done={done} />}
        {editing === 'salary' && <SalaryForm employee={e} country={country} done={done} />}
        {editing && typeof editing === 'object' && 'contract' in editing && <ContractForm employee={e} contract={editing.contract} done={done} />}
        {editing && typeof editing === 'object' && 'kin' in editing && <KinForm employee={e} kin={editing.kin} country={country} done={done} />}
      </Modal>
    </>
  )
}

function Section({ title, onEdit, children }: { title: string; onEdit?: () => void; children: ReactNode }) {
  return (
    <Card withBorder maw={900}>
      <Group justify="space-between" mb="sm">
        <Text fw={600}>{title}</Text>
        {onEdit && <Button size="xs" variant="light" onClick={onEdit}>Edit</Button>}
      </Group>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="sm" verticalSpacing="xs">{children}</SimpleGrid>
    </Card>
  )
}

function Field({ label, value }: { label: string; value: ReactNode }) {
  return (
    <Stack gap={0}>
      <Text size="xs" c="dimmed">{label}</Text>
      <Text size="sm">{value === null || value === undefined || value === '' ? '—' : value}</Text>
    </Stack>
  )
}

/** Saves one section; every section's endpoint returns the whole updated employee. */
function useSave<R>(employee: Employee, method: 'put' | 'post' | 'delete', path: string, success: string, done?: () => void) {
  return useApiMutation((r: R) => method === 'delete' ? api.delete<Employee>(`/employees/${employee.id}${path}`)
    : api[method]<Employee>(`/employees/${employee.id}${path}`, r), {
    invalidate: ['employees', 'org', 'directory'],
    success,
    onSuccess: done,
  })
}

function Buttons({ done, loading }: { done: () => void; loading: boolean }) {
  return (
    <Group justify="flex-end" mt="md">
      <Button variant="default" onClick={done}>Cancel</Button>
      <Button type="submit" loading={loading}>Save</Button>
    </Group>
  )
}

// Edit forms

function PersonalForm({ employee: e, country, done }: { employee: Employee; country: Country; done: () => void }) {
  const p = e.personal!
  const form = useForm<{ personal: PersonalDetails }>({
    initialValues: { personal: { ...p, firstName: e.firstName, middleName: e.middleName, lastName: e.lastName, idType: p.idType ?? 'NationalId' } },
    validate: { personal: personalValidation },
  })
  const save = useSave<PersonalDetails>(e, 'put', '/personal', 'Personal details saved.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate(cleanPersonal(v.personal)))}>
      <PersonalFields form={form} country={country} />
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}

function EmploymentForm({ employee: e, done }: { employee: Employee; done: () => void }) {
  const { hireDate, workEmail, workPhone, branchId, departmentId, positionId, managerId } = e.employment
  const form = useForm({ initialValues: { employment: { hireDate, workEmail, workPhone, branchId, departmentId, positionId, managerId } } })
  const save = useSave<typeof form.values.employment>(e, 'put', '/employment', 'Employment saved.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate(cleanEmployment(v.employment)))}>
      <EmploymentFields form={form} employeeId={e.id} />
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}

function PaymentForm({ employee: e, country, done }: { employee: Employee; country: Country; done: () => void }) {
  const form = useForm<PaymentDetails>({ initialValues: e.payment! })
  const save = useSave<PaymentDetails>(e, 'put', '/payment', 'Payment details saved.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate(cleanPayment(v)))}>
      <PaymentFields form={form} country={country} />
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}

// Contracts

function ContractsTab({ employee: e, onAdd, onEdit }: { employee: Employee; onAdd: () => void; onEdit: (c: Contract) => void }) {
  const confirm = useApiMutation(({ c, on }: { c: Contract; on: string }) =>
    api.post<Employee>(`/employees/${e.id}/contracts/${c.id}/confirm`, { confirmedOn: on }), {
    invalidate: ['employees'], success: `${e.fullName} confirmed.`,
  })
  return (
    <Stack maw={1000}>
      {e.access.canEdit && <Group justify="flex-end"><Button leftSection={<IconPlus size={16} />} onClick={onAdd}>New contract</Button></Group>}
      <Table.ScrollContainer minWidth={720}>
        <Table striped>
          <Table.Thead>
            <Table.Tr><Table.Th>Type</Table.Th><Table.Th>Starts</Table.Th><Table.Th>Ends</Table.Th><Table.Th>Probation</Table.Th><Table.Th>Notes</Table.Th><Table.Th /></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {e.contracts.length === 0 && <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="md">No contracts recorded.</Text></Table.Td></Table.Tr>}
            {e.contracts.map((c) => (
              <Table.Tr key={c.id}>
                <Table.Td><Group gap="xs">{label(contractTypes, c.type)}{c.isCurrent && <Badge size="sm" variant="light">Current</Badge>}</Group></Table.Td>
                <Table.Td>{date(c.startDate)}</Table.Td>
                <Table.Td>{c.endDate ? date(c.endDate) : 'Open-ended'}</Table.Td>
                <Table.Td>{!c.probationEndDate ? '—' : c.confirmedOn ? `Confirmed ${date(c.confirmedOn)}` : `Until ${date(c.probationEndDate)}`}</Table.Td>
                <Table.Td>{c.notes ?? '—'}</Table.Td>
                <Table.Td w={40}>
                  {e.access.canEdit && (
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label="Contract actions"><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item onClick={() => onEdit(c)}>Edit</Menu.Item>
                        {c.probationEndDate && !c.confirmedOn && (
                          <Menu.Item onClick={() => confirm.mutate({ c, on: new Date().toISOString().slice(0, 10) })}>Confirm today</Menu.Item>
                        )}
                      </Menu.Dropdown>
                    </Menu>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Stack>
  )
}

function ContractForm({ employee: e, contract, done }: { employee: Employee; contract: Contract | null; done: () => void }) {
  const form = useForm<{ contract: SaveContractRequest }>({
    initialValues: { contract: contract
      ? { type: contract.type, startDate: contract.startDate, endDate: contract.endDate, probationEndDate: contract.probationEndDate, notes: contract.notes }
      : { type: 'Permanent', startDate: '', endDate: null, probationEndDate: null, notes: null } },
    validate: { contract: { startDate: (v: string) => (v ? null : 'Required') } },
  })
  const save = useSave<SaveContractRequest>(e, contract ? 'put' : 'post', contract ? `/contracts/${contract.id}` : '/contracts', 'Contract saved.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({
      ...v.contract, endDate: v.contract.endDate || null, probationEndDate: v.contract.probationEndDate || null, notes: orNull(v.contract.notes),
    }))}>
      <ContractFields form={form} path="contract" />
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}

// Salary

function SalaryTab({ employee: e, country, onAdd }: { employee: Employee; country: Country; onAdd: () => void }) {
  const remove = useApiMutation((salaryId: string) => api.delete<Employee>(`/employees/${e.id}/salaries/${salaryId}`), {
    invalidate: ['employees'], success: 'Salary change removed.',
  })
  const today = new Date().toISOString().slice(0, 10)
  return (
    <Card withBorder maw={1000}>
      <Group justify="space-between" mb="sm">
        <Text fw={600}>Salary history</Text>
        {e.access.canEditPay && <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={onAdd}>Salary change</Button>}
      </Group>
      <Table.ScrollContainer minWidth={640}>
        <Table>
          <Table.Thead>
            <Table.Tr><Table.Th>Effective</Table.Th><Table.Th ta="right">Monthly basic</Table.Th><Table.Th>Reason</Table.Th><Table.Th>Recorded</Table.Th><Table.Th /></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {e.salaries!.length === 0 && <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" ta="center" py="md">No salary recorded.</Text></Table.Td></Table.Tr>}
            {e.salaries!.map((s) => (
              <Table.Tr key={s.id} fw={s.isCurrent ? 600 : undefined}>
                <Table.Td>
                  <Group gap="xs">{date(s.effectiveDate)}
                    {s.isCurrent && <Badge size="sm" variant="light">Current</Badge>}
                    {s.effectiveDate > today && <Badge size="sm" variant="light" color="blue">Upcoming</Badge>}
                  </Group>
                </Table.Td>
                <Table.Td ta="right">
                  <Group gap={4} justify="flex-end" wrap="nowrap">
                    {s.outsideGrade && (
                      <Tooltip label={`Outside the ${e.employment.jobGradeCode} grade range`}><IconAlertTriangle size={16} color="orange" /></Tooltip>
                    )}
                    {money(s.monthlyBasic, country)}
                  </Group>
                </Table.Td>
                <Table.Td>{s.reason}</Table.Td>
                <Table.Td><Text size="xs" c="dimmed">{s.recordedBy}, {dateTime(s.recordedAt)}</Text></Table.Td>
                <Table.Td w={40}>
                  {e.access.canEditPay && s.effectiveDate > today && (
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label="Salary actions"><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown><Menu.Item color="red" onClick={() => remove.mutate(s.id)}>Remove (not yet effective)</Menu.Item></Menu.Dropdown>
                    </Menu>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Card>
  )
}

function SalaryForm({ employee: e, country, done }: { employee: Employee; country: Country; done: () => void }) {
  const form = useForm({
    initialValues: { effectiveDate: new Date().toISOString().slice(0, 10), monthlyBasic: '' as number | '', reason: '' },
    validate: {
      effectiveDate: (v: string) => (v ? null : 'Required'),
      monthlyBasic: (v: number | '') => (v === '' || v <= 0 ? 'Enter the new monthly basic salary' : null),
      reason: (v: string) => (v.trim() ? null : 'Say why, e.g. promotion or annual review'),
    },
  })
  const save = useSave<AddSalaryRequest>(e, 'post', '/salaries', 'Salary change recorded.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ effectiveDate: v.effectiveDate, monthlyBasic: Number(v.monthlyBasic), reason: v.reason.trim() }))}>
      <Stack>
        <Text size="sm" c="dimmed">Salary changes are kept as history. One that hasn't taken effect yet can be removed to correct it.</Text>
        <Group grow align="flex-start">
          <NumberInput label="Monthly basic salary" required thousandSeparator="," min={0} decimalScale={country.currencyMinorUnits}
            prefix={`${country.currencyCode} `} data-autofocus {...form.getInputProps('monthlyBasic')} />
          <DateInput label="Effective from" required valueFormat="D MMM YYYY" {...form.getInputProps('effectiveDate')} />
        </Group>
        <TextInput label="Reason" required placeholder="Promotion, annual review…" {...form.getInputProps('reason')} />
      </Stack>
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}

// Next of kin

function KinTab({ employee: e, onAdd, onEdit }: { employee: Employee; onAdd: () => void; onEdit: (k: NextOfKin) => void }) {
  const remove = useApiMutation((k: NextOfKin) => api.delete<Employee>(`/employees/${e.id}/next-of-kin/${k.id}`), {
    invalidate: ['employees'], success: (_, k) => `${k.name} removed.`,
  })
  return (
    <Stack maw={1000}>
      {e.access.canEdit && <Group justify="flex-end"><Button leftSection={<IconPlus size={16} />} onClick={onAdd}>Add next of kin</Button></Group>}
      {e.nextOfKin!.length === 0 && <Text c="dimmed">None recorded.</Text>}
      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        {e.nextOfKin!.map((k) => (
          <Card key={k.id} withBorder padding="sm">
            <Group justify="space-between" align="flex-start">
              <Stack gap={2}>
                <Text fw={600}>{k.name}</Text>
                <Text size="sm" c="dimmed">{k.relationship}</Text>
                <Text size="sm">{k.phone}</Text>
                {k.email && <Text size="sm">{k.email}</Text>}
                {k.address && <Text size="sm" c="dimmed">{k.address}</Text>}
              </Stack>
              {e.access.canEdit && (
                <Menu position="bottom-end">
                  <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${k.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                  <Menu.Dropdown>
                    <Menu.Item onClick={() => onEdit(k)}>Edit</Menu.Item>
                    <Menu.Item color="red" onClick={() => remove.mutate(k)}>Remove</Menu.Item>
                  </Menu.Dropdown>
                </Menu>
              )}
            </Group>
          </Card>
        ))}
      </SimpleGrid>
    </Stack>
  )
}

function KinForm({ employee: e, kin, country, done }: { employee: Employee; kin: NextOfKin | null; country: Country; done: () => void }) {
  const form = useForm<SaveNextOfKinRequest>({ initialValues: kinInitial(kin), validate: kinValidation })
  const save = useSave<SaveNextOfKinRequest>(e, kin ? 'put' : 'post', kin ? `/next-of-kin/${kin.id}` : '/next-of-kin', 'Next of kin saved.', done)
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate(cleanKin(v)))}>
      <KinFields form={form} country={country} />
      <Buttons done={done} loading={save.isPending} />
    </form>
  )
}
