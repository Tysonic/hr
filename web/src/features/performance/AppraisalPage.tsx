import { ActionIcon, Alert, Anchor, Badge, Button, Card, Group, MultiSelect, NumberInput, Rating, Stack, Table, Text, Textarea, TextInput, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { IconArrowLeft, IconPlus, IconTrash } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import type { Appraisal, Goal, SaveGoal, SaveReviewRequest } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { ratingText, StageBadge, useColleagues } from './components'

export function AppraisalPage() {
  const { id } = useParams()
  const isHr = useCan('performanceManage')
  const q = useQuery({ queryKey: ['appraisals', id], queryFn: () => api.get<Appraisal>(`/appraisals/${id}`) })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const a = q.data
  const reviewer = a.managerName ?? 'HR'
  return (
    <>
      {isHr && a.role === 'Hr'
        ? <Anchor component={Link} to={`/performance/cycles/${a.cycleId}`} size="sm"><Group gap={4}><IconArrowLeft size={14} />{a.cycleName}</Group></Anchor>
        : <Anchor component={Link} to="/reviews" size="sm"><Group gap={4}><IconArrowLeft size={14} />My reviews</Group></Anchor>}
      <PageHeader title={a.role === 'Employee' ? a.cycleName : `${a.employeeName}: ${a.cycleName}`}
        description={`${a.positionTitle ? `${a.positionTitle} · ` : ''}${date(a.periodStart)} – ${date(a.periodEnd)} · reviewed by ${reviewer}`}
        actions={<StageBadge a={{ status: a.status, goalsAgreed: !!a.goalsAgreedAt, goalsSubmitted: !!a.goalsSubmittedAt, acknowledged: !!a.acknowledgedAt }} />} />
      <Stack maw={960}>
        {a.cycleStatus === 'Closed' && <Alert color="gray">This review cycle is closed.</Alert>}
        <Guidance a={a} />
        {a.can.editGoals ? <GoalsEditor a={a} key={a.goals.map((g) => g.id + g.weight + g.title).join()} /> : <GoalsTable a={a} />}
        {a.can.reopenGoals && <ReopenGoals id={a.id} />}
        {a.can.selfReview && <ReviewForm a={a} kind="self" key={`self-${a.selfSubmittedAt}`} />}
        {a.role !== 'Employee' && a.cyclePeerFeedback && (a.feedback.length > 0 || a.can.requestFeedback) && <Feedback a={a} />}
        {a.can.managerReview && <ReviewForm a={a} kind="manager" key={`mgr-${a.managerSubmittedAt}`} />}
        {a.status === 'Calibration' && a.role !== 'Employee' && <Calibration a={a} key={`cal-${a.finalRating}`} />}
        {a.status === 'Completed' && a.finalRating != null && <Result a={a} />}
      </Stack>
    </>
  )
}

function ReopenGoals({ id }: { id: string }) {
  const reopen = useApiMutation(() => api.post<Appraisal>(`/appraisals/${id}/reopen-goals`), { invalidate: ['appraisals', 'performance'], success: 'Goals reopened.' })
  return (
    <Group>
      <Button variant="subtle" color="orange" loading={reopen.isPending} onClick={() => modals.openConfirmModal({
        title: 'Reopen the goals?', children: <Text size="sm">They go back to drafting and need agreeing again. Any self-ratings made so far are cleared.</Text>,
        labels: { confirm: 'Reopen', cancel: 'Cancel' }, confirmProps: { color: 'orange' }, onConfirm: () => reopen.mutate(undefined),
      })}>Reopen goals</Button>
    </Group>
  )
}

/** What happens now, from the user's side. */
function Guidance({ a }: { a: Appraisal }) {
  const me = a.role === 'Employee'
  let text: string | null = null
  if (a.cycleStatus === 'Closed') return null
  if (a.status === 'GoalSetting' && !a.goalsAgreedAt)
    text = me ? (a.goalsSubmittedAt ? `Your goals are with ${a.managerName ?? 'HR'} to agree.` : `Draft your goals with weights adding up to 100%, then send them to ${a.managerName ?? 'HR'}${a.goalsDue ? ` by ${date(a.goalsDue)}` : ''}.`)
      : a.can.agreeGoals ? `Agree ${a.employeeName}'s goals once they add up to 100%. You can edit them first.` : null
  else if (a.status === 'GoalSetting') text = 'Goals are agreed. Reviews open when HR starts them.'
  else if (a.status === 'SelfReview') text = me ? `Rate yourself on each goal${a.reviewsDue ? ` by ${date(a.reviewsDue)}` : ''}.` : `Waiting for ${a.employeeName}'s self-review.`
  else if (a.status === 'ManagerReview') text = a.can.managerReview ? `Rate ${a.employeeName} on each goal and add your overall comments.` : `Waiting for ${a.managerName ?? 'HR'}'s review.`
  else if (a.status === 'Calibration') text = me ? 'Your review is finished. You’ll see the result once HR releases this cycle’s results.'
    : a.can.calibrate ? 'Check the rating against others in the cycle. Results go to employees when HR releases them.' : 'HR is calibrating ratings before results are released.'
  return text ? <Alert variant="light">{text}</Alert> : null
}

function GoalsTable({ a }: { a: Appraisal }) {
  const showSelf = a.goals.some((g) => g.selfRating != null)
  const showMgr = a.goals.some((g) => g.managerRating != null) && a.status === 'Completed'
  if (a.goals.length === 0) return <Text c="dimmed">No goals yet.</Text>
  return (
    <Card withBorder>
      <Title order={5} mb="xs">Goals</Title>
      <Table.ScrollContainer minWidth={560}>
        <Table>
          <Table.Thead><Table.Tr><Table.Th>Goal</Table.Th><Table.Th ta="right">Weight</Table.Th>{showSelf && <Table.Th>Self</Table.Th>}{showMgr && <Table.Th>Manager</Table.Th>}</Table.Tr></Table.Thead>
          <Table.Tbody>
            {a.goals.map((g) => (
              <Table.Tr key={g.id}>
                <Table.Td><Text size="sm">{g.title}</Text>{g.measure && <Text size="xs" c="dimmed">{g.measure}</Text>}</Table.Td>
                <Table.Td ta="right">{g.weight}%</Table.Td>
                {showSelf && <Table.Td><Score value={g.selfRating} comment={g.selfComment} /></Table.Td>}
                {showMgr && <Table.Td><Score value={g.managerRating} comment={g.managerComment} /></Table.Td>}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
    </Card>
  )
}

const Score = ({ value, comment }: { value: number | null; comment: string | null }) => (
  <Stack gap={0}><Rating value={value ?? 0} readOnly size="sm" />{comment && <Text size="xs" c="dimmed">{comment}</Text>}</Stack>
)

function GoalsEditor({ a }: { a: Appraisal }) {
  const [goals, setGoals] = useState<SaveGoal[]>(a.goals.length
    ? a.goals.map((g) => ({ id: g.id, title: g.title, measure: g.measure, weight: g.weight }))
    : [{ id: null, title: '', measure: null, weight: 100 }])
  const total = goals.reduce((s, g) => s + (Number(g.weight) || 0), 0)
  const clean = () => goals.map((g) => ({ ...g, title: g.title.trim(), measure: g.measure?.trim() || null, weight: Number(g.weight) || 0 }))
  const saved = JSON.stringify(clean()) === JSON.stringify(a.goals.map((g) => ({ id: g.id, title: g.title, measure: g.measure, weight: g.weight })))
  const opts = { invalidate: ['appraisals'] }
  const save = useApiMutation(() => api.put<Appraisal>(`/appraisals/${a.id}/goals`, clean()), { ...opts, success: 'Goals saved.' })
  const next = useApiMutation((step: 'submit-goals' | 'agree-goals') => api.post<Appraisal>(`/appraisals/${a.id}/${step}`), {
    ...opts, success: a.can.agreeGoals ? 'Goals agreed.' : 'Sent to your manager.',
  })
  const set = (i: number, patch: Partial<SaveGoal>) => setGoals(goals.map((g, k) => (k === i ? { ...g, ...patch } : g)))
  const valid = goals.length > 0 && goals.every((g) => g.title.trim()) && total === 100
  return (
    <Card withBorder>
      <Group justify="space-between" mb="xs">
        <Title order={5}>Goals</Title>
        <Badge color={total === 100 ? 'green' : 'red'} variant="light">Weights total {total}%</Badge>
      </Group>
      <Stack gap="sm">
        {goals.map((g, i) => (
          <Group key={i} align="flex-start" gap="xs" wrap="nowrap">
            <Stack gap={4} style={{ flex: 1 }}>
              <TextInput placeholder="Goal, e.g. Close the monthly books by the 5th" value={g.title} onChange={(e) => set(i, { title: e.currentTarget.value })} />
              <TextInput size="xs" placeholder="How it's measured (KPI, target)" value={g.measure ?? ''} onChange={(e) => set(i, { measure: e.currentTarget.value })} />
            </Stack>
            <NumberInput w={90} suffix="%" min={1} max={100} value={g.weight} onChange={(v) => set(i, { weight: Number(v) || 0 })} aria-label="Weight" />
            <ActionIcon variant="subtle" color="red" mt={6} aria-label="Remove goal" onClick={() => setGoals(goals.filter((_, k) => k !== i))}><IconTrash size={16} /></ActionIcon>
          </Group>
        ))}
        <Button variant="subtle" w="fit-content" leftSection={<IconPlus size={14} />}
          onClick={() => setGoals([...goals, { id: null, title: '', measure: null, weight: Math.max(0, 100 - total) }])}>Add goal</Button>
      </Stack>
      <Group justify="flex-end" mt="md">
        <Button variant="default" disabled={saved || goals.some((g) => !g.title.trim())} loading={save.isPending} onClick={() => save.mutate(undefined)}>Save draft</Button>
        {a.can.submitGoals && <Button disabled={!saved || !valid} loading={next.isPending} onClick={() => next.mutate('submit-goals')}>Send to {a.managerName ?? 'HR'}</Button>}
        {a.can.agreeGoals && <Button color="green" disabled={!saved || !valid} loading={next.isPending} onClick={() => next.mutate('agree-goals')}>Agree goals</Button>}
      </Group>
      {!saved && <Text size="xs" c="dimmed" ta="right" mt={4}>Save your changes before sending or agreeing.</Text>}
    </Card>
  )
}

function ReviewForm({ a, kind }: { a: Appraisal; kind: 'self' | 'manager' }) {
  const pick = (g: Goal) => (kind === 'self' ? [g.selfRating, g.selfComment] as const : [g.managerRating, g.managerComment] as const)
  const [ratings, setRatings] = useState(() => Object.fromEntries(a.goals.map((g) => [g.id, { rating: pick(g)[0], comment: pick(g)[1] ?? '' }])))
  const [comment, setComment] = useState((kind === 'self' ? a.selfComment : a.managerComment) ?? '')
  const send = useApiMutation((submit: boolean) => api.post<Appraisal>(`/appraisals/${a.id}/${kind === 'self' ? 'self-review' : 'manager-review'}`, {
    ratings: a.goals.map((g) => ({ goalId: g.id, rating: ratings[g.id].rating, comment: ratings[g.id].comment.trim() || null })),
    comment: comment.trim() || null, submit,
  } satisfies SaveReviewRequest), {
    invalidate: ['appraisals', 'performance'],
    success: (r) => r.status !== a.status ? (kind === 'self' ? `Sent to ${a.managerName ?? 'HR'}.` : `Review complete. ${a.employeeName} has been emailed.`) : 'Draft saved.',
  })
  const all = a.goals.every((g) => ratings[g.id].rating)
  const weighted = all ? a.goals.reduce((s, g) => s + g.weight * ratings[g.id].rating!, 0) / 100 : null
  const ready = all && (kind === 'self' || comment.trim())
  return (
    <Card withBorder>
      <Title order={5} mb="xs">{kind === 'self' ? 'Your self-review' : `Your review of ${a.employeeName}`}</Title>
      <Text size="xs" c="dimmed" mb="sm">{a.scale.map((s, i) => `${i + 1} ${s}`).join(' · ')}</Text>
      <Stack>
        {a.goals.map((g) => (
          <Stack key={g.id} gap={4}>
            <Group justify="space-between" wrap="nowrap" align="flex-start">
              <Stack gap={0} style={{ flex: 1 }}>
                <Text size="sm" fw={500}>{g.title} <Text span c="dimmed" size="xs">({g.weight}%)</Text></Text>
                {g.measure && <Text size="xs" c="dimmed">{g.measure}</Text>}
                {kind === 'manager' && g.selfRating != null && <Text size="xs">Self: {g.selfRating} {a.scale[g.selfRating - 1]}{g.selfComment && ` – ${g.selfComment}`}</Text>}
              </Stack>
              <Stack gap={0} align="flex-end">
                <Rating value={ratings[g.id].rating ?? 0} onChange={(v) => setRatings({ ...ratings, [g.id]: { ...ratings[g.id], rating: v || null } })} />
                <Text size="xs" c="dimmed">{ratings[g.id].rating ? a.scale[ratings[g.id].rating! - 1] : 'Not rated'}</Text>
              </Stack>
            </Group>
            <TextInput size="xs" placeholder="Comment (optional)" value={ratings[g.id].comment}
              onChange={(e) => setRatings({ ...ratings, [g.id]: { ...ratings[g.id], comment: e.currentTarget.value } })} />
          </Stack>
        ))}
        {kind === 'manager' && a.selfComment && <Alert variant="light" title={`${a.employeeName}'s comments`}><Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{a.selfComment}</Text></Alert>}
        <Textarea label={kind === 'self' ? 'Overall comments (optional)' : 'Overall comments'} required={kind === 'manager'} autosize minRows={3}
          placeholder={kind === 'self' ? 'Achievements, challenges, support you need' : 'Strengths, areas to develop, and what to focus on next'}
          value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
        {kind === 'manager' && weighted != null && <Text size="sm">Overall rating: <b>{ratingText(Math.round(weighted * 10) / 10, a.scale)}</b></Text>}
        <Group justify="flex-end">
          <Button variant="default" loading={send.isPending} onClick={() => send.mutate(false)}>Save draft</Button>
          <Button disabled={!ready} loading={send.isPending} onClick={() => send.mutate(true)}>{kind === 'self' ? `Send to ${a.managerName ?? 'HR'}` : 'Complete review'}</Button>
        </Group>
      </Stack>
    </Card>
  )
}

function Result({ a }: { a: Appraisal }) {
  const [comment, setComment] = useState('')
  const ack = useApiMutation(() => api.post<Appraisal>(`/appraisals/${a.id}/acknowledge`, { comment: comment.trim() || null }), {
    invalidate: ['appraisals', 'performance'], success: 'Acknowledged.',
  })
  return (
    <Card withBorder>
      <Title order={5}>Result</Title>
      <Text size="xl" fw={700} mt="xs">{ratingText(a.finalRating, a.scale)}</Text>
      <Text size="xs" c="dimmed">
        {a.finalRating === a.overallRating ? `Weighted average of ${a.managerName ?? 'HR'}'s ratings` : 'After calibration'} · {dateTime(a.managerSubmittedAt)}
      </Text>
      {a.role !== 'Employee' && a.finalRating !== a.overallRating && (
        <Text size="xs" c="dimmed">Manager's rating {ratingText(a.overallRating, a.scale)}{a.calibrationNote && `; ${a.calibrationNote}`}</Text>
      )}
      {a.managerComment && <Text size="sm" mt="sm" style={{ whiteSpace: 'pre-wrap' }}>{a.managerComment}</Text>}
      {a.acknowledgedAt
        ? <Text size="sm" mt="sm" c="dimmed">Acknowledged by {a.employeeName} on {dateTime(a.acknowledgedAt)}{a.employeeComment && `: “${a.employeeComment}”`}</Text>
        : a.can.acknowledge ? (
          <Stack mt="md" gap="xs">
            <Textarea label="Your response (optional)" autosize minRows={2} value={comment} onChange={(e) => setComment(e.currentTarget.value)} />
            <Group justify="flex-end"><Button loading={ack.isPending} onClick={() => ack.mutate(undefined)}>I have read my review</Button></Group>
          </Stack>
        ) : <Text size="sm" mt="sm" c="dimmed">Waiting for {a.employeeName} to acknowledge it.</Text>}
    </Card>
  )
}

/** Colleagues' 360° feedback: the reviewer and HR read it and can ask more people. */
function Feedback({ a }: { a: Appraisal }) {
  const colleagues = useColleagues()
  const [ids, setIds] = useState<string[]>([])
  const ask = useApiMutation(() => api.post<Appraisal>(`/appraisals/${a.id}/feedback`, { reviewerIds: ids }), {
    invalidate: ['appraisals', 'performance'], success: 'Asked. They have been emailed.', onSuccess: () => setIds([]),
  })
  const asked = new Set([a.employeeId, a.managerId, ...a.feedback.map((f) => f.reviewerId)])
  const options = (colleagues.data?.items ?? []).filter((e) => !asked.has(e.id) && e.workEmail)
    .map((e) => ({ value: e.id, label: e.positionTitle ? `${e.fullName} (${e.positionTitle})` : e.fullName }))
  return (
    <Card withBorder>
      <Title order={5}>360° feedback</Title>
      <Text size="xs" c="dimmed" mb="sm">Only the reviewer and HR see this. {a.employeeName} doesn't.</Text>
      <Stack gap="sm">
        {a.feedback.map((f) => (
          <Card key={f.id} withBorder padding="xs">
            <Group justify="space-between">
              <Text size="sm" fw={500}>{f.reviewerName}</Text>
              {f.status === 'Submitted' ? <Group gap={4}><Rating value={f.rating ?? 0} readOnly size="xs" /><Text size="xs">{f.rating && a.scale[f.rating - 1]}</Text></Group>
                : <Badge variant="light" color={f.status === 'Declined' ? 'gray' : 'orange'}>{f.status === 'Declined' ? 'Declined' : 'Waiting'}</Badge>}
            </Group>
            {f.strengths && <Text size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}><b>Strengths:</b> {f.strengths}</Text>}
            {f.improvements && <Text size="sm" mt={4} style={{ whiteSpace: 'pre-wrap' }}><b>Could improve:</b> {f.improvements}</Text>}
          </Card>
        ))}
        {a.feedback.length === 0 && <Text size="sm" c="dimmed">Nobody has been asked yet.</Text>}
        {a.can.requestFeedback && (
          <Group align="flex-end">
            <MultiSelect style={{ flex: 1 }} label="Ask colleagues" placeholder="Pick people who work with them" searchable value={ids} onChange={setIds}
              data={options} nothingFoundMessage="No one else to ask" />
            <Button disabled={ids.length === 0} loading={ask.isPending} onClick={() => ask.mutate(undefined)}>Ask</Button>
          </Group>
        )}
      </Stack>
    </Card>
  )
}

/** HR sets the final rating while the cycle is in calibration. */
function Calibration({ a }: { a: Appraisal }) {
  const [rating, setRating] = useState<number | string>(a.finalRating ?? '')
  const [note, setNote] = useState(a.calibrationNote ?? '')
  const save = useApiMutation(() => api.put<Appraisal>(`/appraisals/${a.id}/final-rating`, { rating: Number(rating), note: note.trim() || null }), {
    invalidate: ['appraisals', 'performance'], success: 'Final rating saved.',
  })
  const changed = Number(rating) !== a.overallRating
  return (
    <Card withBorder>
      <Title order={5}>Calibration</Title>
      <Text size="sm" mt="xs">{a.managerName ?? 'HR'}'s rating: <b>{ratingText(a.overallRating, a.scale)}</b></Text>
      {a.managerComment && <Text size="sm" c="dimmed" mt={4} style={{ whiteSpace: 'pre-wrap' }}>{a.managerComment}</Text>}
      {a.can.calibrate ? (
        <Stack mt="sm" gap="xs">
          <NumberInput label="Final rating" w={160} min={1} max={5} step={0.1} decimalScale={1} value={rating} onChange={setRating}
            description={rating !== '' ? ratingText(Number(rating), a.scale) : undefined} />
          <Textarea label="Why it differs from the manager's" required={changed} disabled={!changed} autosize minRows={2} value={changed ? note : ''}
            onChange={(e) => setNote(e.currentTarget.value)} />
          <Group justify="flex-end">
            <Button disabled={rating === '' || (Number(rating) === a.finalRating && (note.trim() || null) === a.calibrationNote) || (changed && !note.trim())}
              loading={save.isPending} onClick={() => save.mutate(undefined)}>Save final rating</Button>
          </Group>
        </Stack>
      ) : <Text size="sm" mt="xs">Final rating: <b>{ratingText(a.finalRating, a.scale)}</b>{a.calibrationNote && ` (${a.calibrationNote})`}</Text>}
    </Card>
  )
}
