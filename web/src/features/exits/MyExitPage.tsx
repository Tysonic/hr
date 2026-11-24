import { Alert, Anchor, Button, Card, Group, Stack, Text, Textarea, Title } from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { modals } from '@mantine/modals'
import { IconArrowLeft } from '@tabler/icons-react'
import dayjs from 'dayjs'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { MyExit } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { ClearanceTable, InterviewForm, InterviewView, useMyExit } from './components'

/** Handing in a resignation, then the leaver's checklist and exit interview. */
export function MyExitPage() {
  const q = useMyExit()
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const x = q.data
  return (
    <>
      <Anchor component={Link} to="/me" size="sm"><Group gap={4}><IconArrowLeft size={14} />My profile</Group></Anchor>
      <PageHeader title="Leaving" description={x ? undefined : 'Hand in your resignation. Talk to your manager first; HR confirms your last working day.'} />
      <Stack maw={860}>{x ? <Exit x={x} /> : <Resign />}</Stack>
    </>
  )
}

function Resign() {
  const [lastDay, setLastDay] = useState<string | null>(null)
  const [reason, setReason] = useState('')
  const send = useApiMutation(() => api.post<MyExit>('/me/resignation', { lastWorkingDay: lastDay, reason: reason.trim() || null }), {
    invalidate: ['me'], success: 'Your resignation has been sent to HR. Your manager has been told.',
  })
  return (
    <Card withBorder>
      <Stack>
        <DateInput label="Proposed last working day" required valueFormat="D MMM YYYY" minDate={dayjs().format('YYYY-MM-DD')} value={lastDay} onChange={setLastDay}
          description="Your contract sets how much notice you give; HR will confirm the date." />
        <Textarea label="Reason (optional)" autosize minRows={3} value={reason} onChange={(e) => setReason(e.currentTarget.value)} />
        <Group justify="flex-end">
          <Button color="red" disabled={!lastDay} loading={send.isPending} onClick={() => modals.openConfirmModal({
            title: 'Hand in your resignation?', children: <Text size="sm">HR and your manager are told straight away. You can withdraw it until HR accepts it.</Text>,
            labels: { confirm: 'Resign', cancel: 'Cancel' }, confirmProps: { color: 'red' }, onConfirm: () => send.mutate(undefined),
          })}>Hand in resignation</Button>
        </Group>
      </Stack>
    </Card>
  )
}

function Exit({ x }: { x: MyExit }) {
  const withdraw = useApiMutation(() => api.post(`/me/exit/${x.id}/withdraw`, { note: null }), { invalidate: ['me'], success: 'Resignation withdrawn.' })
  return (
    <>
      {x.status === 'Requested' ? (
        <Alert variant="light" title="Waiting for HR">
          <Text size="sm">You handed in your resignation on {date(x.noticeGivenOn)}, proposing {date(x.lastWorkingDay)} as your last working day. HR will confirm it.</Text>
          {x.canWithdraw && <Button mt="sm" size="xs" variant="default" loading={withdraw.isPending} onClick={() => withdraw.mutate(undefined)}>Withdraw my resignation</Button>}
        </Alert>
      ) : (
        <Alert variant="light" title={`Your last working day is ${date(x.lastWorkingDay)}`}>
          <Text size="sm">Please finish the items below before then. Talk to HR if anything changes.</Text>
        </Alert>
      )}
      {x.clearance.length > 0 && <Card withBorder><Title order={5} mb="xs">Your checklist</Title><ClearanceTable items={x.clearance} /></Card>}
      {x.status === 'InProgress' && (
        <Card withBorder>
          <Title order={5}>Exit interview</Title>
          <Text size="xs" c="dimmed" mb="sm">Only HR reads this; your manager doesn't. Honest answers help us improve. Once sent, it can't be changed.</Text>
          {x.interview ? <InterviewView interview={x.interview} /> : x.canInterview
            ? <InterviewForm path="/me/exit/interview" submitLabel="Send to HR" /> : <Text size="sm" c="dimmed">HR has recorded your interview.</Text>}
        </Card>
      )}
    </>
  )
}
