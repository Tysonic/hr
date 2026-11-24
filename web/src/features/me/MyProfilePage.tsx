import { Alert, Button, Card, Group, Modal, SimpleGrid, Stack, Table, Text, Textarea } from '@mantine/core'
import { useForm } from '@mantine/form'
import { IconDownload, IconPencil, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { api, ApiError, download } from '../../api/client'
import type { Country, Employee, EmployeeDocument, NextOfKin, PaymentDetails, PersonalDetails, ProfileChange, SaveNextOfKinRequest } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { Link } from 'react-router'
import { useMyExit } from '../exits/components'
import { useMe } from '../../lib/permissions'
import { documentCategories } from '../employees/DocumentsTab'
import {
  cleanKin, cleanPayment, cleanPersonal, genders, idTypes, KinFields, kinInitial, kinValidation, label, orNull, PaymentFields, paymentMethods,
  PersonalFields, personalValidation,
} from '../employees/fields'
import { ChangeStatusBadge, ChangeTable } from './components'

type Editing = 'personal' | 'payment' | { kin: NextOfKin | null } | null

/** The employee's own record. Changes go to HR as requests; nothing here edits the record directly. */
export function MyProfilePage() {
  const { data: me } = useMe()
  const [editing, setEditing] = useState<Editing>(null)
  const profile = useQuery({
    queryKey: ['me', 'profile'], queryFn: () => api.get<Employee>('/me/profile'),
    retry: (count, e) => !(e instanceof ApiError && e.status === 400) && count < 2,
  })
  const changes = useQuery({ queryKey: ['me', 'changes'], queryFn: () => api.get<ProfileChange[]>('/me/changes'), enabled: profile.isSuccess })
  const documents = useQuery({ queryKey: ['me', 'documents'], queryFn: () => api.get<EmployeeDocument[]>('/me/documents'), enabled: profile.isSuccess })
  const withdraw = useApiMutation((id: string) => api.post<ProfileChange>(`/me/changes/${id}/withdraw`), { invalidate: ['me'], success: 'Request withdrawn.' })

  if (profile.isError && profile.error instanceof ApiError && profile.error.status === 400)
    return <><PageHeader title="My profile" /><Alert color="yellow" title="No employee record yet">{profile.error.message}</Alert></>
  if (profile.isPending || !me?.company) return <Loading />
  if (profile.isError) return <LoadError error={profile.error} />
  const e = profile.data
  const country = me.company.country
  const p = e.personal!
  const pay = e.payment!
  const pending = (changes.data ?? []).filter((c) => c.status === 'Pending')
  const waiting = (kind: ProfileChange['kind']) => pending.some((c) => c.kind === kind)
  const done = () => setEditing(null)

  return (
    <>
      <PageHeader title="My profile" description={`${e.fullName} · ${e.employeeNumber}. Changes you ask for go to HR, who update your record once they approve.`} />
      <Stack maw={900}>
        {pending.length > 0 && (
          <Alert color="yellow" title={`${pending.length} change${pending.length === 1 ? '' : 's'} waiting for HR`}>
            You'll get an email when HR decides. See the list at the bottom of the page.
          </Alert>
        )}

        <Section title="Employment" note="Kept by HR. Tell them if anything here is wrong.">
          <Field label="Position" value={e.employment.positionTitle} />
          <Field label="Department" value={e.employment.departmentName} />
          <Field label="Branch" value={e.employment.branchName} />
          <Field label="Manager" value={e.employment.managerName} />
          <Field label="Hired" value={date(e.employment.hireDate)} />
          <Field label="Work email" value={e.employment.workEmail} />
        </Section>

        <Section title="Personal details" action={
          <Button size="xs" variant="light" leftSection={<IconPencil size={14} />} disabled={waiting('Personal')} onClick={() => setEditing('personal')}>
            {waiting('Personal') ? 'Change waiting for HR' : 'Request a change'}
          </Button>}>
          <Field label="Name" value={e.fullName} />
          <Field label="Gender" value={label(genders, p.gender)} />
          <Field label="Date of birth" value={date(p.dateOfBirth)} />
          <Field label="Marital status" value={p.maritalStatus} />
          <Field label="Nationality" value={p.nationality} />
          <Field label={p.idType ? label(idTypes, p.idType) : 'ID'} value={p.idNumber} />
          <Field label={country.taxNumberLabel} value={p.taxNumber} />
          <Field label={country.socialSecurityLabel} value={p.socialSecurityNumber} />
          <Field label="Personal email" value={p.personalEmail} />
          <Field label="Personal phone" value={p.personalPhone} />
          <Field label="Home address" value={p.homeAddress} />
        </Section>

        <Section title="Payment details" note="Where your salary is paid." action={
          <Button size="xs" variant="light" leftSection={<IconPencil size={14} />} disabled={waiting('Payment')} onClick={() => setEditing('payment')}>
            {waiting('Payment') ? 'Change waiting for HR' : 'Request a change'}
          </Button>}>
          <Field label="Paid by" value={label(paymentMethods, pay.method)} />
          {pay.method === 'Bank' && <>
            <Field label="Bank" value={[pay.bankName, pay.bankBranch].filter(Boolean).join(', ')} />
            <Field label="Account number" value={pay.bankAccountNumber} />
            <Field label="Account name" value={pay.bankAccountName} />
          </>}
          {pay.method === 'MobileMoney' && <>
            <Field label="Provider" value={pay.mobileMoneyProvider} />
            <Field label="Number" value={pay.mobileMoneyNumber} />
          </>}
        </Section>

        <Card withBorder>
          <Group justify="space-between" mb="sm">
            <Text fw={600}>Next of kin</Text>
            <Button size="xs" variant="light" leftSection={<IconPlus size={14} />} onClick={() => setEditing({ kin: null })}>Add</Button>
          </Group>
          {(e.nextOfKin ?? []).length === 0 ? <Text size="sm" c="dimmed">None on record. Please add someone HR can contact in an emergency.</Text> : (
            <Table.ScrollContainer minWidth={520}>
              <Table>
                <Table.Tbody>
                  {e.nextOfKin!.map((k) => (
                    <Table.Tr key={k.id}>
                      <Table.Td><Text size="sm" fw={500}>{k.name}</Text><Text size="xs" c="dimmed">{k.relationship}</Text></Table.Td>
                      <Table.Td><Text size="sm">{k.phone}</Text>{k.email && <Text size="xs" c="dimmed">{k.email}</Text>}</Table.Td>
                      <Table.Td w={140} ta="right">
                        <Button size="xs" variant="subtle" onClick={() => setEditing({ kin: k })}>Request a change</Button>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          )}
        </Card>

        <Card withBorder>
          <Text fw={600} mb="sm">My documents</Text>
          {documents.isPending ? <Loading /> : documents.isError ? <LoadError error={documents.error} /> : documents.data.length === 0
            ? <Text size="sm" c="dimmed">HR hasn't filed any documents for you.</Text> : (
              <Table.ScrollContainer minWidth={480}>
                <Table>
                  <Table.Tbody>
                    {documents.data.map((d) => (
                      <Table.Tr key={d.id}>
                        <Table.Td><Text size="sm" fw={500}>{d.title}</Text><Text size="xs" c="dimmed">{label(documentCategories, d.category)}</Text></Table.Td>
                        <Table.Td><Text size="xs" c="dimmed">{d.expiresOn ? `Expires ${date(d.expiresOn)}` : ''}</Text></Table.Td>
                        <Table.Td w={60}>
                          <Button size="xs" variant="subtle" leftSection={<IconDownload size={14} />}
                            onClick={() => void download(`/me/documents/${d.id}`, d.fileName)}>Download</Button>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Table.ScrollContainer>
            )}
        </Card>

        <Card withBorder>
          <Text fw={600} mb="sm">My change requests</Text>
          {changes.isPending ? <Loading /> : changes.isError ? <LoadError error={changes.error} /> : changes.data.length === 0
            ? <Text size="sm" c="dimmed">None yet.</Text> : (
              <Stack>
                {changes.data.map((c) => (
                  <Card key={c.id} withBorder padding="sm">
                    <Group justify="space-between" mb="xs" wrap="wrap">
                      <Group gap="xs"><Text size="sm" fw={600}>{c.what}</Text><ChangeStatusBadge status={c.status} /></Group>
                      <Group gap="xs">
                        <Text size="xs" c="dimmed">Asked {dateTime(c.requestedAt)}</Text>
                        {c.canWithdraw && <Button size="xs" variant="subtle" color="red" loading={withdraw.isPending} onClick={() => withdraw.mutate(c.id)}>Withdraw</Button>}
                      </Group>
                    </Group>
                    <ChangeTable changes={c.changes} />
                    {c.decisionNote && <Text size="sm" mt="xs"><b>{c.status === 'Rejected' ? 'Why declined' : 'Note'}:</b> {c.decisionNote}</Text>}
                  </Card>
                ))}
              </Stack>
            )}
        </Card>

        <Leaving />
      </Stack>

      <Modal opened={editing !== null} onClose={done} size="xl" title={
        editing === 'personal' ? 'Change my personal details' : editing === 'payment' ? 'Change my payment details'
          : editing && editing.kin ? `Change ${editing.kin.name}` : 'Add a next of kin'}>
        {editing === 'personal' && <PersonalRequest employee={e} country={country} done={done} />}
        {editing === 'payment' && <PaymentRequest payment={pay} country={country} done={done} />}
        {editing && typeof editing === 'object' && <KinRequest kin={editing.kin} country={country} done={done} />}
      </Modal>
    </>
  )
}

function Section({ title, note, action, children }: { title: string; note?: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Card withBorder>
      <Group justify="space-between" mb="sm" wrap="wrap">
        <Stack gap={0}><Text fw={600}>{title}</Text>{note && <Text size="xs" c="dimmed">{note}</Text>}</Stack>
        {action}
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

/** Reason box and buttons shared by the request forms. */
function Submit({ reason, setReason, done, loading, payment = false }:
  { reason: string; setReason: (v: string) => void; done: () => void; loading: boolean; payment?: boolean }) {
  return (
    <Stack mt="md">
      <Textarea label="Reason (optional)" autosize minRows={1} value={reason} onChange={(e) => setReason(e.currentTarget.value)}
        placeholder="e.g. moved house, new phone number" />
      {payment && <Alert color="blue">For your security, HR checks payment changes and we email you, at every address on your record, once the change is made.</Alert>}
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button type="submit" loading={loading}>Send to HR</Button>
      </Group>
    </Stack>
  )
}

const useRequest = <T,>(path: string, done: () => void) =>
  useApiMutation((body: T) => api.post<ProfileChange>(`/me/changes/${path}`, body), {
    invalidate: ['me'], success: 'Sent to HR. You\'ll get an email when they decide.', onSuccess: done,
  })

function PersonalRequest({ employee: e, country, done }: { employee: Employee; country: Country; done: () => void }) {
  const p = e.personal!
  const [reason, setReason] = useState('')
  const form = useForm<{ personal: PersonalDetails }>({
    initialValues: { personal: { ...p, firstName: e.firstName, middleName: e.middleName, lastName: e.lastName, idType: p.idType ?? 'NationalId' } },
    validate: { personal: personalValidation },
  })
  const send = useRequest<{ details: PersonalDetails; reason: string | null }>('personal', done)
  return (
    <form onSubmit={form.onSubmit((v) => send.mutate({ details: cleanPersonal(v.personal), reason: orNull(reason) }))}>
      <PersonalFields form={form} country={country} />
      <Submit reason={reason} setReason={setReason} done={done} loading={send.isPending} />
    </form>
  )
}

function PaymentRequest({ payment, country, done }: { payment: PaymentDetails; country: Country; done: () => void }) {
  const [reason, setReason] = useState('')
  const form = useForm<PaymentDetails>({ initialValues: payment })
  const send = useRequest<{ details: PaymentDetails; reason: string | null }>('payment', done)
  return (
    <form onSubmit={form.onSubmit((v) => send.mutate({ details: cleanPayment(v), reason: orNull(reason) }))}>
      <PaymentFields form={form} country={country} />
      <Submit reason={reason} setReason={setReason} done={done} loading={send.isPending} payment />
    </form>
  )
}

function KinRequest({ kin, country, done }: { kin: NextOfKin | null; country: Country; done: () => void }) {
  const [reason, setReason] = useState('')
  const form = useForm<SaveNextOfKinRequest>({ initialValues: kinInitial(kin), validate: kinValidation })
  const send = useRequest<{ nextOfKinId: string | null; details: SaveNextOfKinRequest; reason: string | null }>('next-of-kin', done)
  return (
    <form onSubmit={form.onSubmit((v) => send.mutate({ nextOfKinId: kin?.id ?? null, details: cleanKin(v), reason: orNull(reason) }))}>
      <KinFields form={form} country={country} />
      <Submit reason={reason} setReason={setReason} done={done} loading={send.isPending} />
    </form>
  )
}

/** Where the employee hands in a resignation, or follows their exit once they have. */
function Leaving() {
  const exit = useMyExit()
  const x = exit.data
  return (
    <Card withBorder>
      <Group justify="space-between" wrap="wrap">
        <Stack gap={0}>
          <Text fw={600}>Leaving</Text>
          <Text size="sm" c="dimmed">{x ? (x.status === 'Requested' ? `Resignation handed in; waiting for HR.` : `Your last working day is ${date(x.lastWorkingDay)}.`)
            : 'Hand in your resignation here, after talking to your manager.'}</Text>
        </Stack>
        <Button variant={x ? 'filled' : 'default'} component={Link} to="/me/exit">{x ? 'Checklist and exit interview' : 'Resign'}</Button>
      </Group>
    </Card>
  )
}
