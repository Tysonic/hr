import { Alert, Button, Drawer, Group, Stack, Table, Text } from '@mantine/core'
import { IconDownload } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { MyPayslip, Payslip } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { monthName, PayslipView, useMoney } from './components'
import { save } from './PayrollRunPage'

export function MyPayslipsPage() {
  const m = useMoney()
  const [open, setOpen] = useState<string | null>(null)
  const list = useQuery({
    queryKey: ['payroll', 'mine'], queryFn: () => api.get<MyPayslip[]>('/payroll/my-payslips'),
    retry: (count, e) => !(e instanceof ApiError && e.status === 400) && count < 2,
  })
  const slip = useQuery({ queryKey: ['payroll', 'mine', open], queryFn: () => api.get<Payslip>(`/payroll/my-payslips/${open}`), enabled: !!open })
  if (list.isError && list.error instanceof ApiError && list.error.status === 400)
    return <><PageHeader title="My payslips" /><Alert color="yellow" title="No employee record yet">{list.error.message}</Alert></>
  if (list.isPending) return <Loading />
  if (list.isError) return <LoadError error={list.error} />
  const pdf = (p: { id: string; year: number; month: number }) => void save(`/payroll/my-payslips/${p.id}/pdf`, `payslip-${p.year}-${String(p.month).padStart(2, '0')}.pdf`)
  return (
    <>
      <PageHeader title="My payslips" description="Payslips appear here once the month's payroll is finalised." />
      <Table striped highlightOnHover maw={800}>
        <Table.Thead><Table.Tr><Table.Th>Month</Table.Th><Table.Th>Paid</Table.Th><Table.Th ta="right">Gross</Table.Th><Table.Th ta="right">Net</Table.Th><Table.Th /></Table.Tr></Table.Thead>
        <Table.Tbody>
          {list.data.length === 0 && <Table.Tr><Table.Td colSpan={5}><Text c="dimmed" ta="center" py="lg">No payslips yet.</Text></Table.Td></Table.Tr>}
          {list.data.map((p) => (
            <Table.Tr key={p.id} style={{ cursor: 'pointer' }} onClick={() => setOpen(p.id)}>
              <Table.Td><Text size="sm" fw={500}>{monthName(p.year, p.month)}</Text>{!p.runName.endsWith(' payroll') && <Text size="xs" c="dimmed">{p.runName}</Text>}</Table.Td>
              <Table.Td>{date(p.payDate)}</Table.Td>
              <Table.Td ta="right">{m(p.gross)}</Table.Td>
              <Table.Td ta="right" fw={600}>{m(p.net)}</Table.Td>
              <Table.Td w={60}><Button size="xs" variant="subtle" leftSection={<IconDownload size={14} />} onClick={(e) => { e.stopPropagation(); pdf(p) }}>PDF</Button></Table.Td>
            </Table.Tr>
          ))}
        </Table.Tbody>
      </Table>
      <Drawer opened={!!open} onClose={() => setOpen(null)} position="right" size="lg" title="Payslip">
        {slip.isPending ? <Loading /> : slip.isError ? <LoadError error={slip.error} /> : (
          <Stack>
            <Group justify="flex-end"><Button size="xs" variant="default" leftSection={<IconDownload size={14} />} onClick={() => pdf(slip.data)}>PDF</Button></Group>
            <PayslipView payslip={slip.data} />
          </Stack>
        )}
      </Drawer>
    </>
  )
}
