import { Anchor, Badge, Button, Group, SegmentedControl, Table, Text } from '@mantine/core'
import { IconMailForward } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { ExpiringItem, ReminderKind, ReminderRunResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'

const kinds: Record<ReminderKind, { label: string; color: string }> = {
  DocumentExpiry: { label: 'Document', color: 'blue' },
  ContractEnd: { label: 'Contract', color: 'grape' },
  ProbationEnd: { label: 'Probation', color: 'teal' },
}

function when(days: number) {
  if (days < 0) return <Text size="sm" c="red" fw={500}>{-days} day{days === -1 ? '' : 's'} ago</Text>
  if (days === 0) return <Text size="sm" c="red" fw={500}>Today</Text>
  return <Text size="sm" c={days <= 7 ? 'orange' : undefined} fw={days <= 7 ? 500 : undefined}>in {days} day{days === 1 ? '' : 's'}</Text>
}

/** Documents expiring, contracts ending with nothing after them, and probations not yet confirmed. */
export function ExpiriesPage() {
  const [within, setWithin] = useState('60')
  const canSend = useCan('companySettings')
  const items = useQuery({ queryKey: ['expiries', within], queryFn: () => api.get<ExpiringItem[]>('/expiries', { withinDays: within }) })
  const send = useApiMutation(() => api.post<ReminderRunResult>('/expiries/send-reminders'), {
    success: (r) => !r.mailConfigured ? 'No mail server is configured, so nothing was sent.'
      : r.itemsReminded === 0 ? 'Nothing new to remind about; every reminder due has been sent.'
        : `Reminder about ${r.itemsReminded} item${r.itemsReminded === 1 ? '' : 's'} sent to ${r.sentTo}.`,
  })

  return (
    <>
      <PageHeader title="Expiring soon"
        description="Reminders go to the company's HR email 30 days and 7 days before, and on the day."
        actions={canSend && <Button variant="default" leftSection={<IconMailForward size={16} />} loading={send.isPending} onClick={() => send.mutate(undefined)}>Send reminders now</Button>} />
      <SegmentedControl mb="md" value={within} onChange={setWithin}
        data={[{ value: '30', label: 'Next 30 days' }, { value: '60', label: '60 days' }, { value: '90', label: '90 days' }, { value: '180', label: '6 months' }]} />
      {items.isPending ? <Loading /> : items.isError ? <LoadError error={items.error} /> : (
        <Table.ScrollContainer minWidth={700}>
          <Table striped highlightOnHover maw={1100}>
            <Table.Thead>
              <Table.Tr><Table.Th>Due</Table.Th><Table.Th>When</Table.Th><Table.Th>What</Table.Th><Table.Th>Employee</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {items.data.length === 0 && (
                <Table.Tr><Table.Td colSpan={4}><Text c="dimmed" ta="center" py="md">Nothing expiring or ending in this period.</Text></Table.Td></Table.Tr>
              )}
              {items.data.map((i) => (
                <Table.Tr key={`${i.kind}-${i.itemId}`}>
                  <Table.Td>{date(i.dueOn)}</Table.Td>
                  <Table.Td>{when(i.daysLeft)}</Table.Td>
                  <Table.Td><Group gap="xs"><Badge size="sm" variant="light" color={kinds[i.kind].color}>{kinds[i.kind].label}</Badge>{i.title}</Group></Table.Td>
                  <Table.Td><Anchor component={Link} to={`/employees/${i.employeeId}`} size="sm">{i.employeeName}</Anchor> <Text span size="xs" c="dimmed">{i.employeeNumber}</Text></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </>
  )
}
