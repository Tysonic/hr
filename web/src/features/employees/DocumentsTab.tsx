import { ActionIcon, Badge, Button, FileInput, Group, Menu, Modal, Select, SimpleGrid, Stack, Table, Text, Textarea, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { modals } from '@mantine/modals'
import { notifications } from '@mantine/notifications'
import { IconDots, IconDownload, IconPaperclip, IconUpload } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, download } from '../../api/client'
import type { DocumentCategory, DocumentDetails, Employee, EmployeeDocument } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { label, orNull } from './fields'

export const documentCategories: { value: DocumentCategory; label: string }[] = [
  { value: 'Identification', label: 'Identification' }, { value: 'Contract', label: 'Contract' },
  { value: 'Certificate', label: 'Certificate / qualification' }, { value: 'WorkPermit', label: 'Work permit' },
  { value: 'Medical', label: 'Medical' }, { value: 'Other', label: 'Other' },
]

const maxBytes = 10 * 1024 * 1024
const accept = '.pdf,.png,.jpg,.jpeg,.docx,.xlsx'

export function expiryBadge(daysLeft: number | null) {
  if (daysLeft === null) return null
  if (daysLeft < 0) return <Badge size="sm" color="red" variant="light">Expired {-daysLeft}d ago</Badge>
  if (daysLeft === 0) return <Badge size="sm" color="red" variant="light">Expires today</Badge>
  if (daysLeft <= 30) return <Badge size="sm" color="orange" variant="light">{daysLeft}d left</Badge>
  return null
}

const size = (bytes: number) => bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`

/** An employee's documents: HR uploads and edits, payroll can read. Files are fetched with the user's token. */
export function DocumentsTab({ employee }: { employee: Employee }) {
  const canManage = useCan('documentsManage')
  const path = `/employees/${employee.id}/documents`
  const documents = useQuery({ queryKey: ['documents', employee.id], queryFn: () => api.get<EmployeeDocument[]>(path) })
  const [editing, setEditing] = useState<EmployeeDocument | 'new' | null>(null)
  const remove = useApiMutation((d: EmployeeDocument) => api.delete<void>(`${path}/${d.id}`), {
    invalidate: ['documents', 'expiries'], success: (_, d) => `${d.title} deleted.`,
  })
  const confirmDelete = (d: EmployeeDocument) => modals.openConfirmModal({
    title: `Delete ${d.title}?`,
    children: <Text size="sm">The file {d.fileName} is removed for good.</Text>,
    labels: { confirm: 'Delete', cancel: 'Cancel' },
    confirmProps: { color: 'red' },
    onConfirm: () => remove.mutate(d),
  })
  const get = (d: EmployeeDocument) =>
    download(`${path}/${d.id}/file`, d.fileName).catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))

  if (documents.isPending) return <Loading />
  if (documents.isError) return <LoadError error={documents.error} />
  return (
    <Stack maw={1100}>
      {canManage && (
        <Group justify="flex-end"><Button leftSection={<IconUpload size={16} />} onClick={() => setEditing('new')}>Upload document</Button></Group>
      )}
      <Table.ScrollContainer minWidth={760}>
        <Table striped>
          <Table.Thead>
            <Table.Tr><Table.Th>Document</Table.Th><Table.Th>Category</Table.Th><Table.Th>Issued</Table.Th><Table.Th>Expires</Table.Th><Table.Th>Uploaded</Table.Th><Table.Th /></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {documents.data.length === 0 && (
              <Table.Tr><Table.Td colSpan={6}><Text c="dimmed" ta="center" py="md">No documents yet.</Text></Table.Td></Table.Tr>
            )}
            {documents.data.map((d) => (
              <Table.Tr key={d.id}>
                <Table.Td>
                  <Stack gap={0}>
                    <Text size="sm" fw={500}>{d.title}</Text>
                    <Text size="xs" c="dimmed"><IconPaperclip size={11} /> {d.fileName} · {size(d.sizeBytes)}</Text>
                    {d.notes && <Text size="xs" c="dimmed">{d.notes}</Text>}
                  </Stack>
                </Table.Td>
                <Table.Td>{label(documentCategories, d.category)}</Table.Td>
                <Table.Td>{date(d.issuedOn)}</Table.Td>
                <Table.Td><Group gap="xs">{d.expiresOn ? date(d.expiresOn) : '—'}{expiryBadge(d.daysToExpiry)}</Group></Table.Td>
                <Table.Td><Text size="xs" c="dimmed">{d.uploadedBy}, {dateTime(d.uploadedAt)}</Text></Table.Td>
                <Table.Td w={80}>
                  <Group gap={4} wrap="nowrap">
                    <ActionIcon variant="subtle" aria-label={`Download ${d.title}`} onClick={() => void get(d)}><IconDownload size={16} /></ActionIcon>
                    {canManage && (
                      <Menu position="bottom-end">
                        <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${d.title}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                        <Menu.Dropdown>
                          <Menu.Item onClick={() => setEditing(d)}>Edit details</Menu.Item>
                          <Menu.Item color="red" onClick={() => confirmDelete(d)}>Delete</Menu.Item>
                        </Menu.Dropdown>
                      </Menu>
                    )}
                  </Group>
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} size="lg"
        title={editing === 'new' ? `Upload a document for ${employee.fullName}` : 'Document details'}>
        {editing !== null && <DocumentForm path={path} document={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </Stack>
  )
}

function DocumentForm({ path, document, done }: { path: string; document: EmployeeDocument | null; done: () => void }) {
  const form = useForm({
    initialValues: {
      file: null as File | null,
      title: document?.title ?? '',
      category: document?.category ?? ('Identification' as DocumentCategory),
      issuedOn: document?.issuedOn ?? null as string | null,
      expiresOn: document?.expiresOn ?? null as string | null,
      notes: document?.notes ?? '',
    },
    validate: {
      file: (f: File | null) => document ? null : !f ? 'Choose a file' : f.size > maxBytes ? 'Files can be at most 10 MB' : null,
      title: (v: string) => (v.trim() ? null : 'Required'),
      expiresOn: (v: string | null, all) => (v && all.issuedOn && v < all.issuedOn ? 'Expires before it was issued' : null),
    },
  })
  const save = useApiMutation(async (v: typeof form.values) => {
    const details: DocumentDetails = { title: v.title.trim(), category: v.category, issuedOn: v.issuedOn || null, expiresOn: v.expiresOn || null, notes: orNull(v.notes) }
    if (document) return api.put<EmployeeDocument>(`${path}/${document.id}`, details)
    const body = new FormData()
    body.set('file', v.file!)
    for (const [k, value] of Object.entries(details)) if (value !== null) body.set(k, value)
    return api.upload<EmployeeDocument>(path, body)
  }, { invalidate: ['documents', 'expiries'], success: (d) => `${d.title} saved.`, onSuccess: done })

  return (
    <form onSubmit={form.onSubmit((v) => save.mutate(v))}>
      <Stack>
        {!document && (
          <FileInput label="File" required accept={accept} placeholder="PDF, image, Word or Excel, up to 10 MB" clearable
            leftSection={<IconPaperclip size={16} />} {...form.getInputProps('file')}
            onChange={(f) => {
              form.setFieldValue('file', f)
              if (f && !form.getValues().title) form.setFieldValue('title', f.name.replace(/\.[^.]+$/, '').replace(/[-_]+/g, ' '))
            }} />
        )}
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="Title" required placeholder="Passport, degree certificate…" {...form.getInputProps('title')} />
          <Select label="Category" data={documentCategories} allowDeselect={false} {...form.getInputProps('category')} />
          <DateInput label="Issued on" clearable valueFormat="D MMM YYYY" {...form.getInputProps('issuedOn')} />
          <DateInput label="Expires on" clearable valueFormat="D MMM YYYY" description="HR is reminded 30 and 7 days before" {...form.getInputProps('expiresOn')} />
        </SimpleGrid>
        <Textarea label="Notes" autosize minRows={1} {...form.getInputProps('notes')} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>{document ? 'Save' : 'Upload'}</Button>
        </Group>
      </Stack>
    </form>
  )
}
