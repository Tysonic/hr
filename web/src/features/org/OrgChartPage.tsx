import { ActionIcon, Badge, Box, Card, Group, Stack, Text, TextInput } from '@mantine/core'
import { IconChevronDown, IconChevronRight, IconSearch } from '@tabler/icons-react'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import type { OrgChartNode } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { buildOrgTree, teamSize, type TreeNode } from '../../lib/orgTree'
import { useCan } from '../../lib/permissions'
import { useOrgChart } from './hooks'

/** Who reports to whom, from each employee's line manager. Searching opens the branches leading to the matches. */
export function OrgChartPage() {
  const chart = useOrgChart()
  const [search, setSearch] = useState('')
  const tree = useMemo(() => buildOrgTree(chart.data ?? []), [chart.data])
  const term = search.trim().toLowerCase()

  return (
    <>
      <PageHeader title="Org chart" description="Reporting lines across the company. Set each employee's line manager on their record." />
      <TextInput mb="md" maw={360} placeholder="Find a person, position or department" leftSection={<IconSearch size={16} />}
        value={search} onChange={(e) => setSearch(e.currentTarget.value)} />
      {chart.isPending ? <Loading /> : chart.isError ? <LoadError error={chart.error} /> : tree.length === 0 ? (
        <Text c="dimmed">No employees yet.</Text>
      ) : (
        <Stack gap={4}>
          {tree.map((t) => <Branch key={t.node.employeeId} tree={t} term={term} depth={0} />)}
        </Stack>
      )}
    </>
  )
}

const matches = (n: OrgChartNode, term: string) =>
  [n.name, n.positionTitle, n.departmentName, n.employeeNumber].some((v) => v?.toLowerCase().includes(term))

const containsMatch = (t: TreeNode<OrgChartNode>, term: string): boolean =>
  matches(t.node, term) || t.reports.some((r) => containsMatch(r, term))

function Branch({ tree, term, depth }: { tree: TreeNode<OrgChartNode>; term: string; depth: number }) {
  const [open, setOpen] = useState(depth < 2)
  const canOpenRecords = useCan('employeesView')
  if (term && !containsMatch(tree, term)) return null
  const expanded = term ? tree.reports.some((r) => containsMatch(r, term)) : open
  const { node } = tree
  const size = teamSize(tree)
  const highlighted = term !== '' && matches(node, term)

  return (
    <Box pl={depth === 0 ? 0 : 'lg'} style={depth === 0 ? undefined : { borderLeft: '1px solid var(--mantine-color-default-border)' }}>
      <Card withBorder padding="xs" maw={520} bg={highlighted ? 'var(--mantine-primary-color-light)' : undefined}>
        <Group gap="xs" wrap="nowrap">
          {tree.reports.length > 0 ? (
            <ActionIcon variant="subtle" size="sm" onClick={() => setOpen(!expanded)} aria-label={expanded ? 'Collapse' : 'Expand'}>
              {expanded ? <IconChevronDown size={14} /> : <IconChevronRight size={14} />}
            </ActionIcon>
          ) : <Box w={22} />}
          <Stack gap={0} style={{ flex: 1, minWidth: 0 }}>
            {canOpenRecords
              ? <Text fw={600} size="sm" truncate component={Link} to={`/employees/${node.employeeId}`}>{node.name}</Text>
              : <Text fw={600} size="sm" truncate>{node.name}</Text>}
            <Text size="xs" c="dimmed" truncate>{[node.positionTitle, node.departmentName].filter(Boolean).join(' · ') || 'No position set'}</Text>
          </Stack>
          {size > 0 && <Badge variant="light" size="sm">{size} {size === 1 ? 'report' : 'in team'}</Badge>}
        </Group>
      </Card>
      {expanded && tree.reports.length > 0 && (
        <Stack gap={4} mt={4}>
          {tree.reports.map((r) => <Branch key={r.node.employeeId} tree={r} term={term} depth={depth + 1} />)}
        </Stack>
      )}
    </Box>
  )
}
