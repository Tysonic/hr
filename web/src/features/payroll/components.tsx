import { Alert, Badge, Group, Stack, Table, Text, Title } from '@mantine/core'
import type { PayLine, Payslip, PayrollRunKind, PayrollStatus } from '../../api/types'
import { useMe } from '../../lib/permissions'
import { money } from '../../lib/format'

/** Formats amounts in the company's currency. */
export function useMoney() {
  const { data } = useMe()
  const country = data?.company?.country ?? { currencyCode: '', currencyMinorUnits: 0 }
  return (amount: number | null | undefined) => money(amount, country)
}

export const statusColor: Record<PayrollStatus, string> = { Draft: 'gray', Calculated: 'blue', Submitted: 'orange', Approved: 'teal', Locked: 'green' }
export const statusLabel: Record<PayrollStatus, string> = { Draft: 'Draft', Calculated: 'Calculated', Submitted: 'Waiting for approval', Approved: 'Approved', Locked: 'Locked' }
export const kindLabel: Record<PayrollRunKind, string> = { Regular: 'Regular', OffCycle: 'Off-cycle', Reversal: 'Reversal' }

export const StatusBadge = ({ status }: { status: PayrollStatus }) => <Badge variant="light" color={statusColor[status]}>{statusLabel[status]}</Badge>

export const monthName = (year: number, month: number) => new Date(year, month - 1, 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })

/** A payslip laid out as the employee would read it. */
export function PayslipView({ payslip: p }: { payslip: Payslip }) {
  const m = useMoney()
  const section = (title: string, lines: PayLine[], total?: [string, number]) => lines.length > 0 && (
    <Stack gap={4}>
      <Title order={6} c="dimmed" tt="uppercase">{title}</Title>
      <Table verticalSpacing={4} fz="sm">
        <Table.Tbody>
          {lines.map((l, i) => (
            <Table.Tr key={`${l.code}-${i}`}>
              <Table.Td>{l.name}{l.note && <Text span size="xs" c="dimmed"> · {l.note}</Text>}</Table.Td>
              <Table.Td ta="right" w={160} c={l.amount < 0 ? 'red' : undefined}>{m(l.amount)}</Table.Td>
            </Table.Tr>
          ))}
          {total && <Table.Tr fw={700}><Table.Td>{total[0]}</Table.Td><Table.Td ta="right">{m(total[1])}</Table.Td></Table.Tr>}
        </Table.Tbody>
      </Table>
    </Stack>
  )
  return (
    <Stack>
      <Group justify="space-between" align="flex-start">
        <div>
          <Text fw={700} size="lg">{p.employeeName}</Text>
          <Text size="sm" c="dimmed">{[p.employeeNumber, p.positionTitle, p.departmentName].filter(Boolean).join(' · ')}</Text>
        </div>
        <div style={{ textAlign: 'right' }}>
          <Text fw={600}>{p.runName}</Text>
          <Text size="sm" c="dimmed">Paid {new Date(p.payDate).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })}</Text>
        </div>
      </Group>
      <Group gap="xl">
        <Text size="sm"><Text span c="dimmed">Tax number:</Text> {p.taxNumber ?? '—'}</Text>
        <Text size="sm"><Text span c="dimmed">Social security:</Text> {p.socialSecurityNumber ?? '—'}</Text>
        <Text size="sm"><Text span c="dimmed">Paid to:</Text> {p.paidTo}</Text>
      </Group>
      {p.warnings.length > 0 && <Alert color="orange">{p.warnings.map((w) => <Text key={w} size="sm">{w}</Text>)}</Alert>}
      {section('Earnings', p.lines.filter((l) => l.kind === 'Earning'), ['Gross pay', p.gross])}
      {section('Deductions', p.lines.filter((l) => l.kind === 'Statutory' || l.kind === 'Deduction'), ['Total deductions', p.totalDeductions])}
      <Group justify="space-between" p="sm" style={{ borderRadius: 8, background: 'var(--mantine-color-green-light)' }}>
        <Text fw={700}>Net pay</Text><Text fw={700} size="lg">{m(p.net)}</Text>
      </Group>
      {section('Paid by the employer (not deducted)', p.lines.filter((l) => l.kind === 'Employer'), ['Employer contributions', p.employerContributions])}
    </Stack>
  )
}
