import { Badge, Button, Checkbox, Group, Modal, Rating, SegmentedControl, Select, SimpleGrid, Stack, Table, Text, Textarea, Tooltip } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { ClearanceItem, ClearanceOwner, ExitInterview, ExitStatus, ExitType, InterviewAnswers, LeavingReason, MyExit } from '../../api/types'
import { date, dateTime } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

export const exitTypeLabel: Record<ExitType, string> = {
  Resignation: 'Resignation', Termination: 'Termination with notice', Dismissal: 'Summary dismissal', Redundancy: 'Redundancy',
  EndOfContract: 'End of contract', Retirement: 'Retirement', Death: 'Death in service',
}
const statusLabel: Record<ExitStatus, [string, string]> = {
  Requested: ['Awaiting HR', 'orange'], InProgress: ['Working notice', 'blue'], Completed: ['Left', 'gray'], Withdrawn: ['Withdrawn', 'gray'],
}
export const ExitBadge = ({ value }: { value: ExitStatus }) => <Badge variant="light" color={statusLabel[value][1]}>{statusLabel[value][0]}</Badge>

const ownerLabel: Record<ClearanceOwner, [string, string]> = {
  Hr: ['HR', 'indigo'], It: ['IT', 'cyan'], Manager: ['Manager', 'grape'], Finance: ['Finance', 'teal'], Employee: ['Leaver', 'orange'],
}
export const ownerOptions = (Object.keys(ownerLabel) as ClearanceOwner[]).map((k) => ({ value: k, label: ownerLabel[k][0] }))

export const reasonLabel: Record<LeavingReason, string> = {
  Pay: 'Pay and benefits', CareerGrowth: 'Career growth', Management: 'Management', WorkEnvironment: 'Work environment', WorkLifeBalance: 'Work-life balance',
  Relocation: 'Moving away', Studies: 'Studies', Personal: 'Personal or family', Retirement: 'Retirement', Other: 'Something else',
}

export const useMyClearance = (enabled = true) =>
  useQuery({ queryKey: ['me', 'clearance'], queryFn: () => api.get<ClearanceItem[]>('/me/clearance'), enabled, refetchInterval: 300_000 })

export const useMyExit = () =>
  useQuery({ queryKey: ['me', 'exit'], queryFn: async () => (await api.get<MyExit | undefined>('/me/exit')) ?? null })

/** Clearance items with tick boxes for whoever may clear them. */
export function ClearanceTable({ items, showEmployee, onDelete }: { items: ClearanceItem[]; showEmployee?: boolean; onDelete?: (i: ClearanceItem) => void }) {
  const [clearing, setClearing] = useState<ClearanceItem | null>(null)
  const [note, setNote] = useState('')
  const opts = { invalidate: ['exits', 'me'] }
  const clear = useApiMutation(() => api.post<ClearanceItem>(`/me/clearance/${clearing!.id}/clear`, { note: note.trim() || null }), {
    ...opts, success: 'Cleared.', onSuccess: () => setClearing(null),
  })
  const reopen = useApiMutation((i: ClearanceItem) => api.post<ClearanceItem>(`/me/clearance/${i.id}/reopen`), opts)
  return (
    <>
      <Table.ScrollContainer minWidth={560}>
        <Table verticalSpacing="xs">
          <Table.Thead>
            <Table.Tr><Table.Th w={40} />{showEmployee && <Table.Th>Leaver</Table.Th>}<Table.Th>Item</Table.Th><Table.Th>Who</Table.Th><Table.Th>Due</Table.Th>{onDelete && <Table.Th w={40} />}</Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {items.map((i) => (
              <Table.Tr key={i.id} opacity={i.isDone ? 0.6 : 1}>
                <Table.Td>
                  <Tooltip label={i.isDone ? `Cleared by ${i.completedBy}${i.note ? `: ${i.note}` : ''}` : i.canComplete ? 'Mark cleared' : 'Not yours to clear'}>
                    <Checkbox checked={i.isDone} disabled={!i.canComplete} readOnly
                      onClick={() => { if (!i.canComplete) return; if (i.isDone) reopen.mutate(i); else { setNote(''); setClearing(i) } }} />
                  </Tooltip>
                </Table.Td>
                {showEmployee && <Table.Td><Text size="sm">{i.employeeName}</Text><Text size="xs" c="dimmed">last day {date(i.lastWorkingDay)}</Text></Table.Td>}
                <Table.Td>
                  <Text size="sm" td={i.isDone ? 'line-through' : undefined}>{i.title}</Text>
                  {i.description && <Text size="xs" c="dimmed">{i.description}</Text>}
                  {i.isDone && i.note && <Text size="xs" c="dimmed">Note: {i.note}</Text>}
                </Table.Td>
                <Table.Td>
                  <Badge size="sm" variant="light" color={ownerLabel[i.owner][1]}>{ownerLabel[i.owner][0]}</Badge>
                  <Text size="xs" c="dimmed">{i.assigneeName ?? (i.owner === 'Finance' ? 'Payroll' : 'HR')}</Text>
                </Table.Td>
                <Table.Td><Text size="sm" c={i.isOverdue ? 'red' : undefined}>{date(i.dueOn)}{i.isOverdue && ' (late)'}</Text></Table.Td>
                {onDelete && <Table.Td>{!i.isDone && <Button size="compact-xs" variant="subtle" color="red" onClick={() => onDelete(i)}>Remove</Button>}</Table.Td>}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={!!clearing} onClose={() => setClearing(null)} title={clearing?.title}>
        <Stack>
          <Textarea label="Note (optional)" placeholder="e.g. Laptop serial 5CG123 returned" autosize minRows={2} value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setClearing(null)}>Cancel</Button>
            <Button loading={clear.isPending} onClick={() => clear.mutate(undefined)}>Mark cleared</Button>
          </Group>
        </Stack>
      </Modal>
    </>
  )
}

const ratings: [keyof InterviewAnswers, string][] = [
  ['roleRating', 'The job itself'], ['managerRating', 'Your manager'], ['payRating', 'Pay and benefits'], ['growthRating', 'Training and growth'], ['cultureRating', 'Culture and colleagues'],
]
const empty: InterviewAnswers = {
  mainReason: null, roleRating: null, managerRating: null, payRating: null, growthRating: null, cultureRating: null,
  wouldRecommend: null, wouldReturn: null, wentWell: null, couldImprove: null, comments: null,
}
const yesNo = (v: boolean | null) => (v == null ? '' : v ? 'yes' : 'no')

/** The exit interview, filled in by the leaver or recorded by HR. */
export function InterviewForm({ initial, path, submitLabel, done }: { initial?: InterviewAnswers; path: string; submitLabel: string; done?: () => void }) {
  const [a, setA] = useState<InterviewAnswers>(initial ?? empty)
  const save = useApiMutation(() => api.put(path, {
    ...a, wentWell: a.wentWell?.trim() || null, couldImprove: a.couldImprove?.trim() || null, comments: a.comments?.trim() || null,
  }), { invalidate: ['exits', 'me'], success: 'Thank you. Your answers have been saved.', onSuccess: done })
  return (
    <Stack>
      <Select label="Main reason for leaving" required data={(Object.keys(reasonLabel) as LeavingReason[]).map((k) => ({ value: k, label: reasonLabel[k] }))}
        value={a.mainReason} onChange={(v) => setA({ ...a, mainReason: v as LeavingReason | null })} />
      <Text size="sm" fw={500}>How would you rate…</Text>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing="xs">
        {ratings.map(([k, label]) => (
          <Group key={k} justify="space-between" wrap="nowrap">
            <Text size="sm">{label}</Text>
            <Rating value={(a[k] as number | null) ?? 0} onChange={(v) => setA({ ...a, [k]: v || null })} />
          </Group>
        ))}
      </SimpleGrid>
      <Group grow>
        <Stack gap={4}><Text size="sm">Would you recommend working here?</Text>
          <SegmentedControl value={yesNo(a.wouldRecommend)} onChange={(v) => setA({ ...a, wouldRecommend: v === 'yes' })} data={[{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }]} /></Stack>
        <Stack gap={4}><Text size="sm">Would you come back?</Text>
          <SegmentedControl value={yesNo(a.wouldReturn)} onChange={(v) => setA({ ...a, wouldReturn: v === 'yes' })} data={[{ value: 'yes', label: 'Yes' }, { value: 'no', label: 'No' }]} /></Stack>
      </Group>
      <Textarea label="What went well?" autosize minRows={2} maxLength={3000} value={a.wentWell ?? ''} onChange={(e) => setA({ ...a, wentWell: e.currentTarget.value })} />
      <Textarea label="What could we do better?" autosize minRows={2} maxLength={3000} value={a.couldImprove ?? ''} onChange={(e) => setA({ ...a, couldImprove: e.currentTarget.value })} />
      <Textarea label="Anything else?" autosize minRows={2} maxLength={3000} value={a.comments ?? ''} onChange={(e) => setA({ ...a, comments: e.currentTarget.value })} />
      <Group justify="flex-end"><Button disabled={!a.mainReason} loading={save.isPending} onClick={() => save.mutate(undefined)}>{submitLabel}</Button></Group>
    </Stack>
  )
}

export function InterviewView({ interview }: { interview: ExitInterview }) {
  const a = interview.answers
  return (
    <Stack gap="xs">
      <Text size="sm"><b>Main reason:</b> {a.mainReason ? reasonLabel[a.mainReason] : '—'}</Text>
      <SimpleGrid cols={{ base: 1, sm: 2 }} spacing={4}>
        {ratings.map(([k, label]) => (
          <Group key={k} justify="space-between" wrap="nowrap"><Text size="sm">{label}</Text><Rating value={(a[k] as number | null) ?? 0} readOnly size="sm" /></Group>
        ))}
      </SimpleGrid>
      <Text size="sm">Would recommend us: <b>{a.wouldRecommend == null ? '—' : a.wouldRecommend ? 'Yes' : 'No'}</b> · Would come back: <b>{a.wouldReturn == null ? '—' : a.wouldReturn ? 'Yes' : 'No'}</b></Text>
      {a.wentWell && <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}><b>Went well:</b> {a.wentWell}</Text>}
      {a.couldImprove && <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}><b>Could do better:</b> {a.couldImprove}</Text>}
      {a.comments && <Text size="sm" style={{ whiteSpace: 'pre-wrap' }}><b>Other:</b> {a.comments}</Text>}
      <Text size="xs" c="dimmed">{interview.byEmployee ? 'Filled in by the leaver' : `Recorded by ${interview.submittedBy}`} · {dateTime(interview.submittedAt)}</Text>
    </Stack>
  )
}
