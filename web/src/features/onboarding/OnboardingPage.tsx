import { Anchor, Badge, Button, Drawer, Group, Progress, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { OnboardingProgress } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { EmployeeOnboarding } from './EmployeeOnboardingTab'

/** New starters and how far along their onboarding is (HR). */
export function OnboardingPage() {
  const list = useQuery({ queryKey: ['onboarding', 'progress'], queryFn: () => api.get<OnboardingProgress[]>('/onboarding') })
  const [open, setOpen] = useState<OnboardingProgress | null>(null)
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  return (
    <>
      <PageHeader title="Onboarding" description="New starters' checklists. Start one from the employee's record, or when making a candidate an employee."
        actions={<Button variant="default" component={Link} to="/settings/onboarding">Checklists</Button>} />
      {list.data.length === 0 ? <Text c="dimmed">Nobody is being onboarded.</Text> : (
        <Table.ScrollContainer minWidth={640}>
          <Table highlightOnHover>
            <Table.Thead><Table.Tr><Table.Th>New starter</Table.Th><Table.Th>Starts</Table.Th><Table.Th>Manager</Table.Th><Table.Th w={220}>Progress</Table.Th><Table.Th>Next due</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {list.data.map((p) => (
                <Table.Tr key={p.employeeId} style={{ cursor: 'pointer' }} onClick={() => setOpen(p)}>
                  <Table.Td><Text size="sm" fw={500}>{p.employeeName}</Text><Text size="xs" c="dimmed">{p.employeeNumber}</Text></Table.Td>
                  <Table.Td>{date(p.startDate)}</Table.Td>
                  <Table.Td>{p.managerName ?? '—'}</Table.Td>
                  <Table.Td>
                    <Group gap="xs" wrap="nowrap">
                      <Progress value={(p.done / p.total) * 100} color={p.done === p.total ? 'green' : 'blue'} style={{ flex: 1 }} />
                      <Text size="xs">{p.done}/{p.total}</Text>
                      {p.overdue > 0 && <Badge size="xs" color="red">{p.overdue} late</Badge>}
                    </Group>
                  </Table.Td>
                  <Table.Td>{p.nextDue ? date(p.nextDue) : 'All done'}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
      <Drawer opened={!!open} onClose={() => setOpen(null)} position="right" size="xl"
        title={open && <Anchor component={Link} to={`/employees/${open.employeeId}`} fw={600}>{open.employeeName}</Anchor>}>
        {open && <EmployeeOnboarding employeeId={open.employeeId} />}
      </Drawer>
    </>
  )
}
