import { Group, Pagination, Select, Stack, Tabs } from '@mantine/core'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { LeaveRequest, LeaveStatus, PagedResult } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useCan } from '../../lib/permissions'
import { RequestsTable } from './components'

/** Pending requests the user may decide (their team's, or everyone's for HR), and for HR and payroll all requests. */
export function ApprovalsPage() {
  const canSeeAll = useCan('leaveViewAll')
  const canApprove = useCan('leaveApprove')
  return (
    <>
      <PageHeader title={canApprove ? 'Leave approvals' : 'Leave requests'}
        description={canApprove ? 'Requests waiting for your decision, earliest first. Nobody decides their own leave.' : undefined} />
      <Tabs defaultValue={canApprove ? 'pending' : 'all'} keepMounted={false}>
        <Tabs.List mb="md">
          {canApprove && <Tabs.Tab value="pending">Waiting for me</Tabs.Tab>}
          {(canSeeAll || canApprove) && <Tabs.Tab value="all">{canSeeAll ? 'All requests' : 'My team'}</Tabs.Tab>}
        </Tabs.List>
        {canApprove && <Tabs.Panel value="pending"><RequestList scope="approvals" /></Tabs.Panel>}
        <Tabs.Panel value="all"><RequestList scope="all" filter /></Tabs.Panel>
      </Tabs>
    </>
  )
}

export function RequestList({ scope, filter = false }: { scope: string; filter?: boolean }) {
  const [page, setPage] = useState(1)
  const [status, setStatus] = useState<LeaveStatus | null>(null)
  const list = useQuery({
    queryKey: ['leave', 'requests', scope, status, page],
    queryFn: () => api.get<PagedResult<LeaveRequest>>('/leave/requests', { scope, status, page, pageSize: 50 }),
    placeholderData: keepPreviousData,
  })
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  return (
    <Stack>
      {filter && (
        <Group><Select placeholder="Any status" clearable w={170} data={['Pending', 'Approved', 'Rejected', 'Cancelled']} value={status}
          onChange={(v) => { setStatus(v as LeaveStatus | null); setPage(1) }} /></Group>
      )}
      <RequestsTable requests={list.data.items} showEmployee empty={scope === 'approvals' ? 'Nothing waiting for you.' : 'No requests.'} />
      {list.data.totalPages > 1 && <Pagination value={page} onChange={setPage} total={list.data.totalPages} />}
    </Stack>
  )
}
