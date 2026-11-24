import { Alert, Anchor, Button, Card, Group, Modal, SegmentedControl, Stack, Text, Textarea, Timeline, Title } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { IconArrowLeft } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import type { ImprovementPlan, PlanProgress, PlanStatus } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { PlanBadge, progressLabel, ProgressBadge } from './components'
import { PlanForm } from './PlansPage'

export function PlanPage() {
  const { id } = useParams()
  const canList = useCan('plans')
  const [editing, setEditing] = useState(false)
  const [closing, setClosing] = useState(false)
  const q = useQuery({ queryKey: ['plans', id], queryFn: () => api.get<ImprovementPlan>(`/improvement-plans/${id}`) })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const p = q.data
  return (
    <>
      {p.isMine || !canList
        ? <Anchor component={Link} to="/reviews" size="sm"><Group gap={4}><IconArrowLeft size={14} />My reviews</Group></Anchor>
        : <Anchor component={Link} to="/plans" size="sm"><Group gap={4}><IconArrowLeft size={14} />Improvement plans</Group></Anchor>}
      <PageHeader title={p.isMine ? 'Your improvement plan' : `Improvement plan: ${p.employeeName}`}
        description={`${date(p.startDate)} – ${date(p.endDate)} · owner ${p.ownerName ?? 'HR'}${p.status === 'Active' ? ` · ${p.daysLeft >= 0 ? `${p.daysLeft} days left` : `ended ${-p.daysLeft} days ago`}` : ''}`}
        actions={
          <Group>
            <PlanBadge value={p.status} />
            {p.canManage && <Button variant="default" onClick={() => setEditing(true)}>Edit</Button>}
            {p.canManage && <Button color="dark" onClick={() => setClosing(true)}>Record outcome</Button>}
          </Group>
        } />
      <Stack maw={860}>
        {p.status !== 'Active' && (
          <Alert color={p.status === 'Succeeded' ? 'green' : 'gray'} title="Outcome">
            <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{p.outcome}</Text>
            <Text size="xs" c="dimmed" mt={4}>{p.closedBy} · {dateTime(p.closedAt)}</Text>
          </Alert>
        )}
        <Card withBorder>
          <Title order={5}>Why</Title>
          <Text size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{p.reason}</Text>
          <Title order={5} mt="md">Objectives</Title>
          <Stack gap={6} mt={4}>
            {p.objectives.map((o, i) => (
              <div key={i}><Text size="sm" fw={500}>{i + 1}. {o.objective}</Text>{o.measure && <Text size="xs" c="dimmed">Measured by: {o.measure}</Text>}</div>
            ))}
          </Stack>
          {p.support && <><Title order={5} mt="md">Support</Title><Text size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{p.support}</Text></>}
        </Card>
        <Acknowledgement p={p} />
        <Card withBorder>
          <Title order={5} mb="sm">Check-ins</Title>
          {p.canManage && <CheckInForm p={p} />}
          {p.checkIns.length === 0 ? <Text size="sm" c="dimmed">No check-ins yet.</Text> : (
            <Timeline bulletSize={14} lineWidth={2} mt="sm">
              {p.checkIns.map((c) => (
                <Timeline.Item key={c.id} color={progressLabel[c.progress][1]}
                  title={<Group gap="xs"><Text size="sm" fw={500}>{date(c.on)}</Text><ProgressBadge value={c.progress} /></Group>}>
                  <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{c.note}</Text>
                  <Text size="xs" c="dimmed">{c.by}</Text>
                </Timeline.Item>
              ))}
            </Timeline>
          )}
        </Card>
      </Stack>
      <Modal opened={editing} onClose={() => setEditing(false)} size="lg" title="Edit plan">
        {editing && <PlanForm plan={p} done={() => setEditing(false)} />}
      </Modal>
      <Modal opened={closing} onClose={() => setClosing(false)} title="Record the outcome">
        {closing && <CloseForm id={p.id} done={() => setClosing(false)} />}
      </Modal>
    </>
  )
}

function Acknowledgement({ p }: { p: ImprovementPlan }) {
  const [comment, setComment] = useState('')
  const ack = useApiMutation(() => api.post<ImprovementPlan>(`/improvement-plans/${p.id}/acknowledge`, { comment: comment.trim() || null }), {
    invalidate: ['plans', 'appraisals'], success: 'Acknowledged.',
  })
  if (p.acknowledgedAt)
    return <Text size="sm" c="dimmed">Acknowledged by {p.employeeName} on {dateTime(p.acknowledgedAt)}{p.employeeComment && `: “${p.employeeComment}”`}</Text>
  if (!p.isMine) return <Text size="sm" c="dimmed">{p.employeeName} hasn't acknowledged the plan yet.</Text>
  return (
    <Card withBorder>
      <Text size="sm">Please read the plan and confirm you've seen it. Acknowledging doesn't mean you agree with everything; add your view below if you want.</Text>
      <Textarea mt="sm" label="Your comments (optional)" autosize minRows={2} value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
      <Group justify="flex-end" mt="sm"><Button loading={ack.isPending} onClick={() => ack.mutate(undefined)}>I have read this plan</Button></Group>
    </Card>
  )
}

function CheckInForm({ p }: { p: ImprovementPlan }) {
  const [on, setOn] = useState<string | null>(dayjs().format('YYYY-MM-DD'))
  const [progress, setProgress] = useState<PlanProgress>('OnTrack')
  const [note, setNote] = useState('')
  const add = useApiMutation(() => api.post<ImprovementPlan>(`/improvement-plans/${p.id}/check-ins`, { on, progress, note: note.trim() }), {
    invalidate: ['plans'], success: 'Check-in recorded.', onSuccess: () => setNote(''),
  })
  return (
    <Stack gap="xs" mb="md">
      <Group align="flex-end">
        <DateInput label="Date" w={160} valueFormat="D MMM YYYY" value={on} onChange={setOn} maxDate={dayjs().format('YYYY-MM-DD')} />
        <SegmentedControl value={progress} onChange={(v) => setProgress(v as PlanProgress)}
          data={(Object.keys(progressLabel) as PlanProgress[]).map((k) => ({ value: k, label: progressLabel[k][0] }))} />
      </Group>
      <Textarea placeholder="What was discussed, progress on each objective, next steps" autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Group justify="flex-end"><Button size="xs" disabled={!on || !note.trim()} loading={add.isPending} onClick={() => add.mutate(undefined)}>Add check-in</Button></Group>
    </Stack>
  )
}

function CloseForm({ id, done }: { id: string; done: () => void }) {
  const [outcome, setOutcome] = useState<PlanStatus>('Succeeded')
  const [note, setNote] = useState('')
  const close = useApiMutation(() => api.post<ImprovementPlan>(`/improvement-plans/${id}/close`, { outcome, note: note.trim() }), {
    invalidate: ['plans', 'appraisals'], success: 'Outcome recorded.', onSuccess: done,
  })
  return (
    <Stack>
      <SegmentedControl value={outcome} onChange={(v) => setOutcome(v as PlanStatus)}
        data={[{ value: 'Succeeded', label: 'Succeeded' }, { value: 'NotSucceeded', label: 'Not succeeded' }, { value: 'Cancelled', label: 'Cancelled' }]} />
      <Textarea label="Outcome" required autosize minRows={3} placeholder="How the objectives were met, and what happens next" value={note} onChange={(e) => setNote(e.currentTarget.value)} />
      <Text size="xs" c="dimmed">The plan can't be changed afterwards.</Text>
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button disabled={!note.trim()} loading={close.isPending} onClick={() => close.mutate(undefined)}>Record outcome</Button>
      </Group>
    </Stack>
  )
}
