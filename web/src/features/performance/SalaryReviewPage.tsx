import { Alert, Anchor, Badge, Button, Card, Group, Modal, NumberInput, SimpleGrid, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { modals } from '@mantine/modals'
import { IconArrowLeft } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import type { ApplyRaisesResult, SalaryRecommendation, SalaryReview } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useMoney } from '../payroll/components'
import { ratingText } from './components'

/** Turns a cycle's final ratings into raises (people who may set pay). */
export function SalaryReviewPage() {
  const { id } = useParams()
  const money = useMoney()
  const [editing, setEditing] = useState<SalaryRecommendation | null>(null)
  const q = useQuery({ queryKey: ['performance', 'salary-review', id], queryFn: () => api.get<SalaryReview>(`/performance/cycles/${id}/salary-review`) })
  const apply = useApiMutation(() => api.post<ApplyRaisesResult>(`/performance/cycles/${id}/salary-review/apply`), {
    invalidate: ['performance', 'employees'], success: (r) => `${r.applied} raise${r.applied === 1 ? '' : 's'} applied as salary changes.`,
  })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const r = q.data
  const toApply = r.recommendations.filter((x) => x.status === 'Proposed' && x.proposedSalary > x.currentSalary)
  const increase = r.proposedTotal - r.currentTotal
  return (
    <>
      <Anchor component={Link} to={`/performance/cycles/${r.cycleId}`} size="sm"><Group gap={4}><IconArrowLeft size={14} />{r.cycleName}</Group></Anchor>
      <PageHeader title="Salary review" description="Raises from final ratings. Applying them records salary changes on the effective date."
        actions={r.canApply && <Button color="green" loading={apply.isPending} onClick={() => modals.openConfirmModal({
          title: 'Apply the raises?',
          children: <Text size="sm">{toApply.length} salary change{toApply.length === 1 ? '' : 's'} will be recorded, effective {date(r.effectiveDate)}, adding {money(toApply.reduce((s, x) => s + x.increase, 0))} a month. Payroll uses them from that month. Applied raises can't be changed here; correct them on the employee's record.</Text>,
          labels: { confirm: 'Apply raises', cancel: 'Cancel' }, confirmProps: { color: 'green' }, onConfirm: () => apply.mutate(undefined),
        })}>Apply raises</Button>} />
      <Stack>
        <Guideline r={r} key={r.percents.join() + r.effectiveDate} />
        {r.left.map((l) => <Alert key={l} color="yellow" variant="light">{l}</Alert>)}
        {r.exists && (
          <>
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <Card withBorder><Text size="xs" c="dimmed">Monthly basic now</Text><Text fw={600}>{money(r.currentTotal)}</Text></Card>
              <Card withBorder><Text size="xs" c="dimmed">After raises</Text><Text fw={600}>{money(r.proposedTotal)}</Text></Card>
              <Card withBorder><Text size="xs" c="dimmed">Increase a month</Text>
                <Text fw={600}>{money(increase)}{r.currentTotal > 0 && ` (${((increase / r.currentTotal) * 100).toFixed(1)}%)`}</Text></Card>
            </SimpleGrid>
            <Table.ScrollContainer minWidth={820}>
              <Table>
                <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Rating</Table.Th><Table.Th ta="right">Current</Table.Th><Table.Th ta="right">Raise</Table.Th>
                  <Table.Th ta="right">Proposed</Table.Th><Table.Th>Status</Table.Th><Table.Th /></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {r.recommendations.map((x) => (
                    <Table.Tr key={x.id} c={x.status === 'Skipped' ? 'dimmed' : undefined}>
                      <Table.Td><Text size="sm">{x.employeeName}</Text><Text size="xs" c="dimmed">{x.employeeNumber}{x.departmentName && ` · ${x.departmentName}`}</Text></Table.Td>
                      <Table.Td>{ratingText(x.rating, r.scale)}</Table.Td>
                      <Table.Td ta="right">{money(x.currentSalary)}</Table.Td>
                      <Table.Td ta="right">{x.percent}%{x.adjusted && <Text size="xs" c="dimmed">adjusted</Text>}</Table.Td>
                      <Table.Td ta="right"><Text size="sm" fw={500}>{money(x.proposedSalary)}</Text>{x.note && <Text size="xs" c="dimmed">{x.note}</Text>}</Table.Td>
                      <Table.Td><Badge variant="light" color={{ Proposed: 'blue', Applied: 'green', Skipped: 'gray' }[x.status]}>{x.status}</Badge></Table.Td>
                      <Table.Td>{x.status !== 'Applied' && <Button size="compact-xs" variant="subtle" onClick={() => setEditing(x)}>Adjust</Button>}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Table.ScrollContainer>
          </>
        )}
      </Stack>
      <Modal opened={!!editing} onClose={() => setEditing(null)} title={editing ? `Raise for ${editing.employeeName}` : ''}>
        {editing && <Adjust cycleId={r.cycleId} rec={editing} done={() => setEditing(null)} />}
      </Modal>
    </>
  )
}

function Guideline({ r }: { r: SalaryReview }) {
  const [percents, setPercents] = useState<(number | string)[]>(r.percents)
  const [effective, setEffective] = useState<string | null>(r.effectiveDate)
  const save = useApiMutation(() => api.put<SalaryReview>(`/performance/cycles/${r.cycleId}/salary-review`, {
    percents: percents.map((p) => Number(p) || 0), effectiveDate: effective,
  }), { invalidate: ['performance'], success: r.exists ? 'Guideline saved; recommendations updated.' : 'Recommendations worked out.' })
  return (
    <Card withBorder>
      <Title order={5}>Guideline</Title>
      <Text size="xs" c="dimmed" mb="sm">Increase for each final rating, rounded to the nearest whole rating. Adjusted raises keep their own figure.</Text>
      <Group align="flex-end" wrap="wrap">
        {r.scale.map((label, i) => (
          <NumberInput key={i} w={130} label={`${i + 1} ${label}`} suffix="%" min={0} max={100} decimalScale={2} value={percents[i]}
            onChange={(v) => setPercents(percents.map((p, k) => (k === i ? v : p)))} styles={{ label: { whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' } }} />
        ))}
        <DateInput label="Effective from" w={160} valueFormat="D MMM YYYY" value={effective} onChange={setEffective} />
        <Button loading={save.isPending} disabled={!effective} onClick={() => save.mutate(undefined)}>{r.exists ? 'Recalculate' : 'Work out raises'}</Button>
      </Group>
    </Card>
  )
}

function Adjust({ cycleId, rec, done }: { cycleId: string; rec: SalaryRecommendation; done: () => void }) {
  const money = useMoney()
  const [proposed, setProposed] = useState<number | string>(rec.proposedSalary)
  const [note, setNote] = useState(rec.note ?? '')
  const [skip, setSkip] = useState(rec.status === 'Skipped')
  const save = useApiMutation(() => api.put<SalaryReview>(`/performance/cycles/${cycleId}/salary-review/recommendations/${rec.id}`, {
    proposedSalary: skip || Number(proposed) === rec.proposedSalary ? null : Number(proposed), note: note.trim() || null, skip,
  }), { invalidate: ['performance'], success: 'Saved.', onSuccess: done })
  return (
    <Stack>
      <Text size="sm">Current monthly basic: <b>{money(rec.currentSalary)}</b></Text>
      <Switch label="Leave out of this review" checked={skip} onChange={(e) => setSkip(e.currentTarget.checked)} />
      {!skip && <NumberInput label="Proposed monthly basic" min={rec.currentSalary} thousandSeparator="," value={proposed} onChange={setProposed}
        description={Number(proposed) > 0 ? `${(((Number(proposed) / rec.currentSalary) - 1) * 100).toFixed(2)}% raise` : undefined} />}
      <TextInput label="Note" placeholder={skip ? 'Why, e.g. promoted mid-year' : 'Why, if it differs from the guideline'} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button>
      </Group>
    </Stack>
  )
}
