import {
  Alert, Badge, Button, Card, Checkbox, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Table, TagsInput, Text, Textarea, TextInput,
  MultiSelect,
} from '@mantine/core'
import { DateInput, DateTimePicker } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconCalendarPlus, IconDownload, IconFileText } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { api, download } from '../../api/client'
import type {
  EmployeeListItem, HireRequest, HireResult, Interview, JobApplication, Offer, OfferStatus, OfferTerms, PagedResult, Recommendation,
  ScheduleInterviewRequest,
} from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { contractTypes, genders, label, orNull } from '../employees/fields'
import { StartOnboardingFields } from '../onboarding/components'
import { useMoney } from '../payroll/components'

export const recommendationLabel: Record<Recommendation, string> = { StrongNo: 'Strong no', No: 'No', Yes: 'Yes', StrongYes: 'Strong yes' }
export const recommendationColor: Record<Recommendation, string> = { StrongNo: 'red', No: 'orange', Yes: 'teal', StrongYes: 'green' }
const offerColor: Record<OfferStatus, string> = { Draft: 'gray', Sent: 'blue', Accepted: 'green', Declined: 'red', Withdrawn: 'dark' }

/** "Tue 24 Nov, 10:00" from a company-local "YYYY-MM-DDTHH:mm:ss". */
export const when = (local: string) =>
  new Date(local).toLocaleString('en-GB', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

const useActiveEmployees = () => useQuery({
  queryKey: ['employees', 'managers'],
  queryFn: () => api.get<PagedResult<EmployeeListItem>>('/employees', { status: 'Active', pageSize: 200 }),
})

// Interviews

export function InterviewsSection({ app }: { app: JobApplication }) {
  const [editing, setEditing] = useState<Interview | 'new' | null>(null)
  const interviews = useQuery({ queryKey: ['recruitment', 'interviews', app.id], queryFn: () => api.get<Interview[]>(`/recruitment/applications/${app.id}/interviews`) })
  const cancel = useApiMutation((i: Interview) => api.post<Interview>(`/recruitment/interviews/${i.id}/cancel`), { invalidate: ['recruitment'], success: 'Interview cancelled.' })
  const closed = ['Hired', 'Rejected', 'Withdrawn'].includes(app.stage)
  return (
    <Stack gap="xs">
      <Group justify="space-between">
        <Text fw={600} size="sm">Interviews</Text>
        {!closed && <Button size="xs" variant="light" leftSection={<IconCalendarPlus size={14} />} onClick={() => setEditing('new')}>Schedule</Button>}
      </Group>
      {interviews.isPending ? <Loading /> : interviews.isError ? <LoadError error={interviews.error} /> : interviews.data.length === 0
        ? <Text size="sm" c="dimmed">None yet.</Text>
        : interviews.data.map((i) => (
          <Card key={i.id} withBorder padding="sm">
            <Group justify="space-between" wrap="nowrap">
              <Stack gap={0}>
                <Text size="sm" fw={500}>{when(i.localStart)} · {i.durationMinutes} min</Text>
                <Text size="xs" c="dimmed">{[i.location, i.panel.map((p) => p.name + (p.submitted ? ' ✓' : '')).join(', ')].filter(Boolean).join(' · ')}</Text>
              </Stack>
              <Group gap={4}>
                <Badge variant="light" color={i.status === 'Completed' ? 'green' : i.status === 'Cancelled' ? 'gray' : 'blue'}>{i.status}</Badge>
                {i.status === 'Scheduled' && <Button size="compact-xs" variant="subtle" onClick={() => setEditing(i)}>Move</Button>}
                {i.status === 'Scheduled' && <Button size="compact-xs" variant="subtle" color="red" onClick={() => cancel.mutate(i)}>Cancel</Button>}
              </Group>
            </Group>
            {i.scorecards.length > 0 && <Scorecards interview={i} />}
          </Card>
        ))}
      <Modal opened={editing !== null} onClose={() => setEditing(null)} size="lg" title={editing === 'new' ? `Interview ${app.firstName} ${app.lastName}` : 'Move interview'}>
        {editing && <InterviewForm app={app} interview={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </Stack>
  )
}

/** Each panellist's marks: average, recommendation and comments, with per-criterion scores. */
export function Scorecards({ interview: i }: { interview: Interview }) {
  return (
    <Table.ScrollContainer minWidth={420} mt="xs">
      <Table withTableBorder fz="xs">
        <Table.Thead>
          <Table.Tr><Table.Th>Interviewer</Table.Th>{i.criteria.map((c) => <Table.Th key={c} ta="center">{c}</Table.Th>)}<Table.Th ta="center">Average</Table.Th><Table.Th>Verdict</Table.Th></Table.Tr>
        </Table.Thead>
        <Table.Tbody>
          {i.scorecards.map((s) => (
            <Table.Tr key={s.id} title={s.comments ?? undefined}>
              <Table.Td>{s.interviewerName}</Table.Td>
              {i.criteria.map((c) => <Table.Td key={c} ta="center">{s.scores.find((x) => x.criterion === c)?.score ?? '—'}</Table.Td>)}
              <Table.Td ta="center" fw={600}>{s.average.toFixed(1)}</Table.Td>
              <Table.Td><Badge size="xs" variant="light" color={recommendationColor[s.recommendation]}>{recommendationLabel[s.recommendation]}</Badge></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      {i.scorecards.filter((s) => s.comments).map((s) => <Text key={s.id} size="xs" mt={4}><b>{s.interviewerName}:</b> {s.comments}</Text>)}
    </Table.ScrollContainer>
  )
}

function InterviewForm({ app, interview: i, done }: { app: JobApplication; interview: Interview | null; done: () => void }) {
  const employees = useActiveEmployees()
  const form = useForm({
    initialValues: {
      localStart: i ? i.localStart.replace('T', ' ') : null as string | null,
      durationMinutes: i?.durationMinutes ?? 45,
      location: i?.location ?? '',
      panelIds: i?.panel.map((p) => p.employeeId) ?? [],
      criteria: i?.criteria ?? ['Skills for the job', 'Experience', 'Communication', 'Fit with the team'],
      emailCandidate: !i,
    },
    validate: {
      localStart: (v: string | null) => (v ? null : 'Pick a date and time'),
      panelIds: (v: string[]) => (v.length ? null : 'Add at least one interviewer'),
      criteria: (v: string[]) => (v.length ? null : 'Add at least one thing to score'),
    },
  })
  const save = useApiMutation((r: ScheduleInterviewRequest) => i
    ? api.put<Interview>(`/recruitment/interviews/${i.id}`, r) : api.post<Interview>(`/recruitment/applications/${app.id}/interviews`, r), {
    invalidate: ['recruitment'], success: 'Saved. The panel has been emailed an invite.', onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ ...v, localStart: v.localStart!.replace(' ', 'T'), location: orNull(v.location) }))}>
      <Stack>
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <DateTimePicker label="When" required valueFormat="ddd D MMM YYYY, HH:mm" {...form.getInputProps('localStart')} />
          <NumberInput label="Minutes" min={10} max={480} step={15} {...form.getInputProps('durationMinutes')} />
        </SimpleGrid>
        <TextInput label="Where" placeholder="Room, address or video link" {...form.getInputProps('location')} />
        <MultiSelect label="Panel" required searchable description="They get an invite and send a scorecard from TyHR. They need a work email."
          data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: e.fullName, disabled: !e.workEmail }))} {...form.getInputProps('panelIds')} />
        <TagsInput label="Scored on" description="Each interviewer marks each of these from 1 to 5. Press Enter to add." {...form.getInputProps('criteria')} />
        <Checkbox label={`Email ${app.firstName} the invite at ${app.email}`} {...form.getInputProps('emailCandidate', { type: 'checkbox' })} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>{i ? 'Move' : 'Schedule'}</Button>
        </Group>
      </Stack>
    </form>
  )
}

// Offers and hiring

export function OffersSection({ app }: { app: JobApplication }) {
  const m = useMoney()
  const [editing, setEditing] = useState<Offer | 'new' | null>(null)
  const [sending, setSending] = useState<Offer | null>(null)
  const [answer, setAnswer] = useState<{ offer: Offer; kind: 'accept' | 'decline' | 'withdraw' } | null>(null)
  const [note, setNote] = useState('')
  const [hiring, setHiring] = useState(false)
  const offers = useQuery({ queryKey: ['recruitment', 'offers', app.id], queryFn: () => api.get<Offer[]>(`/recruitment/applications/${app.id}/offers`) })
  const respond = useApiMutation(() => answer!.kind === 'withdraw'
    ? api.post<Offer>(`/recruitment/offers/${answer!.offer.id}/withdraw`, { note: orNull(note) })
    : api.post<Offer>(`/recruitment/offers/${answer!.offer.id}/respond`, { accepted: answer!.kind === 'accept', note: orNull(note) }), {
    invalidate: ['recruitment'], success: 'Recorded.', onSuccess: () => setAnswer(null),
  })
  if (offers.isPending) return <Loading />
  if (offers.isError) return <LoadError error={offers.error} />
  const live = offers.data.find((o) => ['Draft', 'Sent', 'Accepted'].includes(o.status))
  const closed = ['Hired', 'Rejected', 'Withdrawn'].includes(app.stage)
  return (
    <Stack gap="xs">
      <Group justify="space-between">
        <Text fw={600} size="sm">Offer</Text>
        {!closed && !live && <Button size="xs" variant="light" onClick={() => setEditing('new')}>Make an offer</Button>}
      </Group>
      {offers.data.length === 0 && <Text size="sm" c="dimmed">No offer yet.</Text>}
      {offers.data.map((o) => (
        <Card key={o.id} withBorder padding="sm">
          <Group justify="space-between" wrap="wrap">
            <Stack gap={0}>
              <Text size="sm" fw={500}>{o.jobTitle} · {m(o.monthlySalary)} a month</Text>
              <Text size="xs" c="dimmed">
                {label(contractTypes, o.contractType)} from {date(o.startDate)}{o.contractEndDate && ` to ${date(o.contractEndDate)}`}
                {o.probationMonths > 0 && ` · ${o.probationMonths} months' probation`} · reply by {date(o.respondBy)}
              </Text>
            </Stack>
            <Badge variant="light" color={offerColor[o.status]}>{o.status}</Badge>
          </Group>
          {o.warnings.map((w) => <Text key={w} size="xs" c="orange" mt={4}>{w}</Text>)}
          {o.responseNote && <Text size="xs" mt={4}>Note: {o.responseNote}</Text>}
          <Group gap="xs" mt="xs">
            <Button size="compact-xs" variant="default" leftSection={<IconDownload size={12} />}
              onClick={() => void download(`/recruitment/offers/${o.id}/letter`, `offer-${app.lastName}.pdf`)}>Letter</Button>
            {o.status === 'Draft' && <Button size="compact-xs" variant="default" onClick={() => setEditing(o)}>Edit</Button>}
            {o.status === 'Draft' && <Button size="compact-xs" onClick={() => setSending(o)}>Send</Button>}
            {o.status === 'Sent' && <Button size="compact-xs" color="green" onClick={() => { setNote(''); setAnswer({ offer: o, kind: 'accept' }) }}>Accepted</Button>}
            {o.status === 'Sent' && <Button size="compact-xs" variant="default" onClick={() => { setNote(''); setAnswer({ offer: o, kind: 'decline' }) }}>Declined</Button>}
            {(o.status === 'Draft' || o.status === 'Sent') && <Button size="compact-xs" variant="subtle" color="red" onClick={() => { setNote(''); setAnswer({ offer: o, kind: 'withdraw' }) }}>Withdraw</Button>}
            {o.status === 'Accepted' && app.stage === 'Offer' && <Button size="compact-xs" color="green" onClick={() => setHiring(true)}>Make employee</Button>}
          </Group>
        </Card>
      ))}
      <Modal opened={editing !== null} onClose={() => setEditing(null)} size="lg" title={editing === 'new' ? 'Make an offer' : 'Edit offer'}>
        {editing && <OfferForm app={app} offer={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
      <Modal opened={!!sending} onClose={() => setSending(null)} title="Send the offer">
        {sending && <SendOffer offer={sending} app={app} done={() => setSending(null)} />}
      </Modal>
      <Modal opened={!!answer} onClose={() => setAnswer(null)}
        title={answer?.kind === 'accept' ? 'Offer accepted' : answer?.kind === 'decline' ? 'Offer declined' : 'Withdraw offer'}>
        <Textarea label="Note (optional)" autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} data-autofocus
          placeholder={answer?.kind === 'accept' ? 'e.g. signed letter received 25 Nov' : undefined} />
        <Group justify="flex-end" mt="md">
          <Button variant="default" onClick={() => setAnswer(null)}>Cancel</Button>
          <Button color={answer?.kind === 'accept' ? 'green' : 'red'} loading={respond.isPending} onClick={() => respond.mutate(undefined)}>Save</Button>
        </Group>
      </Modal>
      <Modal opened={hiring} onClose={() => setHiring(false)} size="lg" title={`Make ${app.firstName} ${app.lastName} an employee`}>
        {hiring && <HireForm app={app} done={() => setHiring(false)} />}
      </Modal>
    </Stack>
  )
}

function OfferForm({ app, offer: o, done }: { app: JobApplication; offer: Offer | null; done: () => void }) {
  const form = useForm<OfferTerms>({
    initialValues: o ?? {
      jobTitle: app.postingTitle, startDate: '', monthlySalary: 0, contractType: 'Permanent', contractEndDate: null, probationMonths: 6, respondBy: '', extraTerms: '',
    },
    validate: {
      jobTitle: (v) => (v.trim() ? null : 'Required'), startDate: (v) => (v ? null : 'Required'), respondBy: (v) => (v ? null : 'Required'),
      monthlySalary: (v) => (Number(v) > 0 ? null : 'Required'),
    },
  })
  const save = useApiMutation((t: OfferTerms) => o ? api.put<Offer>(`/recruitment/offers/${o.id}`, t) : api.post<Offer>(`/recruitment/applications/${app.id}/offers`, t), {
    invalidate: ['recruitment'], success: 'Saved as a draft. Download the letter to check it, then send it.', onSuccess: done,
  })
  const v = form.getValues()
  return (
    <form onSubmit={form.onSubmit((t) => save.mutate({ ...t, jobTitle: t.jobTitle.trim(), monthlySalary: Number(t.monthlySalary),
      contractEndDate: t.contractType === 'Permanent' ? null : t.contractEndDate || null, extraTerms: orNull(t.extraTerms) }))}>
      <Stack>
        <TextInput label="Job title" required {...form.getInputProps('jobTitle')} />
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <NumberInput label="Monthly gross salary" required thousandSeparator="," min={0} {...form.getInputProps('monthlySalary')} />
          <Select label="Contract" data={contractTypes} allowDeselect={false} {...form.getInputProps('contractType')} />
          <DateInput label="Start date" required valueFormat="D MMM YYYY" {...form.getInputProps('startDate')} />
          {v.contractType !== 'Permanent'
            ? <DateInput label="Contract ends" required valueFormat="D MMM YYYY" {...form.getInputProps('contractEndDate')} />
            : <div />}
          <NumberInput label="Probation (months)" min={0} max={12} {...form.getInputProps('probationMonths')} />
          <DateInput label="Reply by" required valueFormat="D MMM YYYY" {...form.getInputProps('respondBy')} />
        </SimpleGrid>
        <Textarea label="Extra terms (optional)" description="Added to the end of the letter, e.g. allowances or benefits." autosize minRows={2}
          {...form.getInputProps('extraTerms')} value={v.extraTerms ?? ''} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>Save draft</Button>
        </Group>
      </Stack>
    </form>
  )
}

function SendOffer({ offer, app, done }: { offer: Offer; app: JobApplication; done: () => void }) {
  const [emailIt, setEmailIt] = useState(true)
  const send = useApiMutation(() => api.post<Offer>(`/recruitment/offers/${offer.id}/send`, { emailCandidate: emailIt }), {
    invalidate: ['recruitment'], success: emailIt ? `Offer emailed to ${app.email}.` : 'Marked as sent.', onSuccess: done,
  })
  return (
    <Stack>
      <Button variant="default" leftSection={<IconFileText size={16} />} onClick={() => void download(`/recruitment/offers/${offer.id}/letter`, `offer-${app.lastName}.pdf`)}>
        Check the letter first
      </Button>
      <Checkbox checked={emailIt} onChange={(e) => setEmailIt(e.currentTarget.checked)} label={`Email the letter to ${app.email}`}
        description="Untick if you're handing it over or sending it yourself." />
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button loading={send.isPending} onClick={() => send.mutate(undefined)}>{emailIt ? 'Email offer' : 'Mark as sent'}</Button>
      </Group>
    </Stack>
  )
}

function HireForm({ app, done }: { app: JobApplication; done: () => void }) {
  const navigate = useNavigate()
  const employees = useActiveEmployees()
  const form = useForm<HireRequest>({ initialValues: { gender: 'Female', dateOfBirth: null, nationality: '', workEmail: '', managerId: null, onboarding: { templateId: null, itContactId: null } } })
  const hire = useApiMutation((r: HireRequest) => api.post<HireResult>(`/recruitment/applications/${app.id}/hire`, r), {
    invalidate: ['recruitment', 'employees'],
    success: (x) => [`${app.firstName} is now ${x.employeeNumber}.`, !x.salaryRecorded && 'Ask payroll to add their salary.',
      x.requisitionFilled && 'All openings are filled, so the requisition and its adverts are closed.',
      x.onboardingTasks > 0 && `${x.onboardingTasks} onboarding tasks created.`, ...x.warnings].filter(Boolean).join(' '),
    onSuccess: (x) => navigate(`/employees/${x.employeeId}`),
  })
  return (
    <form onSubmit={form.onSubmit((v) => hire.mutate({ ...v, nationality: orNull(v.nationality)?.toUpperCase() ?? null, workEmail: orNull(v.workEmail), dateOfBirth: v.dateOfBirth || null }))}>
      <Stack>
        <Alert color="blue">Their name, email, phone and CV come from the application; department, position and branch from the requisition;
          start date, contract, probation and salary from the accepted offer.</Alert>
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <Select label="Gender" required data={genders} allowDeselect={false} {...form.getInputProps('gender')} />
          <DateInput label="Date of birth" valueFormat="D MMM YYYY" clearable {...form.getInputProps('dateOfBirth')} />
          <TextInput label="Nationality" placeholder="UG" maxLength={2} {...form.getInputProps('nationality')} value={form.getValues().nationality ?? ''} />
          <TextInput label="Work email" description="Links their sign-in to the record" {...form.getInputProps('workEmail')} value={form.getValues().workEmail ?? ''} />
          <Select label="Line manager" clearable searchable data={(employees.data?.items ?? []).map((e) => ({ value: e.id, label: `${e.fullName} (${e.employeeNumber})` }))}
            {...form.getInputProps('managerId')} />
        </SimpleGrid>
        <Checkbox label="Start onboarding" checked={!!form.getValues().onboarding}
          onChange={(e) => form.setFieldValue('onboarding', e.currentTarget.checked ? { templateId: null, itContactId: null } : null)} />
        {form.getValues().onboarding && <StartOnboardingFields value={form.getValues().onboarding!} onChange={(v) => form.setFieldValue('onboarding', v)} />}
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" color="green" loading={hire.isPending}>Make employee</Button>
        </Group>
      </Stack>
    </form>
  )
}

