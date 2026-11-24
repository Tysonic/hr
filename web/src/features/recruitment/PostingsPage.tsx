import { ActionIcon, Anchor, Button, Checkbox, CopyButton, Group, SimpleGrid, Stack, Table, Text, Textarea, TextInput, Tooltip } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconCheck, IconCopy, IconExternalLink } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { Posting, PostingDetails } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useMe } from '../../lib/permissions'
import { orNull } from '../employees/fields'
import { PostingBadge } from './components'

export const careersUrl = (slug: string) => `${window.location.origin}/careers/${slug}`

/** Jobs advertised (or drafted) from approved requisitions, with how many candidates are at each stage. */
export function PostingsPage() {
  const { data: me } = useMe()
  const list = useQuery({ queryKey: ['recruitment', 'postings'], queryFn: () => api.get<Posting[]>('/recruitment/postings') })
  if (list.isPending || !me?.company) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  const url = careersUrl(me.company.slug)
  return (
    <>
      <PageHeader title="Jobs" description="Advertise a job from an approved requisition (Requisitions → Advertise). Open jobs show on your public careers page." />
      <Group gap="xs" mb="md">
        <Text size="sm">Careers page:</Text>
        <Anchor href={url} target="_blank" size="sm">{url} <IconExternalLink size={12} /></Anchor>
        <CopyButton value={url}>
          {({ copied, copy }) => (
            <Tooltip label={copied ? 'Copied' : 'Copy link'}>
              <ActionIcon variant="subtle" onClick={copy} aria-label="Copy link">{copied ? <IconCheck size={16} /> : <IconCopy size={16} />}</ActionIcon>
            </Tooltip>
          )}
        </CopyButton>
      </Group>
      <Table.ScrollContainer minWidth={760}>
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr><Table.Th>Job</Table.Th><Table.Th>Status</Table.Th><Table.Th>Closes</Table.Th><Table.Th ta="right">Candidates</Table.Th><Table.Th ta="right">New</Table.Th><Table.Th ta="right">Interview</Table.Th><Table.Th ta="right">Offer</Table.Th></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {list.data.length === 0 && <Table.Tr><Table.Td colSpan={7}><Text c="dimmed" ta="center" py="lg">No jobs yet.</Text></Table.Td></Table.Tr>}
            {list.data.map((p) => (
              <Table.Tr key={p.id}>
                <Table.Td>
                  <Anchor component={Link} to={`/recruitment/postings/${p.id}`} fw={500} size="sm">{p.title}</Anchor>
                  <Text size="xs" c="dimmed">{[p.requisitionNumber, p.location].filter(Boolean).join(' · ')}</Text>
                </Table.Td>
                <Table.Td><PostingBadge status={p.status} accepting={p.acceptingApplications} /></Table.Td>
                <Table.Td>{date(p.closingDate)}</Table.Td>
                <Table.Td ta="right">{p.applications}</Table.Td>
                <Table.Td ta="right">{p.stages.Applied ?? 0}</Table.Td>
                <Table.Td ta="right">{p.stages.Interview ?? 0}</Table.Td>
                <Table.Td ta="right">{p.stages.Offer ?? 0}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </>
  )
}

export function PostingForm({ initial, submit, loading, done }:
  { initial: PostingDetails; submit: (d: PostingDetails) => void; loading: boolean; done: () => void }) {
  const form = useForm<PostingDetails>({
    initialValues: { ...initial, location: initial.location ?? '' },
    validate: { title: (v) => (v.trim() ? null : 'Required'), description: (v) => (v.trim() ? null : 'Required') },
  })
  return (
    <form onSubmit={form.onSubmit((d) => submit({ ...d, title: d.title.trim(), description: d.description.trim(), location: orNull(d.location), closingDate: d.closingDate || null }))}>
      <Stack>
        <TextInput label="Title" required data-autofocus {...form.getInputProps('title')} />
        <Textarea label="Description" required autosize minRows={8} maxRows={20}
          description="What the job is, what you're looking for, and how to apply. Shown as written on the careers page." {...form.getInputProps('description')} />
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="Location" placeholder="Kampala" {...form.getInputProps('location')} />
          <DateInput label="Closing date" valueFormat="D MMM YYYY" clearable {...form.getInputProps('closingDate')} />
        </SimpleGrid>
        <Checkbox label="Show the salary range from the requisition" {...form.getInputProps('showSalary', { type: 'checkbox' })} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={loading}>Save</Button>
        </Group>
      </Stack>
    </form>
  )
}
