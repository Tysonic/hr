import { Anchor, Card, Group, Pagination, Select, SimpleGrid, Stack, Text, TextInput } from '@mantine/core'
import { useDebouncedValue } from '@mantine/hooks'
import { IconMail, IconPhone, IconSearch } from '@tabler/icons-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { DirectoryEntry, PagedResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { options, useDepartments } from '../org/hooks'

/** Everyone in the company can find a colleague: name, job and work contacts only. */
export function DirectoryPage() {
  const [search, setSearch] = useState('')
  const [debounced] = useDebouncedValue(search, 300)
  const [departmentId, setDepartmentId] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const departments = useDepartments()
  const result = useQuery({
    queryKey: ['directory', debounced, departmentId, page],
    queryFn: () => api.get<PagedResult<DirectoryEntry>>('/directory', { search: debounced, departmentId, page, pageSize: 24 }),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader title="Directory" description="Find a colleague." />
      <Group mb="md" align="flex-end">
        <TextInput placeholder="Name, employee number or email" leftSection={<IconSearch size={16} />} w={320}
          value={search} onChange={(e) => { setSearch(e.currentTarget.value); setPage(1) }} />
        <Select placeholder="All departments" clearable searchable w={240} data={options(departments.data, (d) => d.name)}
          value={departmentId} onChange={(v) => { setDepartmentId(v); setPage(1) }} />
      </Group>
      {result.isPending ? <Loading /> : result.isError ? <LoadError error={result.error} /> : (
        <Stack>
          {result.data.items.length === 0 && <Text c="dimmed">Nobody matches.</Text>}
          <SimpleGrid cols={{ base: 1, sm: 2, lg: 3 }}>
            {result.data.items.map((p) => (
              <Card key={p.id} withBorder padding="sm">
                <Text fw={600}>{p.fullName}</Text>
                <Text size="sm" c="dimmed">{[p.positionTitle, p.departmentName].filter(Boolean).join(' · ') || '—'}</Text>
                {p.branchName && <Text size="xs" c="dimmed">{p.branchName}</Text>}
                {p.managerName && <Text size="xs" c="dimmed">Reports to {p.managerName}</Text>}
                <Stack gap={2} mt="xs">
                  {p.workEmail && <Group gap={6}><IconMail size={14} /><Anchor size="sm" href={`mailto:${p.workEmail}`}>{p.workEmail}</Anchor></Group>}
                  {p.workPhone && <Group gap={6}><IconPhone size={14} /><Anchor size="sm" href={`tel:${p.workPhone}`}>{p.workPhone}</Anchor></Group>}
                </Stack>
              </Card>
            ))}
          </SimpleGrid>
          {result.data.totalPages > 1 && <Pagination value={page} onChange={setPage} total={result.data.totalPages} />}
        </Stack>
      )}
    </>
  )
}
