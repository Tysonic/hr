import { Alert, Button, Card, Group, Modal, SegmentedControl, Stack, Text, Textarea } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { ProbationDue, ProbationOutcome } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { outcomeColor, outcomeLabel } from './components'

export function ProbationPage() {
  return (
    <>
      <PageHeader title="Probation reviews" description="Probations ending in the next 30 days, or already ended, that aren't confirmed. Managers recommend; HR decides." />
      <ProbationList />
    </>
  )
}

const when = (days: number) => (days < 0 ? `ended ${-days} day${days === -1 ? '' : 's'} ago` : days === 0 ? 'ends today' : `ends in ${days} day${days === 1 ? '' : 's'}`)

export function ProbationList() {
  const list = useQuery({ queryKey: ['probation'], queryFn: () => api.get<ProbationDue[]>('/probation') })
  const [acting, setActing] = useState<{ item: ProbationDue; mode: 'recommend' | 'decide' } | null>(null)
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  if (list.data.length === 0) return <Text c="dimmed" ta="center" py="xl">No probations due for review.</Text>
  return (
    <Stack>
      {list.data.map((p) => (
        <Card key={p.contractId} withBorder>
          <Group justify="space-between" wrap="wrap" align="flex-start">
            <Stack gap={0}>
              <Text fw={600} component={Link} to={`/employees/${p.employeeId}`}>{p.employeeName}</Text>
              <Text size="sm" c="dimmed">{[p.positionTitle, p.managerName && `reports to ${p.managerName}`].filter(Boolean).join(' · ')}</Text>
              <Text size="sm">Started {date(p.startDate)}; probation {when(p.daysLeft)} ({date(p.probationEndDate)})</Text>
            </Stack>
            <Group gap="xs">
              {p.canRecommend && <Button size="xs" variant={p.review ? 'default' : 'filled'} onClick={() => setActing({ item: p, mode: 'recommend' })}>
                {p.review ? 'Change recommendation' : 'Recommend'}</Button>}
              {p.canDecide && <Button size="xs" color="green" variant={p.review ? 'filled' : 'light'} onClick={() => setActing({ item: p, mode: 'decide' })}>Decide</Button>}
            </Group>
          </Group>
          {p.review ? (
            <Alert mt="sm" color={outcomeColor[p.review.recommendation]} variant="light"
              title={`${p.review.recommendedBy} recommends: ${outcomeLabel[p.review.recommendation]}${p.review.recommendedExtendTo ? ` to ${date(p.review.recommendedExtendTo)}` : ''}`}>
              <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}>{p.review.comments}</Text>
              <Text size="xs" c="dimmed" mt={4}>{dateTime(p.review.recommendedAt)}</Text>
            </Alert>
          ) : <Text size="sm" c="dimmed" mt="xs">Waiting for the manager's recommendation.</Text>}
          {p.history.filter((h) => h.status === 'Extended').map((h) => (
            <Text key={h.id} size="xs" c="dimmed" mt={4}>Extended from {date(h.probationEndDate)} to {date(h.extendedTo)} by {h.decidedBy}{h.decisionNote ? `: ${h.decisionNote}` : ''}</Text>
          ))}
        </Card>
      ))}
      <Modal opened={!!acting} onClose={() => setActing(null)} size="lg"
        title={acting && (acting.mode === 'recommend' ? `Review ${acting.item.employeeName}'s probation` : `Decide on ${acting.item.employeeName}'s probation`)}>
        {acting && <Act item={acting.item} mode={acting.mode} done={() => setActing(null)} />}
      </Modal>
    </Stack>
  )
}

function Act({ item, mode, done }: { item: ProbationDue; mode: 'recommend' | 'decide'; done: () => void }) {
  const r = item.review
  const [outcome, setOutcome] = useState<ProbationOutcome>(r?.recommendation ?? 'Confirm')
  const [extendTo, setExtendTo] = useState<string | null>(r?.recommendedExtendTo ?? null)
  const [text, setText] = useState(mode === 'recommend' ? r?.comments ?? '' : '')
  const send = useApiMutation(() => mode === 'recommend'
    ? api.post<ProbationDue>(`/probation/${item.contractId}/recommend`, { recommendation: outcome, extendTo: outcome === 'Extend' ? extendTo : null, comments: text })
    : api.post<ProbationDue>(`/probation/${item.contractId}/decide`, { outcome, extendTo: outcome === 'Extend' ? extendTo : null, note: text.trim() || null }), {
    invalidate: ['probation', 'inbox', 'employees'],
    success: mode === 'recommend' ? 'Sent to HR.' : outcome === 'Confirm' ? `${item.employeeName} is confirmed and has been emailed.`
      : outcome === 'Extend' ? 'Probation extended. They have been emailed.' : 'Recorded. Talk to them in person and process their exit.',
    onSuccess: done,
  })
  const needsText = mode === 'recommend' || !r || outcome === 'NotConfirm'
  return (
    <Stack>
      <SegmentedControl fullWidth value={outcome} onChange={(v) => setOutcome(v as ProbationOutcome)}
        data={(['Confirm', 'Extend', 'NotConfirm'] as ProbationOutcome[]).map((o) => ({ value: o, label: outcomeLabel[o] }))} />
      {outcome === 'Extend' && <DateInput label="New probation end" required valueFormat="D MMM YYYY" minDate={item.probationEndDate} value={extendTo} onChange={setExtendTo} />}
      <Textarea label={mode === 'recommend' ? 'How have they done?' : r ? 'Note' : 'Why (there is no manager review)'} required={needsText} autosize minRows={4}
        placeholder={mode === 'recommend' ? 'Performance against the objectives agreed, attendance, conduct' : undefined}
        value={text} onChange={(e) => setText(e.currentTarget.value)} />
      {mode === 'decide' && outcome === 'NotConfirm' && <Alert color="red" variant="light">They won't be emailed: tell them in person.</Alert>}
      {mode === 'decide' && outcome !== 'NotConfirm' && <Text size="xs" c="dimmed">They'll be emailed at their work address.</Text>}
      <Group justify="flex-end">
        <Button variant="default" onClick={done}>Cancel</Button>
        <Button loading={send.isPending} color={mode === 'decide' ? outcomeColor[outcome] : undefined}
          disabled={(needsText && !text.trim()) || (outcome === 'Extend' && !extendTo)} onClick={() => send.mutate(undefined)}>
          {mode === 'recommend' ? 'Send to HR' : outcomeLabel[outcome]}
        </Button>
      </Group>
    </Stack>
  )
}

