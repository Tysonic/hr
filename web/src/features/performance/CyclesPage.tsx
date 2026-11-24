import { Button, Checkbox, Group, Modal, MultiSelect, Progress, SimpleGrid, Stack, Table, Text, TextInput } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { Cycle, CycleDetails, CycleSummary } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useDepartments } from '../org/hooks'
import { CycleBadge } from './components'

export function CyclesPage() {
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  const list = useQuery({ queryKey: ['performance', 'cycles'], queryFn: () => api.get<CycleSummary[]>('/performance/cycles') })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  return (
    <>
      <PageHeader title="Review cycles" description="Performance reviews: set a period, launch it for goal setting, then open reviews and close it when done."
        actions={<Button leftSection={<IconPlus size={16} />} onClick={() => setCreating(true)}>New cycle</Button>} />
      {list.data.length === 0 ? <Text c="dimmed">No review cycles yet.</Text> : (
        <Table.ScrollContainer minWidth={600}>
          <Table highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>Cycle</Table.Th><Table.Th>Period</Table.Th><Table.Th>Status</Table.Th><Table.Th w={220}>Completed</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {list.data.map((c) => (
                <Table.Tr key={c.id} style={{ cursor: 'pointer' }} onClick={() => navigate(`/performance/cycles/${c.id}`)}>
                  <Table.Td fw={500}>{c.name}</Table.Td>
                  <Table.Td>{date(c.periodStart)} – {date(c.periodEnd)}</Table.Td>
                  <Table.Td><CycleBadge value={c.status} /></Table.Td>
                  <Table.Td>
                    {c.appraisals > 0 && <Group gap="xs" wrap="nowrap"><Progress value={(c.completed / c.appraisals) * 100} style={{ flex: 1 }} /><Text size="xs">{c.completed}/{c.appraisals}</Text></Group>}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Modal opened={creating} onClose={() => setCreating(false)} size="lg" title="New review cycle">
        {creating && <CycleForm done={(c) => { setCreating(false); if (c) navigate(`/performance/cycles/${c.id}`) }} />}
      </Modal>
    </>
  )
}

const defaultScale = ['Unsatisfactory', 'Needs improvement', 'Meets expectations', 'Exceeds expectations', 'Outstanding']

export function CycleForm({ cycle, done }: { cycle?: Cycle; done: (c?: Cycle) => void }) {
  const departments = useDepartments()
  const year = new Date().getFullYear()
  const form = useForm<CycleDetails>({
    initialValues: cycle
      ? { name: cycle.name, periodStart: cycle.periodStart, periodEnd: cycle.periodEnd, selfReview: cycle.selfReview, goalsDue: cycle.goalsDue,
          reviewsDue: cycle.reviewsDue, scale: cycle.scale, departmentIds: cycle.departmentIds, peerFeedback: cycle.peerFeedback, calibrate: cycle.calibrate }
      : { name: `${year} annual review`, periodStart: `${year}-01-01`, periodEnd: `${year}-12-31`, selfReview: true, goalsDue: null, reviewsDue: null,
          scale: defaultScale, departmentIds: [], peerFeedback: false, calibrate: false },
    validate: { name: (v) => (v.trim() ? null : 'Required'), periodStart: (v) => (v ? null : 'Required'), periodEnd: (v) => (v ? null : 'Required') },
  })
  const save = useApiMutation((d: CycleDetails) => cycle ? api.put<Cycle>(`/performance/cycles/${cycle.id}`, d) : api.post<Cycle>('/performance/cycles', d), {
    invalidate: ['performance'], success: 'Saved.', onSuccess: (c) => done(c),
  })
  const started = !!cycle && cycle.status !== 'Draft'
  const v = form.getValues()
  return (
    <form onSubmit={form.onSubmit((d) => save.mutate({ ...d, goalsDue: d.goalsDue || null, reviewsDue: d.reviewsDue || null }))}>
      <Stack>
        <TextInput label="Name" required {...form.getInputProps('name')} />
        <SimpleGrid cols={2}>
          <DateInput label="Period from" required valueFormat="D MMM YYYY" {...form.getInputProps('periodStart')} />
          <DateInput label="to" required valueFormat="D MMM YYYY" {...form.getInputProps('periodEnd')} />
          <DateInput label="Goals agreed by" clearable valueFormat="D MMM YYYY" {...form.getInputProps('goalsDue')} />
          <DateInput label="Reviews done by" clearable valueFormat="D MMM YYYY" {...form.getInputProps('reviewsDue')} />
        </SimpleGrid>
        <MultiSelect label="Departments" placeholder={v.departmentIds.length ? undefined : 'Everyone'} disabled={started} clearable searchable
          data={(departments.data ?? []).map((d) => ({ value: d.id, label: d.name }))} {...form.getInputProps('departmentIds')} />
        <Checkbox label="Employees rate themselves before their manager does" disabled={started} {...form.getInputProps('selfReview', { type: 'checkbox' })} />
        <Checkbox label="Reviewers can ask colleagues for 360° feedback" {...form.getInputProps('peerFeedback', { type: 'checkbox' })} />
        <Checkbox label="HR calibrates ratings before employees see their results" disabled={started} {...form.getInputProps('calibrate', { type: 'checkbox' })} />
        <Stack gap={4}>
          <Text size="sm" fw={500}>Rating scale</Text>
          {(v.scale ?? defaultScale).map((s, i) => (
            <Group key={i} gap="xs"><Text w={20} ta="right" fw={600}>{i + 1}</Text>
              <TextInput style={{ flex: 1 }} value={s} onChange={(e) => form.setFieldValue('scale', (v.scale ?? defaultScale).map((x, k) => (k === i ? e.currentTarget.value : x)))} />
            </Group>
          ))}
        </Stack>
        <Group justify="flex-end">
          <Button variant="default" onClick={() => done()}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>Save</Button>
        </Group>
      </Stack>
    </form>
  )
}
