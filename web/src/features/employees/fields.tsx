import { Select, SimpleGrid, Stack, TextInput, Textarea } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import type { UseFormReturnType } from '@mantine/form'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type {
  ContractType, Country, EmployeeListItem, EmploymentDetails, Gender, IdType, MaritalStatus, NextOfKin, PagedResult, PaymentDetails, PaymentMethod,
  PersonalDetails, SaveContractRequest, SaveNextOfKinRequest,
} from '../../api/types'
import { options, useBranches, useDepartments, usePositions } from '../org/hooks'

// Form sections shared by "new employee" and the edit forms on an employee's record. Dates are "YYYY-MM-DD" strings,
// as the API sends them; empty strings become null when the request is built.

export const genders: { value: Gender; label: string }[] = [{ value: 'Female', label: 'Female' }, { value: 'Male', label: 'Male' }]
export const maritalStatuses: MaritalStatus[] = ['Single', 'Married', 'Divorced', 'Widowed']
export const idTypes: { value: IdType; label: string }[] = [
  { value: 'NationalId', label: 'National ID' }, { value: 'Passport', label: 'Passport' },
  { value: 'RefugeeId', label: 'Refugee ID' }, { value: 'Other', label: 'Other' },
]
export const contractTypes: { value: ContractType; label: string }[] = [
  { value: 'Permanent', label: 'Permanent' }, { value: 'FixedTerm', label: 'Fixed term' },
  { value: 'Casual', label: 'Casual' }, { value: 'Internship', label: 'Internship' },
]
export const paymentMethods: { value: PaymentMethod; label: string }[] = [
  { value: 'Bank', label: 'Bank transfer' }, { value: 'MobileMoney', label: 'Mobile money' }, { value: 'Cash', label: 'Cash' },
]
export const label = <T extends string>(list: { value: T; label: string }[], value: T | null | undefined) =>
  list.find((x) => x.value === value)?.label ?? value ?? '—'

/** Blank strings to null, trimmed otherwise. */
export const orNull = (v: string | null | undefined) => (v && v.trim() ? v.trim() : null)

export const emptyPersonal: PersonalDetails = {
  firstName: '', middleName: '', lastName: '', gender: 'Female', dateOfBirth: null, maritalStatus: null, nationality: '',
  idType: 'NationalId', idNumber: '', taxNumber: '', socialSecurityNumber: '', personalEmail: '', personalPhone: '', homeAddress: '',
}

export function cleanPersonal(p: PersonalDetails): PersonalDetails {
  const idNumber = orNull(p.idNumber)
  return {
    ...p,
    firstName: p.firstName.trim(), lastName: p.lastName.trim(), middleName: orNull(p.middleName),
    dateOfBirth: p.dateOfBirth || null, nationality: orNull(p.nationality)?.toUpperCase() ?? null,
    idType: idNumber ? p.idType : null, idNumber, taxNumber: orNull(p.taxNumber), socialSecurityNumber: orNull(p.socialSecurityNumber),
    personalEmail: orNull(p.personalEmail), personalPhone: orNull(p.personalPhone), homeAddress: orNull(p.homeAddress),
  }
}

export function cleanEmployment(e: EmploymentDetails): EmploymentDetails {
  return { ...e, workEmail: orNull(e.workEmail), workPhone: orNull(e.workPhone) }
}

export const personalValidation = {
  firstName: (v: string) => (v.trim() ? null : 'Required'),
  lastName: (v: string) => (v.trim() ? null : 'Required'),
  nationality: (v: string | null) => (!v || /^[A-Za-z]{2}$/.test(v.trim()) ? null : '2-letter country code, e.g. UG'),
}

export function PersonalFields<T extends { personal: PersonalDetails }>({ form, country, names = true }:
  { form: UseFormReturnType<T>; country: Country; names?: boolean }) {
  const f = (name: keyof PersonalDetails) => {
    const props = form.getInputProps(`personal.${name}`)
    return { ...props, value: props.value ?? '' }
  }
  const idType = form.getValues().personal.idType
  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }}>
      {names && <>
        <TextInput label="First name" required {...f('firstName')} />
        <TextInput label="Last name" required {...f('lastName')} />
        <TextInput label="Middle name" {...f('middleName')} />
      </>}
      <Select label="Gender" required data={genders} allowDeselect={false} {...f('gender')} />
      <DateInput label="Date of birth" valueFormat="D MMM YYYY" clearable {...form.getInputProps('personal.dateOfBirth')} />
      <Select label="Marital status" clearable data={maritalStatuses} {...f('maritalStatus')} value={form.getValues().personal.maritalStatus} />
      <TextInput label="Nationality" placeholder={country.code} maxLength={2} {...f('nationality')} />
      <Select label="ID type" data={idTypes} {...f('idType')} value={idType} />
      <TextInput label={idType === 'NationalId' ? country.nationalIdLabel : 'ID number'} {...f('idNumber')}
        description={idType === 'NationalId' ? `Checked against the ${country.name} format` : undefined} />
      <TextInput label={country.taxNumberLabel} {...f('taxNumber')} />
      <TextInput label={country.socialSecurityLabel} {...f('socialSecurityNumber')} />
      <TextInput label="Personal email" {...f('personalEmail')} />
      <TextInput label="Personal phone" placeholder={`${country.phonePrefix} …`} {...f('personalPhone')} />
      <Textarea label="Home address" autosize minRows={2} {...f('homeAddress')} />
    </SimpleGrid>
  )
}

export function EmploymentFields<T extends { employment: EmploymentDetails }>({ form, employeeId }:
  { form: UseFormReturnType<T>; employeeId?: string }) {
  const branches = useBranches()
  const departments = useDepartments()
  const positions = usePositions()
  const e = form.getValues().employment
  const managers = useQuery({
    queryKey: ['employees', 'managers'],
    queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { status: 'Active', pageSize: 200 }),
  })
  const positionOptions = options(positions.data?.filter((p) => !e.departmentId || p.departmentId === e.departmentId),
    (p) => e.departmentId ? p.title : `${p.title} (${p.departmentName})`, e.positionId)
  const text = (name: 'workEmail' | 'workPhone') => {
    const props = form.getInputProps(`employment.${name}`)
    return { ...props, value: props.value ?? '' }
  }

  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }}>
      <DateInput label="Hire date" required valueFormat="D MMM YYYY" {...form.getInputProps('employment.hireDate')} />
      <Select label="Branch" clearable searchable data={options(branches.data, (b) => b.name, e.branchId)} {...form.getInputProps('employment.branchId')} />
      <Select label="Department" clearable searchable data={options(departments.data, (d) => d.name, e.departmentId)}
        {...form.getInputProps('employment.departmentId')}
        onChange={(v) => {
          form.setFieldValue('employment.departmentId' as never, v as never)
          const position = positions.data?.find((p) => p.id === e.positionId)
          if (position && v && position.departmentId !== v) form.setFieldValue('employment.positionId' as never, null as never)
        }} />
      <Select label="Position" clearable searchable data={positionOptions} {...form.getInputProps('employment.positionId')}
        onChange={(v) => {
          form.setFieldValue('employment.positionId' as never, v as never)
          const position = positions.data?.find((p) => p.id === v)
          if (position && !e.departmentId) form.setFieldValue('employment.departmentId' as never, position.departmentId as never)
        }} />
      <Select label="Line manager" clearable searchable
        data={(managers.data?.items ?? []).filter((m) => m.id !== employeeId).map((m) => ({ value: m.id, label: `${m.fullName} (${m.employeeNumber})` }))}
        {...form.getInputProps('employment.managerId')} />
      <TextInput label="Work email" description="Also links the person's sign-in to this record" {...text('workEmail')} />
      <TextInput label="Work phone" {...text('workPhone')} />
    </SimpleGrid>
  )
}

export const emptyContract: SaveContractRequest = { type: 'Permanent', startDate: '', endDate: null, probationEndDate: null, notes: null }

export function ContractFields<T>({ form, path }: { form: UseFormReturnType<T>; path: string }) {
  const type = (form.getValues() as Record<string, unknown>)[path] as SaveContractRequest | undefined
  const p = (name: keyof SaveContractRequest) => `${path}.${name}`
  const needsEnd = type?.type === 'FixedTerm' || type?.type === 'Internship'
  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }}>
      <Select label="Contract type" required data={contractTypes} allowDeselect={false} {...form.getInputProps(p('type'))} />
      <DateInput label="Start date" required valueFormat="D MMM YYYY" {...form.getInputProps(p('startDate'))} />
      <DateInput label="End date" required={needsEnd} clearable valueFormat="D MMM YYYY" {...form.getInputProps(p('endDate'))} />
      <DateInput label="Probation ends" clearable valueFormat="D MMM YYYY" {...form.getInputProps(p('probationEndDate'))} />
      <Textarea label="Notes" autosize minRows={1} {...form.getInputProps(p('notes'))} value={type?.notes ?? ''} />
    </SimpleGrid>
  )
}

export function cleanPayment(v: PaymentDetails): PaymentDetails {
  return {
    method: v.method, bankName: orNull(v.bankName), bankBranch: orNull(v.bankBranch), bankAccountNumber: orNull(v.bankAccountNumber),
    bankAccountName: orNull(v.bankAccountName), mobileMoneyProvider: orNull(v.mobileMoneyProvider), mobileMoneyNumber: orNull(v.mobileMoneyNumber),
  }
}

export function PaymentFields({ form, country }: { form: UseFormReturnType<PaymentDetails>; country: Country }) {
  const v = form.getValues()
  const t = (name: keyof PaymentDetails) => ({ ...form.getInputProps(name), value: (form.getValues()[name] as string | null) ?? '' })
  return (
    <Stack>
      <Select label="Paid by" data={paymentMethods} allowDeselect={false} {...form.getInputProps('method')} />
      {v.method === 'Bank' && (
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="Bank" required {...t('bankName')} />
          <TextInput label="Branch" {...t('bankBranch')} />
          <TextInput label="Account number" required description="Stored encrypted" {...t('bankAccountNumber')} />
          <TextInput label="Account name" {...t('bankAccountName')} />
        </SimpleGrid>
      )}
      {v.method === 'MobileMoney' && (
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="Provider" required placeholder={country.code === 'KE' ? 'M-Pesa' : 'MTN MoMo'} {...t('mobileMoneyProvider')} />
          <TextInput label="Number" required placeholder={`${country.phonePrefix} …`} description="Stored encrypted" {...t('mobileMoneyNumber')} />
        </SimpleGrid>
      )}
    </Stack>
  )
}

export const kinInitial = (kin: NextOfKin | null): SaveNextOfKinRequest =>
  ({ name: kin?.name ?? '', relationship: kin?.relationship ?? '', phone: kin?.phone ?? '', email: kin?.email ?? '', address: kin?.address ?? '' })

export const kinValidation = {
  name: (v: string) => (v.trim() ? null : 'Required'),
  relationship: (v: string) => (v.trim() ? null : 'Required'),
  phone: (v: string) => (v.trim() ? null : 'Required'),
}

export const cleanKin = (v: SaveNextOfKinRequest): SaveNextOfKinRequest =>
  ({ name: v.name.trim(), relationship: v.relationship.trim(), phone: v.phone.trim(), email: orNull(v.email), address: orNull(v.address) })

export function KinFields({ form, country }: { form: UseFormReturnType<SaveNextOfKinRequest>; country: Country }) {
  const t = (name: keyof SaveNextOfKinRequest) => ({ ...form.getInputProps(name), value: form.getValues()[name] ?? '' })
  return (
    <SimpleGrid cols={{ base: 1, sm: 2 }}>
      <TextInput label="Name" required data-autofocus {...t('name')} />
      <TextInput label="Relationship" required placeholder="Spouse, parent…" {...t('relationship')} />
      <TextInput label="Phone" required placeholder={`${country.phonePrefix} …`} {...t('phone')} />
      <TextInput label="Email" {...t('email')} />
      <Textarea label="Address" autosize minRows={1} {...t('address')} />
    </SimpleGrid>
  )
}
