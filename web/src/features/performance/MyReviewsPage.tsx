import { Badge, Card, Group, Stack, Text, Title } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { api } from '../../api/client'
import type { FeedbackRequest, PlanListItem } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import type { AppraisalListItem } from './components'
import { PlanBadge, StageBadge, useMyAppraisals } from './components'

export function MyReviewsPage() {
  const q = useMyAppraisals()
  const plans = useQuery({ queryKey: ['plans'], queryFn: () => api.get<PlanListItem[]>('/improvement-plans'), enabled: !!q.data?.myPlans })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const { mine, team, feedback } = q.data
  const myPlans = (plans.data ?? []).filter((p) => p.isMine && p.status === 'Active')
  return (
    <>
      <PageHeader title="My reviews" description="Your performance reviews, those you do for your team, and feedback colleagues have asked you for." />
      <Stack gap="lg">
        {myPlans.length > 0 && <Plans items={myPlans} />}
        <List title="Mine" items={mine} empty="You have no reviews yet." showName={false} />
        {team.length > 0 && <List title="My team" items={team} empty="" showName />}
        {feedback.length > 0 && <FeedbackList items={feedback} />}
      </Stack>
    </>
  )
}

function Plans({ items }: { items: PlanListItem[] }) {
  const navigate = useNavigate()
  return (
    <Stack gap="xs">
      <Title order={4}>Improvement plan</Title>
      {items.map((p) => (
        <Card key={p.id} withBorder padding="sm" style={{ cursor: 'pointer' }} onClick={() => navigate(`/plans/${p.id}`)}>
          <Group justify="space-between">
            <Stack gap={0}>
              <Text fw={500}>{date(p.startDate)} – {date(p.endDate)}</Text>
              <Text size="xs" c="dimmed">With {p.ownerName ?? 'HR'}</Text>
            </Stack>
            <Group gap="xs">{!p.acknowledged && <Badge color="orange">Please read</Badge>}<PlanBadge value={p.status} /></Group>
          </Group>
        </Card>
      ))}
    </Stack>
  )
}

function FeedbackList({ items }: { items: FeedbackRequest[] }) {
  const navigate = useNavigate()
  return (
    <Stack gap="xs">
      <Title order={4}>Feedback asked of you</Title>
      {items.map((f) => (
        <Card key={f.id} withBorder padding="sm" style={{ cursor: 'pointer' }} onClick={() => navigate(`/feedback/${f.id}`)}>
          <Group justify="space-between" wrap="wrap">
            <Stack gap={0}>
              <Text fw={500}>{f.employeeName}</Text>
              <Text size="xs" c="dimmed">{f.cycleName}{f.open && f.dueOn ? ` · due ${date(f.dueOn)}` : ''}</Text>
            </Stack>
            {f.status === 'Requested'
              ? f.open ? <Badge color="orange">Your turn</Badge> : <Badge variant="light" color="gray">Closed</Badge>
              : <Badge variant="light" color={f.status === 'Submitted' ? 'green' : 'gray'}>{f.status === 'Submitted' ? 'Sent' : 'Declined'}</Badge>}
          </Group>
        </Card>
      ))}
    </Stack>
  )
}

function List({ title, items, empty, showName }: { title: string; items: AppraisalListItem[]; empty: string; showName: boolean }) {
  const navigate = useNavigate()
  return (
    <Stack gap="xs">
      <Title order={4}>{title}</Title>
      {items.length === 0 ? <Text c="dimmed" size="sm">{empty}</Text> : items.map((a) => (
        <Card key={a.id} withBorder padding="sm" style={{ cursor: 'pointer' }} onClick={() => navigate(`/reviews/${a.id}`)}>
          <Group justify="space-between" wrap="wrap">
            <Stack gap={0}>
              <Text fw={500}>{showName ? a.employeeName : a.cycleName}</Text>
              <Text size="xs" c="dimmed">{showName ? a.cycleName : ''}{a.dueOn && a.status !== 'Completed' ? `${showName ? ' · ' : ''}due ${date(a.dueOn)}` : ''}</Text>
            </Stack>
            <Group gap="xs">
              {a.actionDue && <Badge color="orange">Your turn</Badge>}
              <StageBadge a={a} />
              {a.overallRating != null && <Badge variant="outline">{a.overallRating.toFixed(1)}</Badge>}
            </Group>
          </Group>
        </Card>
      ))}
    </Stack>
  )
}
