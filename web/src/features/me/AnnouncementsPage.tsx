import { ActionIcon, Badge, Button, Card, Checkbox, Group, Modal, Stack, Text, Textarea, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { modals } from '@mantine/modals'
import { IconPencil, IconPin, IconPlus, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { Announcement, SaveAnnouncementRequest } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

export const useAnnouncements = (all = false) =>
  useQuery({ queryKey: ['announcements', all], queryFn: () => api.get<Announcement[]>('/announcements', { all }) })

/** One announcement as everyone sees it. */
export function AnnouncementCard({ a, actions }: { a: Announcement; actions?: React.ReactNode }) {
  return (
    <Card withBorder padding="md" opacity={a.current ? 1 : 0.6}>
      <Group justify="space-between" wrap="nowrap" align="flex-start" mb={4}>
        <Group gap={6}>
          {a.pinned && <IconPin size={16} color="var(--mantine-color-indigo-6)" />}
          <Text fw={600}>{a.title}</Text>
          {!a.current && <Badge size="xs" color="gray">Ended</Badge>}
        </Group>
        {actions}
      </Group>
      <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{a.body}</Text>
      <Text size="xs" c="dimmed" mt="xs">
        {a.publishedBy} · {dateTime(a.publishedAt)}{a.expiresOn && ` · shown until ${date(a.expiresOn)}`}
      </Text>
    </Card>
  )
}

/** HR posts notices for everyone; they show on the home page until their end date. */
export function AnnouncementsPage() {
  const [editing, setEditing] = useState<Announcement | 'new' | null>(null)
  const list = useAnnouncements(true)
  const remove = useApiMutation((a: Announcement) => api.delete<void>(`/announcements/${a.id}`), { invalidate: ['announcements'], success: 'Removed.' })
  const confirmRemove = (a: Announcement) => modals.openConfirmModal({
    title: 'Remove this announcement?', children: <Text size="sm">"{a.title}" will disappear for everyone. This can't be undone.</Text>,
    labels: { confirm: 'Remove', cancel: 'Keep' }, confirmProps: { color: 'red' }, onConfirm: () => remove.mutate(a),
  })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  return (
    <>
      <PageHeader title="Announcements" description="Notices for everyone in the company, shown on their home page."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>New announcement</Button>} />
      <Stack maw={800}>
        {list.data.length === 0 && <Text c="dimmed" ta="center" py="xl">No announcements yet.</Text>}
        {list.data.map((a) => (
          <AnnouncementCard key={a.id} a={a} actions={
            <Group gap={4} wrap="nowrap">
              <ActionIcon variant="subtle" aria-label="Edit" onClick={() => setEditing(a)}><IconPencil size={16} /></ActionIcon>
              <ActionIcon variant="subtle" color="red" aria-label="Remove" onClick={() => confirmRemove(a)}><IconTrash size={16} /></ActionIcon>
            </Group>} />
        ))}
      </Stack>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} size="lg" title={editing === 'new' ? 'New announcement' : 'Edit announcement'}>
        {editing && <AnnouncementForm announcement={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </>
  )
}

function AnnouncementForm({ announcement: a, done }: { announcement: Announcement | null; done: () => void }) {
  const form = useForm<SaveAnnouncementRequest>({
    initialValues: { title: a?.title ?? '', body: a?.body ?? '', pinned: a?.pinned ?? false, expiresOn: a?.expiresOn ?? null },
    validate: { title: (v) => (v.trim() ? null : 'Required'), body: (v) => (v.trim() ? null : 'Required') },
  })
  const save = useApiMutation((r: SaveAnnouncementRequest) => a ? api.put<Announcement>(`/announcements/${a.id}`, r) : api.post<Announcement>('/announcements', r), {
    invalidate: ['announcements'], success: a ? 'Saved.' : 'Published.', onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((v) => save.mutate({ ...v, title: v.title.trim(), body: v.body.trim(), expiresOn: v.expiresOn || null }))}>
      <Stack>
        <TextInput label="Title" required maxLength={200} data-autofocus {...form.getInputProps('title')} />
        <Textarea label="Message" required autosize minRows={4} maxRows={14} {...form.getInputProps('body')} />
        <DateInput label="Show until" description="Leave empty to show it until you remove it." valueFormat="D MMM YYYY" clearable
          {...form.getInputProps('expiresOn')} />
        <Checkbox label="Pin to the top" {...form.getInputProps('pinned', { type: 'checkbox' })} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>{a ? 'Save' : 'Publish'}</Button>
        </Group>
      </Stack>
    </form>
  )
}
