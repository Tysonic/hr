import { Anchor, Badge, Group, Stack, Table, Text } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { api } from '../../api/client'
import type { TeamMember } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useCan } from '../../lib/permissions'
import { clockTime, DayBadge } from '../attendance/components'
import { dateRange } from '../leave/components'

/** The people below the user in the reporting line: who's in today and who's away soon. */
export function MyTeamPage() {
  const canOpen = useCan('employeesView')
  const team = useQuery({ queryKey: ['me', 'team'], queryFn: () => api.get<TeamMember[]>('/me/team') })
  if (team.isPending) return <Loading />
  if (team.isError) return <LoadError error={team.error} />
  const direct = team.data.filter((t) => t.directReport).length
  const away = team.data.filter((t) => t.today?.status === 'OnLeave').length
  return (
    <>
      <PageHeader title="My team" description={team.data.length === 0 ? undefined
        : `${direct} direct report${direct === 1 ? '' : 's'}, ${team.data.length} people in all · ${away} on leave today`} />
      {team.data.length === 0 ? <Text c="dimmed" ta="center" py="xl">Nobody reports to you on the organisation chart.</Text> : (
        <Table.ScrollContainer minWidth={720}>
          <Table striped>
            <Table.Thead>
              <Table.Tr><Table.Th>Name</Table.Th><Table.Th>Today</Table.Th><Table.Th>Leave in the next 30 days</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {team.data.map((t) => (
                <Table.Tr key={t.id}>
                  <Table.Td>
                    <Group gap="xs">
                      {canOpen ? <Anchor component={Link} to={`/employees/${t.id}`} size="sm" fw={500}>{t.name}</Anchor> : <Text size="sm" fw={500}>{t.name}</Text>}
                      {!t.directReport && <Badge size="xs" variant="outline" color="gray">indirect</Badge>}
                      {t.status === 'Suspended' && <Badge size="xs" color="orange">Suspended</Badge>}
                    </Group>
                    <Text size="xs" c="dimmed">{[t.positionTitle, t.departmentName].filter(Boolean).join(' · ')}</Text>
                  </Table.Td>
                  <Table.Td>
                    {t.today ? (
                      <Group gap="xs">
                        <DayBadge day={t.today} />
                        {t.today.firstIn && <Text size="xs" c="dimmed">in {clockTime(t.today.firstIn)}{t.today.lastOut && `, out ${clockTime(t.today.lastOut)}`}</Text>}
                      </Group>
                    ) : <Text size="xs" c="dimmed">—</Text>}
                  </Table.Td>
                  <Table.Td>
                    {t.upcomingLeave.length === 0 ? <Text size="xs" c="dimmed">None</Text> : (
                      <Stack gap={2}>
                        {t.upcomingLeave.map((l) => (
                          <Text key={l.startDate} size="xs">
                            {dateRange(l.startDate, l.endDate)} · {l.leaveTypeName}
                            {l.pending && <Badge ml={4} size="xs" color="yellow" variant="light">pending</Badge>}
                          </Text>
                        ))}
                      </Stack>
                    )}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      )}
    </>
  )
}
