import { Alert, Anchor, Button, Card, Group, Modal, Progress, Select, SimpleGrid, Stack, Table, Text } from '@mantine/core'
import { modals } from '@mantine/modals'
import { IconArrowLeft } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { api } from '../../api/client'
import type { AppraisalRow, Cycle, CycleActionResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { useActiveEmployees } from '../onboarding/components'
import { CycleBadge, ratingText, StageBadge } from './components'
import { CycleForm } from './CyclesPage'

type Step = 'launch' | 'start-reviews' | 'release' | 'close'

export function CyclePage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [editing, setEditing] = useState(false)
  const [reviewer, setReviewer] = useState<AppraisalRow | null>(null)
  const [warnings, setWarnings] = useState<string[]>([])
  const canPay = useCan('employeesPay')
  const q = useQuery({ queryKey: ['performance', 'cycle', id], queryFn: () => api.get<Cycle>(`/performance/cycles/${id}`) })
  const act = useApiMutation((step: Step) => api.post<CycleActionResult>(`/performance/cycles/${id}/${step}`), {
    invalidate: ['performance', 'appraisals'], success: 'Done. Everyone involved has been emailed.', onSuccess: (r) => setWarnings(r.warnings),
  })
  const remove = useApiMutation(() => api.delete(`/performance/cycles/${id}`), { invalidate: ['performance'], success: 'Cycle deleted.', onSuccess: () => navigate('/performance') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const c = q.data
  const total = c.appraisals.length
  const count = (f: (a: AppraisalRow) => boolean) => c.appraisals.filter(f).length
  const confirm = (title: string, body: string, step: Step) => modals.openConfirmModal({
    title, children: <Text size="sm">{body}</Text>, labels: { confirm: 'Go ahead', cancel: 'Cancel' }, onConfirm: () => act.mutate(step),
  })
  const rated = Object.values(c.distribution).reduce((a, b) => a + b, 0)
  return (
    <>
      <Anchor component={Link} to="/performance" size="sm"><Group gap={4}><IconArrowLeft size={14} />Review cycles</Group></Anchor>
      <PageHeader title={c.name} description={[`${date(c.periodStart)} – ${date(c.periodEnd)}`, c.selfReview && 'self-review', c.peerFeedback && '360° feedback',
        c.calibrate && (c.resultsReleasedAt ? `results released ${date(c.resultsReleasedAt)}` : 'calibrated')].filter(Boolean).join(' · ')}
        actions={
          <Group>
            <CycleBadge value={c.status} />
            {c.status !== 'Closed' && <Button variant="default" onClick={() => setEditing(true)}>Edit</Button>}
            {c.status === 'Draft' && <Button variant="default" color="red" onClick={() => modals.openConfirmModal({
              title: `Delete ${c.name}?`, labels: { confirm: 'Delete', cancel: 'Cancel' }, confirmProps: { color: 'red' }, onConfirm: () => remove.mutate(undefined),
            })}>Delete</Button>}
            {c.status === 'Draft' && <Button loading={act.isPending} onClick={() => confirm('Launch the cycle?',
              'Everyone in scope gets a review and an email asking them to set goals with their manager.', 'launch')}>Launch</Button>}
            {c.status === 'GoalSetting' && <Button loading={act.isPending} onClick={() => confirm('Open reviews?',
              'People with agreed goals move on to their self-review (or their manager’s review). Others follow once their goals are agreed.', 'start-reviews')}>Open reviews</Button>}
            {canPay && (c.status === 'Review' || c.status === 'Closed') && (
              <Button variant="default" component={Link} to={`/performance/cycles/${c.id}/salary-review`}>Salary review</Button>
            )}
            {c.status === 'Review' && c.calibrate && !c.resultsReleasedAt && <Button loading={act.isPending} color="green" onClick={() => confirm('Release results?',
              `${count((a) => a.status === 'Calibration')} employee(s) will see their final rating and be emailed. Reviews finished later go straight to the employee.`,
              'release')}>Release results</Button>}
            {c.status === 'Review' && <Button loading={act.isPending} color="dark" onClick={() => confirm('Close the cycle?',
              'Nothing in it can change afterwards. Unfinished reviews stay unfinished.', 'close')}>Close cycle</Button>}
          </Group>
        } />
      {warnings.length > 0 && <Alert color="yellow" mb="md" withCloseButton onClose={() => setWarnings([])}>{warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>}
      {c.status === 'Draft' ? <Text c="dimmed">Launch the cycle to create everyone's reviews.</Text> : (
        <Stack>
          <SimpleGrid cols={{ base: 2, md: 4 }}>
            <Stat label="Goals agreed" value={count((a) => a.goalsAgreed)} total={total} />
            <Stat label="Self-reviews done" value={count((a) => a.status === 'ManagerReview' || a.status === 'Completed')} total={c.selfReview ? total : 0} />
            {c.calibrate && !c.resultsReleasedAt
              ? <Stat label="Reviews finished" value={count((a) => a.status === 'Calibration' || a.status === 'Completed')} total={total} />
              : <Stat label="Reviews complete" value={count((a) => a.status === 'Completed')} total={total} />}
            <Stat label="Acknowledged" value={count((a) => a.acknowledged)} total={total} />
          </SimpleGrid>
          {rated > 0 && (
            <Card withBorder>
              <Text fw={600} size="sm" mb="xs">{c.calibrate ? 'Final ratings so far' : 'Ratings so far'}</Text>
              {c.scale.map((label, i) => (
                <Group key={i} gap="xs" wrap="nowrap">
                  <Text size="sm" w={200}>{i + 1} {label}</Text>
                  <Progress value={(c.distribution[i + 1] / rated) * 100} style={{ flex: 1 }} />
                  <Text size="sm" w={30} ta="right">{c.distribution[i + 1]}</Text>
                </Group>
              ))}
            </Card>
          )}
          <Table.ScrollContainer minWidth={720}>
            <Table highlightOnHover>
              <Table.Thead><Table.Tr><Table.Th>Employee</Table.Th><Table.Th>Department</Table.Th><Table.Th>Reviewer</Table.Th><Table.Th>Stage</Table.Th>{c.peerFeedback && <Table.Th>Feedback</Table.Th>}<Table.Th>Rating</Table.Th></Table.Tr></Table.Thead>
              <Table.Tbody>
                {c.appraisals.map((a) => (
                  <Table.Tr key={a.id}>
                    <Table.Td><Anchor component={Link} to={`/reviews/${a.id}`} size="sm">{a.employeeName}</Anchor><Text size="xs" c="dimmed">{a.employeeNumber}</Text></Table.Td>
                    <Table.Td>{a.departmentName ?? '—'}</Table.Td>
                    <Table.Td>
                      <Group gap={4}>
                        <Text size="sm">{a.managerName ?? 'HR'}</Text>
                        {c.status !== 'Closed' && a.status !== 'Completed' && a.status !== 'Calibration' && <Button size="compact-xs" variant="subtle" onClick={() => setReviewer(a)}>Change</Button>}
                      </Group>
                    </Table.Td>
                    <Table.Td><StageBadge a={a} /></Table.Td>
                    {c.peerFeedback && <Table.Td>{a.feedbackRequested ? `${a.feedbackReceived} of ${a.feedbackRequested}` : '—'}</Table.Td>}
                    <Table.Td>
                      <Text size="sm">{ratingText(a.finalRating, c.scale)}</Text>
                      {a.finalRating !== a.overallRating && <Text size="xs" c="dimmed" title={a.calibrationNote ?? undefined}>Manager: {a.overallRating?.toFixed(1)}</Text>}
                    </Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        </Stack>
      )}
      <Modal opened={editing} onClose={() => setEditing(false)} size="lg" title={`Edit ${c.name}`}>
        {editing && <CycleForm cycle={c} done={() => setEditing(false)} />}
      </Modal>
      <Modal opened={!!reviewer} onClose={() => setReviewer(null)} title={reviewer ? `Who reviews ${reviewer.employeeName}?` : ''}>
        {reviewer && <ChangeReviewer row={reviewer} done={() => setReviewer(null)} />}
      </Modal>
    </>
  )
}

function Stat({ label, value, total }: { label: string; value: number; total: number }) {
  if (total === 0) return <Card withBorder><Text size="xs" c="dimmed">{label}</Text><Text fw={600}>—</Text></Card>
  return (
    <Card withBorder>
      <Text size="xs" c="dimmed">{label}</Text>
      <Text fw={600}>{value} of {total}</Text>
      <Progress mt={4} size="sm" value={(value / total) * 100} />
    </Card>
  )
}

function ChangeReviewer({ row, done }: { row: AppraisalRow; done: () => void }) {
  const employees = useActiveEmployees()
  const [managerId, setManagerId] = useState<string | null>(row.managerId)
  const save = useApiMutation(() => api.put(`/appraisals/${row.id}/manager`, { managerId }), { invalidate: ['performance', 'appraisals'], success: 'Reviewer changed.', onSuccess: done })
  return (
    <Stack>
      <Select placeholder="HR" clearable searchable value={managerId} onChange={setManagerId} description="Leave empty for HR to do the manager's review."
        data={(employees.data?.items ?? []).filter((e) => e.id !== row.employeeId).map((e) => ({ value: e.id, label: e.fullName }))} />
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button>
      </Group>
    </Stack>
  )
}
