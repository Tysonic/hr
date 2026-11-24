import { Alert, Button, Group, Modal, Select, Stack, Text, Title } from '@mantine/core'
import { useDisclosure } from '@mantine/hooks'
import { IconPlus } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { EmployeeLeave, LeaveSettings } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { BalanceCards, RequestLeaveForm, RequestsTable } from './components'

export const useLeaveSettings = () => useQuery({ queryKey: ['leave', 'settings'], queryFn: () => api.get<LeaveSettings>('/leave/settings') })

/** The signed-in person's own leave: balances, requests, and asking for more. */
export function MyLeavePage() {
  const settings = useLeaveSettings()
  const [year, setYear] = useState<number | null>(null)
  const [requesting, { open, close }] = useDisclosure()
  const leave = useQuery({
    queryKey: ['leave', 'balances', 'me', year],
    queryFn: () => api.get<EmployeeLeave>('/leave/balances', { year }),
    retry: (count, e) => !(e instanceof ApiError && e.status === 400) && count < 2,
  })

  if (leave.isError && leave.error instanceof ApiError && leave.error.status === 400)
    return <><PageHeader title="My leave" /><Alert color="yellow" title="No employee record yet">{leave.error.message}</Alert></>
  if (leave.isPending) return <Loading />
  if (leave.isError) return <LoadError error={leave.error} />
  const l = leave.data
  const current = settings.data?.currentLeaveYear ?? l.year

  return (
    <>
      <PageHeader title="My leave" description={`Leave year ${date(l.yearStart)} – ${date(l.yearEnd)}`}
        actions={<Group>
          <Select w={130} allowDeselect={false} value={String(l.year)} onChange={(v) => setYear(Number(v))}
            data={[current + 1, current, current - 1, current - 2].map((y) => ({ value: String(y), label: settings.data?.leaveYearStartMonth === 1 ? String(y) : `${y}/${String(y + 1).slice(2)}` }))} />
          <Button leftSection={<IconPlus size={16} />} onClick={open}>Request leave</Button>
        </Group>} />
      <Stack gap="xl">
        <BalanceCards balances={l.balances} />
        <Stack gap="xs">
          <Title order={4}>Requests</Title>
          <RequestsTable requests={l.requests} empty="No leave in this year yet." />
        </Stack>
        {l.adjustments.length > 0 && (
          <Stack gap={4}>
            <Title order={5}>Adjustments by HR</Title>
            {l.adjustments.map((a) => (
              <Text key={a.id} size="sm">{a.days > 0 ? '+' : ''}{a.days} {a.leaveTypeName.toLowerCase()}: {a.reason} <Text span c="dimmed" size="xs">({a.recordedBy}, {date(a.recordedAt)})</Text></Text>
            ))}
          </Stack>
        )}
      </Stack>
      <Modal opened={requesting} onClose={close} title="Request leave" size="lg">
        {requesting && <RequestLeaveForm gender={l.gender} done={close} />}
      </Modal>
    </>
  )
}
