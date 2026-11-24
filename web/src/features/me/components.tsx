import { Badge, Stack, Table, Text } from '@mantine/core'
import type { ChangeField, ProfileChangeStatus } from '../../api/types'

export const changeStatusColor: Record<ProfileChangeStatus, string> = { Pending: 'yellow', Approved: 'green', Rejected: 'red', Withdrawn: 'gray' }
export const changeStatusLabel: Record<ProfileChangeStatus, string> = { Pending: 'Waiting for HR', Approved: 'Approved', Rejected: 'Declined', Withdrawn: 'Withdrawn' }

export const ChangeStatusBadge = ({ status }: { status: ProfileChangeStatus }) =>
  <Badge color={changeStatusColor[status]} variant="light">{changeStatusLabel[status]}</Badge>

/** What a request changes: from, to and (for HR) a warning when the record changed since. */
export function ChangeTable({ changes, showCurrent = false }: { changes: ChangeField[]; showCurrent?: boolean }) {
  const v = (s: string | null) => s ?? <Text span c="dimmed">—</Text>
  return (
    <Table.ScrollContainer minWidth={420}>
      <Table withTableBorder>
        <Table.Thead><Table.Tr><Table.Th>Field</Table.Th><Table.Th>From</Table.Th><Table.Th>To</Table.Th></Table.Tr></Table.Thead>
        <Table.Tbody>
          {changes.map((c) => (
            <Table.Tr key={c.label}>
              <Table.Td><Text size="sm" fw={500}>{c.label}</Text></Table.Td>
              <Table.Td>
                <Stack gap={0}>
                  <Text size="sm">{v(c.before)}</Text>
                  {showCurrent && c.current !== c.before && <Text size="xs" c="orange">Now on record: {c.current ?? '—'}</Text>}
                </Stack>
              </Table.Td>
              <Table.Td><Text size="sm" fw={600}>{v(c.proposed)}</Text></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
    </Table.ScrollContainer>
  )
}
