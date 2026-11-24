import { Alert, Button, Card, Group, Select, SimpleGrid, Stack, Table, Tabs, Text, Title } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconDownload } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { api, download } from '../../api/client'
import type { HeadcountReport, LeaveLiabilityReport, PayrollCostReport, TurnoverReport } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useCan } from '../../lib/permissions'
import { useMoney } from '../payroll/components'
import { BarList, ColumnChart, longMonth, shortMonth } from './charts'

/** Headcount, turnover, leave liability and payroll cost, with one Excel download of everything the user may see. */
export function ReportsPage() {
  const canPayroll = useCan('payrollView')
  const [busy, setBusy] = useState(false)
  const exportAll = () => {
    setBusy(true)
    download('/reports/export', `tyhr-reports-${new Date().toISOString().slice(0, 10)}.xlsx`)
      .catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))
      .finally(() => setBusy(false))
  }
  return (
    <>
      <PageHeader title="Reports" description="The company at a glance, today and over the last 12 months."
        actions={<Button variant="default" leftSection={<IconDownload size={16} />} loading={busy} onClick={exportAll}>Download as Excel</Button>} />
      <Tabs defaultValue="headcount" keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="headcount">Headcount</Tabs.Tab>
          <Tabs.Tab value="turnover">Turnover</Tabs.Tab>
          <Tabs.Tab value="leave">Leave liability</Tabs.Tab>
          {canPayroll && <Tabs.Tab value="payroll">Payroll cost</Tabs.Tab>}
        </Tabs.List>
        <Tabs.Panel value="headcount"><Headcount /></Tabs.Panel>
        <Tabs.Panel value="turnover"><Turnover /></Tabs.Panel>
        <Tabs.Panel value="leave"><Liability /></Tabs.Panel>
        {canPayroll && <Tabs.Panel value="payroll"><PayrollCost /></Tabs.Panel>}
      </Tabs>
    </>
  )
}

const Stat = ({ label, value, hint }: { label: string; value: ReactNode; hint?: ReactNode }) => (
  <Card withBorder padding="md">
    <Text size="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text>
    <Text fz={28} fw={700} lh={1.2} style={{ fontVariantNumeric: 'tabular-nums' }}>{value}</Text>
    {hint && <Text size="xs" c="dimmed">{hint}</Text>}
  </Card>
)

const Panel = ({ title, children }: { title: string; children: ReactNode }) => (
  <Card withBorder><Title order={5} mb="sm">{title}</Title>{children}</Card>
)

function Headcount() {
  const q = useQuery({ queryKey: ['reports', 'headcount'], queryFn: () => api.get<HeadcountReport>('/reports/headcount') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const h = q.data
  const first = h.trend[0]?.value ?? 0
  return (
    <Stack>
      <SimpleGrid cols={{ base: 1, xs: 3 }}>
        <Stat label="Employees" value={h.total} hint={`On ${date(h.asOf)}${h.suspended ? `, ${h.suspended} suspended` : ''}`} />
        <Stat label="12 months ago" value={first} hint={h.total === first ? 'No change' : `${h.total > first ? '+' : ''}${h.total - first} since`} />
        <Stat label="Departments" value={h.byDepartment.filter((d) => d.label !== 'No department').length} />
      </SimpleGrid>
      <Panel title="Headcount at each month end">
        <ColumnChart points={h.trend.map((m) => ({ key: `${m.year}-${m.month}`, label: shortMonth(m.year, m.month), tooltip: longMonth(m.year, m.month), value: m.value }))} />
      </Panel>
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Panel title="By department"><BarList items={h.byDepartment} /></Panel>
        <Panel title="By branch"><BarList items={h.byBranch} /></Panel>
        <Panel title="By gender"><BarList items={h.byGender} /></Panel>
        <Panel title="By age"><BarList items={h.byAge} /></Panel>
        <Panel title="By length of service"><BarList items={h.byService} /></Panel>
        <Panel title="By contract"><BarList items={h.byContract} /></Panel>
      </SimpleGrid>
    </Stack>
  )
}

function Turnover() {
  const q = useQuery({ queryKey: ['reports', 'turnover'], queryFn: () => api.get<TurnoverReport>('/reports/turnover') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const t = q.data
  const pct = (v: number | null) => (v == null ? '—' : `${v}%`)
  return (
    <Stack>
      <SimpleGrid cols={{ base: 2, sm: 4 }}>
        <Stat label="Turnover" value={pct(t.rate)} hint={`Leavers ÷ average headcount (${t.averageHeadcount})`} />
        <Stat label="Voluntary" value={pct(t.voluntaryRate)} hint="Resignations and retirements" />
        <Stat label="Leavers" value={t.leavers} hint={`${date(t.from)} – ${date(t.to)}`} />
        <Stat label="Hires" value={t.hires} hint="In the same 12 months" />
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Panel title="Leavers each month">
          <ColumnChart height={110} points={t.months.map((m) => ({ key: `${m.year}-${m.month}`, label: shortMonth(m.year, m.month), tooltip: longMonth(m.year, m.month), value: m.leavers }))} />
        </Panel>
        <Panel title="Hires each month">
          <ColumnChart height={110} points={t.months.map((m) => ({ key: `${m.year}-${m.month}`, label: shortMonth(m.year, m.month), tooltip: longMonth(m.year, m.month), value: m.hires }))} />
        </Panel>
        <Panel title="How people left"><BarList items={t.byType} /></Panel>
        <Panel title="Leavers by department"><BarList items={t.byDepartment} /></Panel>
        {t.byReason && <Panel title="Main reason (exit interviews)"><BarList items={t.byReason} /></Panel>}
        <Panel title="Leavers by length of service"><BarList items={t.byService} /></Panel>
      </SimpleGrid>
      <Panel title="Month by month">
        <Table.ScrollContainer minWidth={520}>
          <Table striped>
            <Table.Thead><Table.Tr><Table.Th>Month</Table.Th><Table.Th ta="right">Hires</Table.Th><Table.Th ta="right">Leavers</Table.Th><Table.Th ta="right">Voluntary</Table.Th><Table.Th ta="right">Headcount at month end</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {t.months.map((m) => (
                <Table.Tr key={`${m.year}-${m.month}`}>
                  <Table.Td>{longMonth(m.year, m.month)}</Table.Td><Table.Td ta="right">{m.hires}</Table.Td><Table.Td ta="right">{m.leavers}</Table.Td>
                  <Table.Td ta="right">{m.voluntary}</Table.Td><Table.Td ta="right">{m.headcount}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Panel>
    </Stack>
  )
}

function Liability() {
  const money = useMoney()
  const q = useQuery({ queryKey: ['reports', 'leave-liability'], queryFn: () => api.get<LeaveLiabilityReport>('/reports/leave-liability') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const l = q.data
  const showPay = l.amount != null
  const owed = l.people.filter((p) => p.days > 0)
  return (
    <Stack>
      <SimpleGrid cols={{ base: 1, xs: showPay ? 3 : 2 }}>
        <Stat label="Unused days" value={l.days.toLocaleString('en-GB')} hint={`${l.leaveType ?? 'Annual leave'} on ${date(l.asOf)}`} />
        {showPay && <Stat label="Value" value={money(l.amount)} hint="Before tax, at each person's daily rate" />}
        <Stat label="People with days owed" value={owed.length} />
      </SimpleGrid>
      {l.notes.map((n) => <Text key={n} size="xs" c="dimmed">{n}</Text>)}
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Panel title="Days by department"><BarList items={l.daysByDepartment} /></Panel>
        {l.amountByDepartment && <Panel title="Value by department"><BarList items={l.amountByDepartment} format={(v) => money(v)} /></Panel>}
      </SimpleGrid>
      <Panel title="By person">
        <Table.ScrollContainer minWidth={600}>
          <Table striped>
            <Table.Thead><Table.Tr><Table.Th>Name</Table.Th><Table.Th>Department</Table.Th><Table.Th ta="right">Days</Table.Th>
              {showPay && <><Table.Th ta="right">A working day</Table.Th><Table.Th ta="right">Value</Table.Th></>}</Table.Tr></Table.Thead>
            <Table.Tbody>
              {l.people.map((p) => (
                <Table.Tr key={p.employeeId}>
                  <Table.Td><Text size="sm">{p.name}</Text><Text size="xs" c="dimmed">{p.employeeNumber}</Text></Table.Td>
                  <Table.Td>{p.department ?? '—'}</Table.Td>
                  <Table.Td ta="right" c={p.days < 0 ? 'red' : undefined}>{p.days}</Table.Td>
                  {showPay && <><Table.Td ta="right">{p.dailyRate == null ? 'No salary' : money(p.dailyRate)}</Table.Td><Table.Td ta="right">{money(p.amount)}</Table.Td></>}
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Panel>
    </Stack>
  )
}

function PayrollCost() {
  const money = useMoney()
  const [month, setMonth] = useState<string | null>(null)
  const [y, m] = month ? month.split('-').map(Number) : [null, null]
  const q = useQuery({
    queryKey: ['reports', 'payroll-cost', month],
    queryFn: () => api.get<PayrollCostReport>('/reports/payroll-cost', y ? { year: y, month: m } : undefined),
  })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const c = q.data
  if (c.year == null || c.month == null) return <Alert variant="light">Payroll costs show once a payroll has been approved.</Alert>
  const options = c.trend.map((t) => ({ value: `${t.year}-${t.month}`, label: longMonth(t.year, t.month) })).reverse()
  return (
    <Stack>
      <Group justify="space-between" align="flex-end">
        <Select label="Month" w={220} data={options} value={`${c.year}-${c.month}`} onChange={setMonth} allowDeselect={false} />
        <Text size="xs" c="dimmed">From approved and locked payrolls, including off-cycle and reversals.</Text>
      </Group>
      <SimpleGrid cols={{ base: 1, xs: 3 }}>
        <Stat label="Total cost" value={money(c.total)} hint={longMonth(c.year, c.month)} />
        <Stat label="Gross pay" value={money(c.gross)} />
        <Stat label="Employer contributions" value={money(c.employerContributions)} hint="e.g. NSSF employer share" />
      </SimpleGrid>
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Panel title="Gross pay each month">
          <ColumnChart format={(v) => money(v)} points={c.trend.map((t) => ({ key: `${t.year}-${t.month}`, label: shortMonth(t.year, t.month), tooltip: longMonth(t.year, t.month), value: t.gross }))} />
        </Panel>
        <Panel title="Total cost by department"><BarList items={c.byDepartment.map((d) => ({ label: d.department, value: d.total }))} format={(v) => money(v)} /></Panel>
      </SimpleGrid>
      <Panel title="By department">
        <Table.ScrollContainer minWidth={620}>
          <Table striped>
            <Table.Thead><Table.Tr><Table.Th>Department</Table.Th><Table.Th ta="right">People paid</Table.Th><Table.Th ta="right">Gross pay</Table.Th><Table.Th ta="right">Employer contributions</Table.Th><Table.Th ta="right">Total cost</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {c.byDepartment.map((d) => (
                <Table.Tr key={d.department}>
                  <Table.Td>{d.department}</Table.Td><Table.Td ta="right">{d.people}</Table.Td><Table.Td ta="right">{money(d.gross)}</Table.Td>
                  <Table.Td ta="right">{money(d.employerContributions)}</Table.Td><Table.Td ta="right">{money(d.total)}</Table.Td>
                </Table.Tr>
              ))}
              <Table.Tr fw={700}><Table.Td>Total</Table.Td><Table.Td /><Table.Td ta="right">{money(c.gross)}</Table.Td><Table.Td ta="right">{money(c.employerContributions)}</Table.Td><Table.Td ta="right">{money(c.total)}</Table.Td></Table.Tr>
            </Table.Tbody>
          </Table>
        </Table.ScrollContainer>
      </Panel>
    </Stack>
  )
}
