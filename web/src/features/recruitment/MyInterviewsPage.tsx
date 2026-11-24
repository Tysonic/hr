import { Alert, Badge, Button, Card, Drawer, Group, Rating, SegmentedControl, Stack, Text, Textarea } from '@mantine/core'
import { IconDownload } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, download } from '../../api/client'
import type { Interview, Recommendation } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'
import { useMyInterviews } from '../me/hooks'
import { recommendationLabel, Scorecards, when } from './hiring'

export function MyInterviewsPage() {
  const list = useMyInterviews()
  const [open, setOpen] = useState<string | null>(null)
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  const now = new Date()
  return (
    <>
      <PageHeader title="My interviews" description="Candidates you're interviewing. Send your scorecard after each interview." />
      {list.data.length === 0 ? <Text c="dimmed">You're not on any interview panels.</Text> : (
        <Stack>
          {list.data.map((i) => {
            const started = new Date(i.startsAt) <= now
            return (
              <Card key={i.id} withBorder padding="sm" style={{ cursor: 'pointer' }} onClick={() => setOpen(i.id)}>
                <Group justify="space-between" wrap="wrap">
                  <Stack gap={0}>
                    <Text fw={500}>{i.candidateName} · {i.jobTitle}</Text>
                    <Text size="sm" c="dimmed">{when(i.localStart)} · {i.durationMinutes} min{i.location && ` · ${i.location}`}</Text>
                  </Stack>
                  {i.myScorecard ? <Badge color="green" variant="light">Scorecard sent</Badge>
                    : started ? <Badge color="orange">Scorecard due</Badge> : <Badge variant="light">Upcoming</Badge>}
                </Group>
              </Card>
            )
          })}
        </Stack>
      )}
      <Drawer opened={!!open} onClose={() => setOpen(null)} position="right" size="lg" title="Interview">
        {open && <InterviewView id={open} />}
      </Drawer>
    </>
  )
}

const recommendations: Recommendation[] = ['StrongNo', 'No', 'Yes', 'StrongYes']

function InterviewView({ id }: { id: string }) {
  const q = useQuery({ queryKey: ['me', 'interviews', id], queryFn: () => api.get<Interview>(`/me/interviews/${id}`) })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const i = q.data
  const started = new Date(i.startsAt) <= new Date()
  return (
    <Stack>
      <Stack gap={0}>
        <Text fw={600} size="lg">{i.candidateName}</Text>
        <Text size="sm" c="dimmed">{i.jobTitle} · {when(i.localStart)} · {i.durationMinutes} min{i.location && ` · ${i.location}`}</Text>
        <Text size="xs" c="dimmed">Panel: {i.panel.map((p) => p.name).join(', ')}</Text>
      </Stack>
      {i.cvFileName && <Button variant="default" size="xs" w="fit-content" leftSection={<IconDownload size={14} />}
        onClick={() => void download(`/me/interviews/${i.id}/cv`, i.cvFileName!)}>{i.cvFileName}</Button>}
      {i.coverLetter && <Card withBorder padding="sm"><Text size="xs" c="dimmed" mb={4}>Cover letter</Text><Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{i.coverLetter}</Text></Card>}
      {i.status === 'Cancelled' ? <Alert color="gray">This interview was cancelled.</Alert>
        : !started ? <Alert color="blue">You can send your scorecard once the interview starts.</Alert>
        : <ScorecardForm interview={i} key={i.myScorecard?.submittedAt ?? 'new'} />}
      {i.scorecards.length > 0 && <><Text fw={600} size="sm">Everyone's scores</Text><Scorecards interview={i} /></>}
      {!i.myScorecard && started && i.status !== 'Cancelled' && <Text size="xs" c="dimmed">The other interviewers' scores show once you've sent yours.</Text>}
    </Stack>
  )
}

function ScorecardForm({ interview: i }: { interview: Interview }) {
  const mine = i.myScorecard
  const [scores, setScores] = useState<Record<string, number>>(() => Object.fromEntries(i.criteria.map((c) => [c, mine?.scores.find((s) => s.criterion === c)?.score ?? 0])))
  const [rec, setRec] = useState<Recommendation | null>(mine?.recommendation ?? null)
  const [comments, setComments] = useState(mine?.comments ?? '')
  const save = useApiMutation(() => api.put<Interview>(`/me/interviews/${i.id}/scorecard`, {
    scores: i.criteria.map((c) => ({ criterion: c, score: scores[c], comment: null })), recommendation: rec, comments: comments.trim() || null,
  }), { invalidate: ['me', 'recruitment'], success: 'Scorecard sent. Thank you.' })
  const complete = i.criteria.every((c) => scores[c] > 0) && rec
  return (
    <Card withBorder>
      <Stack gap="sm">
        <Text fw={600} size="sm">{mine ? 'Your scorecard' : 'Your scorecard (1 = poor, 5 = excellent)'}</Text>
        {i.criteria.map((c) => (
          <Group key={c} justify="space-between">
            <Text size="sm">{c}</Text>
            <Rating value={scores[c]} onChange={(v) => setScores({ ...scores, [c]: v })} />
          </Group>
        ))}
        <Stack gap={4}>
          <Text size="sm">Would you hire them?</Text>
          <SegmentedControl fullWidth value={rec ?? ''} onChange={(v) => setRec(v as Recommendation)}
            data={recommendations.map((r) => ({ value: r, label: recommendationLabel[r] }))} />
        </Stack>
        <Textarea label="Comments" autosize minRows={3} value={comments} onChange={(e) => setComments(e.currentTarget.value)}
          placeholder="Strengths, concerns, anything HR should follow up" />
        <Group justify="flex-end">
          <Button disabled={!complete} loading={save.isPending} onClick={() => save.mutate(undefined)}>{mine ? 'Update scorecard' : 'Send scorecard'}</Button>
        </Group>
      </Stack>
    </Card>
  )
}
