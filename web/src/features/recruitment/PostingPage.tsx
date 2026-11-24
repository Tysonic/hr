import {
  Divider,
  ActionIcon, Anchor, Button, Card, Checkbox, Drawer, FileInput, Group, Modal, Rating, SegmentedControl, Select, SimpleGrid, Stack, Table, Text,
  Textarea, TextInput, Timeline,
} from '@mantine/core'
import { useForm } from '@mantine/form'
import { IconArrowLeft, IconDownload, IconExternalLink, IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api, download } from '../../api/client'
import type { ApplicationStage, ApplicationSummary, JobApplication, Posting, PostingDetails } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useMe } from '../../lib/permissions'
import { orNull } from '../employees/fields'
import { PostingBadge, StageBadge, stages } from './components'
import { InterviewsSection, OffersSection } from './hiring'
import { careersUrl, PostingForm } from './PostingsPage'

/** One job: its advert, and its candidates by stage. */
export function PostingPage() {
  const { id } = useParams()
  const { data: me } = useMe()
  const [filter, setFilter] = useState<string>('active')
  const [open, setOpen] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [adding, setAdding] = useState(false)
  const posting = useQuery({ queryKey: ['recruitment', 'postings', id], queryFn: () => api.get<Posting>(`/recruitment/postings/${id}`) })
  const apps = useQuery({ queryKey: ['recruitment', 'applications', id], queryFn: () => api.get<ApplicationSummary[]>(`/recruitment/postings/${id}/applications`) })
  const act = useApiMutation((action: 'publish' | 'close') => api.post<Posting>(`/recruitment/postings/${id}/${action}`), {
    invalidate: ['recruitment'], success: (p) => p.status === 'Open' ? 'Published on the careers page.' : 'Closed to new applications.',
  })
  const save = useApiMutation((d: PostingDetails) => api.put<Posting>(`/recruitment/postings/${id}`, d), {
    invalidate: ['recruitment'], success: 'Saved.', onSuccess: () => setEditing(false),
  })
  if (posting.isPending || apps.isPending) return <Loading />
  if (posting.isError) return <LoadError error={posting.error} />
  if (apps.isError) return <LoadError error={apps.error} />
  const p = posting.data
  const count = (s: ApplicationStage) => apps.data.filter((a) => a.stage === s).length
  const shown = apps.data.filter((a) => filter === 'all' ? true : filter === 'active' ? !['Hired', 'Rejected', 'Withdrawn'].includes(a.stage) : a.stage === filter)
  return (
    <>
      <Anchor component={Link} to="/recruitment" size="sm"><Group gap={4}><IconArrowLeft size={14} /> Jobs</Group></Anchor>
      <PageHeader title={p.title} description={<Group gap="xs"><PostingBadge status={p.status} accepting={p.acceptingApplications} />
        <Text size="sm" c="dimmed">{[p.requisitionNumber, p.location, p.closingDate && `closes ${date(p.closingDate)}`].filter(Boolean).join(' · ')}</Text></Group>}
        actions={
          <Group gap="xs">
            {p.status === 'Open' && me?.company && <Button variant="default" component="a" href={`${careersUrl(me.company.slug)}/jobs/${p.id}`} target="_blank"
              leftSection={<IconExternalLink size={16} />}>View advert</Button>}
            {p.status !== 'Closed' && <Button variant="default" onClick={() => setEditing(true)}>Edit</Button>}
            {p.status === 'Draft' && <Button loading={act.isPending} onClick={() => act.mutate('publish')}>Publish</Button>}
            {p.status === 'Open' && <Button variant="default" color="red" loading={act.isPending} onClick={() => act.mutate('close')}>Close</Button>}
            {p.status !== 'Closed' && <Button leftSection={<IconPlus size={16} />} onClick={() => setAdding(true)}>Add candidate</Button>}
          </Group>
        } />
      <SegmentedControl mb="md" value={filter} onChange={setFilter} data={[
        { value: 'active', label: `In progress (${apps.data.filter((a) => !['Hired', 'Rejected', 'Withdrawn'].includes(a.stage)).length})` },
        ...stages.map((s) => ({ value: s.value, label: `${s.label} (${count(s.value)})` })),
        { value: 'all', label: `All (${apps.data.length})` },
      ]} style={{ overflowX: 'auto', maxWidth: '100%' }} />
      <Table.ScrollContainer minWidth={720}>
        <Table striped highlightOnHover>
          <Table.Thead><Table.Tr><Table.Th>Candidate</Table.Th><Table.Th>Stage</Table.Th><Table.Th>Rating</Table.Th><Table.Th>Applied</Table.Th><Table.Th>CV</Table.Th></Table.Tr></Table.Thead>
          <Table.Tbody>
            {shown.length === 0 && <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" ta="center" py="lg">No candidates here.</Text></Table.Td></Table.Tr>}
            {shown.map((a) => (
              <Table.Tr key={a.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(a.id)}>
                <Table.Td><Text size="sm" fw={500}>{a.name}</Text><Text size="xs" c="dimmed">{a.email}{a.phone && ` · ${a.phone}`}</Text></Table.Td>
                <Table.Td><StageBadge value={a.stage} /></Table.Td>
                <Table.Td>{a.rating ? <Rating value={a.rating} readOnly size="xs" /> : <Text size="xs" c="dimmed">—</Text>}</Table.Td>
                <Table.Td><Text size="sm">{date(a.appliedAt.slice(0, 10))}</Text>{a.source === 'AddedByHr' && <Text size="xs" c="dimmed">added by HR</Text>}</Table.Td>
                <Table.Td>{a.hasCv && <ActionIcon variant="subtle" aria-label="Download CV" onClick={(e) => { e.stopPropagation(); void download(`/recruitment/applications/${a.id}/cv`, `${a.name} CV`) }}><IconDownload size={16} /></ActionIcon>}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Card withBorder mt="lg" maw={900}>
        <Text fw={600} mb="xs">Advert</Text>
        <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{p.description}</Text>
      </Card>

      <Drawer opened={!!open} onClose={() => setOpen(null)} position="right" size="lg" title="Candidate">
        {open && <ApplicationView id={open} />}
      </Drawer>
      <Modal opened={editing} onClose={() => setEditing(false)} size="xl" title="Edit job">
        <PostingForm initial={p} loading={save.isPending} submit={(d) => save.mutate(d)} done={() => setEditing(false)} />
      </Modal>
      <Modal opened={adding} onClose={() => setAdding(false)} size="lg" title="Add a candidate">
        <AddCandidate postingId={p.id} done={() => setAdding(false)} />
      </Modal>
    </>
  )
}

function ApplicationView({ id }: { id: string }) {
  const [note, setNote] = useState('')
  const [move, setMove] = useState<ApplicationStage | null>(null)
  const [moveNote, setMoveNote] = useState('')
  const app = useQuery({ queryKey: ['recruitment', 'application', id], queryFn: () => api.get<JobApplication>(`/recruitment/applications/${id}`) })
  const opts = { invalidate: ['recruitment'] }
  const rate = useApiMutation((rating: number | null) => api.post<JobApplication>(`/recruitment/applications/${id}/rate`, { rating }), opts)
  const addNote = useApiMutation(() => api.post<JobApplication>(`/recruitment/applications/${id}/notes`, { text: note }), { ...opts, onSuccess: () => setNote('') })
  const moveTo = useApiMutation((stage: ApplicationStage) => api.post<JobApplication>(`/recruitment/applications/${id}/move`, { stage, note: moveNote.trim() || null }), {
    ...opts, success: 'Moved.', onSuccess: () => { setMove(null); setMoveNote('') },
  })
  if (app.isPending) return <Loading />
  if (app.isError) return <LoadError error={app.error} />
  const a = app.data
  const closed = ['Hired', 'Rejected', 'Withdrawn'].includes(a.stage)
  return (
    <Stack>
      <Group justify="space-between">
        <Stack gap={0}><Text fw={600} size="lg">{a.firstName} {a.lastName}</Text><Text size="sm" c="dimmed">{a.postingTitle}</Text></Stack>
        <StageBadge value={a.stage} />
      </Group>
      <SimpleGrid cols={2} spacing="xs">
        <Text size="sm"><Anchor href={`mailto:${a.email}`}>{a.email}</Anchor></Text>
        <Text size="sm">{a.phone ?? '—'}</Text>
        <Text size="xs" c="dimmed">Applied {dateTime(a.appliedAt)}{a.source === 'AddedByHr' ? ' (added by HR)' : ''}</Text>
        <Text size="xs" c="dimmed">{a.consentedAt ? `Agreed to data use ${date(a.consentedAt.slice(0, 10))}` : ''}</Text>
      </SimpleGrid>
      <Group>
        {a.cvFileName ? <Button size="xs" variant="default" leftSection={<IconDownload size={14} />}
          onClick={() => void download(`/recruitment/applications/${a.id}/cv`, a.cvFileName!)}>{a.cvFileName}</Button> : <Text size="sm" c="dimmed">No CV</Text>}
        <Group gap={4}><Text size="sm">Rating</Text><Rating value={a.rating ?? 0} onChange={(v) => rate.mutate(v || null)} /></Group>
      </Group>
      {a.employeeId && <Anchor component={Link} to={`/employees/${a.employeeId}`} size="sm">Open employee record</Anchor>}
      {a.coverLetter && <Card withBorder padding="sm"><Text size="xs" c="dimmed" mb={4}>Cover letter</Text><Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{a.coverLetter}</Text></Card>}

      {!closed && (
        <Group align="flex-end">
          <Select label="Move to" w={200} value={move} onChange={(v) => setMove(v as ApplicationStage | null)}
            data={stages.filter((s) => s.value !== a.stage && s.value !== 'Hired').map((s) => ({ value: s.value, label: s.label }))} />
          {move && <TextInput label="Note (optional)" style={{ flex: 1 }} value={moveNote} onChange={(e) => setMoveNote(e.currentTarget.value)} />}
          {move && <Button loading={moveTo.isPending} onClick={() => moveTo.mutate(move)}>Move</Button>}
        </Group>
      )}
      <Divider />
      <InterviewsSection app={a} />
      <Divider />
      <OffersSection app={a} />
      <Divider />

      <Stack gap="xs">
        <Text fw={600} size="sm">History</Text>
        <Group align="flex-end">
          <Textarea placeholder="Add a note" autosize minRows={1} style={{ flex: 1 }} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Button variant="default" disabled={!note.trim()} loading={addNote.isPending} onClick={() => addNote.mutate(undefined)}>Add</Button>
        </Group>
        <Timeline bulletSize={10} lineWidth={2}>
          {[...a.events].reverse().map((e, i) => (
            <Timeline.Item key={i} title={<Text size="sm">{e.text}</Text>}>
              <Text size="xs" c="dimmed">{e.by} · {dateTime(e.at)}</Text>
            </Timeline.Item>
          ))}
        </Timeline>
      </Stack>
    </Stack>
  )
}

function AddCandidate({ postingId, done }: { postingId: string; done: () => void }) {
  const form = useForm({
    initialValues: { firstName: '', lastName: '', email: '', phone: '', coverLetter: '', cv: null as File | null, consent: false },
    validate: {
      firstName: (v: string) => (v.trim() ? null : 'Required'), lastName: (v: string) => (v.trim() ? null : 'Required'),
      email: (v: string) => (/^\S+@\S+$/.test(v.trim()) ? null : 'A valid email'), consent: (v: boolean) => (v ? null : 'Required'),
    },
  })
  const add = useApiMutation((f: FormData) => api.upload<JobApplication>(`/recruitment/postings/${postingId}/applications`, f), {
    invalidate: ['recruitment'], success: 'Candidate added.', onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((v) => {
      const f = new FormData()
      f.set('firstName', v.firstName.trim()); f.set('lastName', v.lastName.trim()); f.set('email', v.email.trim())
      if (orNull(v.phone)) f.set('phone', v.phone.trim())
      if (orNull(v.coverLetter)) f.set('coverLetter', v.coverLetter.trim())
      if (v.cv) f.set('cv', v.cv)
      add.mutate(f)
    })}>
      <Stack>
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="First name" required data-autofocus {...form.getInputProps('firstName')} />
          <TextInput label="Last name" required {...form.getInputProps('lastName')} />
          <TextInput label="Email" required {...form.getInputProps('email')} />
          <TextInput label="Phone" {...form.getInputProps('phone')} />
        </SimpleGrid>
        <FileInput label="CV" accept=".pdf,.docx" clearable placeholder="PDF or Word (.docx), up to 5 MB" {...form.getInputProps('cv')} />
        <Textarea label="Notes or cover letter" autosize minRows={2} {...form.getInputProps('coverLetter')} />
        <Checkbox label="The candidate agreed to us keeping their details for this job" {...form.getInputProps('consent', { type: 'checkbox' })} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={add.isPending}>Add</Button>
        </Group>
      </Stack>
    </form>
  )
}

