import { Badge, Code, Group, Pagination, Popover, Table, Text, UnstyledButton } from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { AuditEntry, PagedResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { dateTime, words } from '../../lib/format'

const actionColor = { created: 'green', updated: 'blue', deleted: 'red' } as const

export function AuditPage() {
  const [page, setPage] = useState(1)
  const audit = useQuery({
    queryKey: ['audit', page],
    queryFn: () => api.get<PagedResult<AuditEntry>>('/audit', { page, pageSize: 50 }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader title="Audit trail" description="Every record created, changed or deleted, by whom and when." />
      {audit.isPending ? <Loading /> : audit.isError ? <LoadError error={audit.error} /> : (
        <>
          <Table.ScrollContainer minWidth={720}>
            <Table striped>
              <Table.Thead>
                <Table.Tr><Table.Th>When</Table.Th><Table.Th>Who</Table.Th><Table.Th>Record</Table.Th><Table.Th>Action</Table.Th><Table.Th>Changes</Table.Th></Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {audit.data.items.length === 0 && (
                  <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" ta="center" py="md">Nothing recorded yet.</Text></Table.Td></Table.Tr>
                )}
                {audit.data.items.map((e) => (
                  <Table.Tr key={e.id}>
                    <Table.Td>{dateTime(e.at)}</Table.Td>
                    <Table.Td>{e.userName}</Table.Td>
                    <Table.Td>{words(e.entityType)}</Table.Td>
                    <Table.Td><Badge variant="light" color={actionColor[e.action]}>{e.action}</Badge></Table.Td>
                    <Table.Td><Changes json={e.changes} /></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
          {audit.data.totalPages > 1 && (
            <Group justify="center" mt="md"><Pagination value={page} onChange={setPage} total={audit.data.totalPages} /></Group>
          )}
        </>
      )}
    </>
  )
}

function Changes({ json }: { json: string | null }) {
  if (!json) return <Text c="dimmed" size="sm">—</Text>
  const fields = Object.entries(JSON.parse(json) as Record<string, [unknown, unknown]>)
  const show = (v: unknown) => (v === null || v === undefined ? '∅' : String(v))
  return (
    <Popover width={420} position="bottom-start" shadow="md">
      <Popover.Target>
        <UnstyledButton><Text size="sm" c="blue">{fields.map(([k]) => words(k)).join(', ')}</Text></UnstyledButton>
      </Popover.Target>
      <Popover.Dropdown>
        {fields.map(([k, [before, after]]) => (
          <Text key={k} size="sm" mb={4}>
            <b>{words(k)}</b>: <Code>{show(before)}</Code> → <Code>{show(after)}</Code>
          </Text>
        ))}
      </Popover.Dropdown>
    </Popover>
  )
}
