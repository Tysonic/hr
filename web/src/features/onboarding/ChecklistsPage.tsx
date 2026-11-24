import { ActionIcon, Badge, Button, Card, Checkbox, Group, Modal, NumberInput, Select, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { modals } from '@mantine/modals'
import { IconPlus, IconTrash } from '@tabler/icons-react'
import { useState } from 'react'
import { api } from '../../api/client'
import type { ChecklistItem, OnboardingTemplate, SaveOnboardingTemplate } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'
import { dueText, ownerColor, ownerLabel, owners, useOnboardingSetup } from './components'

/** Onboarding checklist templates (Settings). */
export function ChecklistsPage() {
  const setup = useOnboardingSetup()
  const [editing, setEditing] = useState<OnboardingTemplate | SaveOnboardingTemplate | null>(null)
  const remove = useApiMutation((t: OnboardingTemplate) => api.delete(`/onboarding/templates/${t.id}`), { invalidate: ['onboarding'], success: 'Checklist deleted.' })
  if (setup.isPending) return <Loading />
  if (setup.isError) return <LoadError error={setup.error} />
  const { templates, standard } = setup.data
  const confirmDelete = (t: OnboardingTemplate) => modals.openConfirmModal({
    title: `Delete ${t.name}?`, children: <Text size="sm">New starters already given these tasks keep them.</Text>,
    labels: { confirm: 'Delete', cancel: 'Cancel' }, confirmProps: { color: 'red' }, onConfirm: () => remove.mutate(t),
  })
  return (
    <>
      <PageHeader title="Onboarding checklists" description="The tasks to do for each new starter, who does them and when. Use different checklists for different kinds of job."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setEditing({ name: '', items: standard, isDefault: templates.length === 0 })}>New checklist</Button>} />
      {templates.length === 0 && (
        <Card withBorder mb="md">
          <Text size="sm">You have no checklists yet, so new starters get TyHR's standard one ({standard.length} tasks). Make your own starting from it with New checklist.</Text>
        </Card>
      )}
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        {templates.map((t) => (
          <Card key={t.id} withBorder>
            <Group justify="space-between" mb="xs">
              <Group gap="xs"><Text fw={600}>{t.name}</Text>{t.isDefault && <Badge variant="light">Default</Badge>}</Group>
              <Group gap={4}>
                <Button size="xs" variant="default" onClick={() => setEditing(t)}>Edit</Button>
                <ActionIcon variant="subtle" color="red" aria-label="Delete" onClick={() => confirmDelete(t)}><IconTrash size={16} /></ActionIcon>
              </Group>
            </Group>
            <Stack gap={4}>
              {t.items.map((i, n) => (
                <Group key={n} gap="xs" wrap="nowrap">
                  <Badge size="xs" variant="light" color={ownerColor[i.owner]} w={80}>{ownerLabel(i.owner)}</Badge>
                  <Text size="sm" style={{ flex: 1 }}>{i.title}</Text>
                  <Text size="xs" c="dimmed">{dueText(i.dueDays)}</Text>
                </Group>
              ))}
            </Stack>
          </Card>
        ))}
      </SimpleGrid>
      <Modal opened={!!editing} onClose={() => setEditing(null)} size="xl" title={editing && 'id' in editing ? `Edit ${editing.name}` : 'New checklist'}>
        {editing && <TemplateForm template={editing} done={() => setEditing(null)} />}
      </Modal>
    </>
  )
}

function TemplateForm({ template, done }: { template: OnboardingTemplate | SaveOnboardingTemplate; done: () => void }) {
  const id = 'id' in template ? template.id : null
  const [name, setName] = useState(template.name)
  const [isDefault, setIsDefault] = useState(template.isDefault)
  const [items, setItems] = useState<ChecklistItem[]>(template.items)
  const save = useApiMutation((r: SaveOnboardingTemplate) => id ? api.put<OnboardingTemplate>(`/onboarding/templates/${id}`, r) : api.post<OnboardingTemplate>('/onboarding/templates', r), {
    invalidate: ['onboarding'], success: 'Checklist saved.', onSuccess: done,
  })
  const set = (n: number, patch: Partial<ChecklistItem>) => setItems(items.map((i, k) => (k === n ? { ...i, ...patch } : i)))
  return (
    <Stack>
      <Group grow align="flex-end">
        <TextInput label="Name" required placeholder="e.g. Office staff" value={name} onChange={(e) => setName(e.currentTarget.value)} />
        <Checkbox label="Use for new starters unless another is chosen" checked={isDefault} onChange={(e) => setIsDefault(e.currentTarget.checked)} mb={8} />
      </Group>
      <Stack gap="xs">
        {items.map((i, n) => (
          <Group key={n} gap="xs" align="flex-end" wrap="nowrap">
            <TextInput label={n === 0 ? 'Task' : undefined} style={{ flex: 3 }} value={i.title} onChange={(e) => set(n, { title: e.currentTarget.value })} />
            <Select label={n === 0 ? 'Who' : undefined} style={{ flex: 1.3 }} data={owners} allowDeselect={false} value={i.owner} onChange={(v) => set(n, { owner: v as ChecklistItem['owner'] })} />
            <NumberInput label={n === 0 ? 'Days from start' : undefined} description={n === 0 ? 'minus is before' : undefined} style={{ flex: 1 }} min={-90} max={365}
              value={i.dueDays} onChange={(v) => set(n, { dueDays: Number(v) || 0 })} />
            <ActionIcon variant="subtle" color="red" mb={4} aria-label="Remove task" onClick={() => setItems(items.filter((_, k) => k !== n))}><IconTrash size={16} /></ActionIcon>
          </Group>
        ))}
        <Button variant="subtle" w="fit-content" leftSection={<IconPlus size={14} />} onClick={() => setItems([...items, { title: '', owner: 'Hr', dueDays: 0, description: null }])}>Add task</Button>
      </Stack>
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button loading={save.isPending} disabled={!name.trim() || items.length === 0 || items.some((i) => !i.title.trim())}
          onClick={() => save.mutate({ name: name.trim(), isDefault, items: items.map((i) => ({ ...i, title: i.title.trim() })) })}>Save</Button>
      </Group>
    </Stack>
  )
}
