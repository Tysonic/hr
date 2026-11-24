import { Alert, Anchor, Button, Card, Group, Rating, Stack, Text, Textarea, Title } from '@mantine/core'
import { modals } from '@mantine/modals'
import { IconArrowLeft } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { api } from '../../api/client'
import type { FeedbackRequest } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

/** A colleague gives 360° feedback on someone's review. */
export function FeedbackPage() {
  const { id } = useParams()
  const q = useQuery({ queryKey: ['appraisals', 'feedback', id], queryFn: () => api.get<FeedbackRequest>(`/me/feedback/${id}`) })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const f = q.data
  return (
    <>
      <Anchor component={Link} to="/reviews" size="sm"><Group gap={4}><IconArrowLeft size={14} />My reviews</Group></Anchor>
      <PageHeader title={`Feedback on ${f.employeeName}`} description={`${f.positionTitle ? `${f.positionTitle} · ` : ''}${f.cycleName}${f.dueOn ? ` · due ${date(f.dueOn)}` : ''}`} />
      <Stack maw={760}>
        <Alert variant="light">Your answers go to {f.employeeName}'s reviewer and HR only. {f.employeeName} won't see them.</Alert>
        {f.goals.length > 0 && (
          <Card withBorder>
            <Title order={5} mb="xs">Their goals this period</Title>
            {f.goals.map((g) => <Text key={g} size="sm">• {g}</Text>)}
          </Card>
        )}
        {f.status === 'Requested' && f.open ? <Form f={f} /> : <Answer f={f} />}
      </Stack>
    </>
  )
}

function Answer({ f }: { f: FeedbackRequest }) {
  if (f.status === 'Declined') return <Text c="dimmed">You declined this request.</Text>
  if (f.status === 'Requested') return <Text c="dimmed">This review has moved on, so feedback is closed.</Text>
  return (
    <Card withBorder>
      <Title order={5}>Your feedback</Title>
      <Group gap="xs" mt="xs"><Rating value={f.rating ?? 0} readOnly /><Text size="sm">{f.rating && f.scale[f.rating - 1]}</Text></Group>
      {f.strengths && <Text size="sm" mt="sm" style={{ whiteSpace: 'pre-wrap' }}><b>Strengths:</b> {f.strengths}</Text>}
      {f.improvements && <Text size="sm" mt="sm" style={{ whiteSpace: 'pre-wrap' }}><b>Could improve:</b> {f.improvements}</Text>}
    </Card>
  )
}

function Form({ f }: { f: FeedbackRequest }) {
  const [rating, setRating] = useState(0)
  const [strengths, setStrengths] = useState('')
  const [improvements, setImprovements] = useState('')
  const opts = { invalidate: ['appraisals'] }
  const send = useApiMutation(() => api.post<FeedbackRequest>(`/me/feedback/${f.id}`, {
    rating, strengths: strengths.trim() || null, improvements: improvements.trim() || null,
  }), { ...opts, success: 'Thank you. Your feedback has been sent.' })
  const decline = useApiMutation(() => api.post<FeedbackRequest>(`/me/feedback/${f.id}/decline`), { ...opts, success: 'Declined.' })
  return (
    <Card withBorder>
      <Stack>
        <Stack gap={4}>
          <Text size="sm" fw={500}>Overall, how do they perform?</Text>
          <Group gap="xs"><Rating value={rating} onChange={setRating} /><Text size="sm" c="dimmed">{rating ? f.scale[rating - 1] : 'Not rated'}</Text></Group>
        </Stack>
        <Textarea label="What do they do well?" autosize minRows={3} maxLength={3000} value={strengths} onChange={(e) => setStrengths(e.currentTarget.value)} />
        <Textarea label="What could they do better?" autosize minRows={3} maxLength={3000} value={improvements} onChange={(e) => setImprovements(e.currentTarget.value)} />
        <Group justify="space-between">
          <Button variant="subtle" color="gray" loading={decline.isPending} onClick={() => modals.openConfirmModal({
            title: 'Decline this request?', children: <Text size="sm">Do this if you don't work closely enough with {f.employeeName} to say.</Text>,
            labels: { confirm: 'Decline', cancel: 'Cancel' }, onConfirm: () => decline.mutate(undefined),
          })}>I can't give feedback</Button>
          <Button disabled={!rating || !(strengths.trim() || improvements.trim())} loading={send.isPending} onClick={() => send.mutate(undefined)}>Send feedback</Button>
        </Group>
      </Stack>
    </Card>
  )
}
