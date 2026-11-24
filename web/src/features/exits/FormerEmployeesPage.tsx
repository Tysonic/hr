import { Alert, Badge, Button, Group, NumberInput, Stack, Table, Text } from '@mantine/core'
import { modals } from '@mantine/modals'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { Archive, FormerEmployee } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { exitTypeLabel } from './components'

/** Former employees, how long their records are kept, and removing personal data after that. */
export function FormerEmployeesPage() {
  const q = useQuery({ queryKey: ['exits', 'former'], queryFn: () => api.get<Archive>('/former-employees') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  const a = q.data
  const due = a.people.filter((p) => p.due).length
  return (
    <>
      <PageHeader title="Former employees" description="Records are kept, read-only, for the retention period after the last working day. After that, personal data is removed." />
      <Stack>
        <Retention a={a} key={a.retentionYears} />
        {due > 0 && <Alert color="yellow" variant="light">{due} former employee{due === 1 ? "'s" : "s'"} retention period has passed.{a.canRemove ? ' Remove their personal data below.' : ' An HR admin can remove their personal data.'}</Alert>}
        {a.people.length === 0 ? <Text c="dimmed">No former employees.</Text> : (
          <Table.ScrollContainer minWidth={760}>
            <Table>
              <Table.Thead><Table.Tr><Table.Th>Name</Table.Th><Table.Th>Employed</Table.Th><Table.Th>Left as</Table.Th><Table.Th>Rehire</Table.Th><Table.Th>Kept until</Table.Th><Table.Th /></Table.Tr></Table.Thead>
              <Table.Tbody>
                {a.people.map((p) => (
                  <Table.Tr key={p.employeeId}>
                    <Table.Td><Text size="sm">{p.name}</Text><Text size="xs" c="dimmed">{p.employeeNumber}{p.departmentName && ` · ${p.departmentName}`}</Text></Table.Td>
                    <Table.Td>{date(p.hireDate)} – {date(p.lastWorkingDay)}</Table.Td>
                    <Table.Td>{p.exitType ? exitTypeLabel[p.exitType] : '—'}</Table.Td>
                    <Table.Td>{p.eligibleForRehire == null ? '—' : p.eligibleForRehire ? 'Yes' : 'No'}</Table.Td>
                    <Table.Td>{p.anonymisedAt ? <Badge variant="light" color="gray">Removed {date(p.anonymisedAt.slice(0, 10))}</Badge>
                      : <Text size="sm" c={p.due ? 'red' : undefined}>{date(p.keepUntil)}</Text>}</Table.Td>
                    <Table.Td>{a.canRemove && p.due && <Remove p={p} />}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Table.ScrollContainer>
        )}
      </Stack>
    </>
  )
}

function Retention({ a }: { a: Archive }) {
  const [years, setYears] = useState<number | string>(a.retentionYears)
  const save = useApiMutation(() => api.put<Archive>('/former-employees/retention', { years: Number(years) }), { invalidate: ['exits'], success: 'Retention period saved.' })
  return (
    <Group align="flex-end">
      <NumberInput label="Keep former employees' records for" suffix=" years" w={260} min={5} max={25} disabled={!a.canRemove} value={years} onChange={setYears}
        description="After the last working day. Tax and employment law need at least 5." />
      {a.canRemove && <Button variant="default" disabled={Number(years) === a.retentionYears} loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button>}
    </Group>
  )
}

function Remove({ p }: { p: FormerEmployee }) {
  const remove = useApiMutation(() => api.post<Archive>(`/former-employees/${p.employeeId}/anonymise`), { invalidate: ['exits', 'employees'], success: 'Personal data removed.' })
  return (
    <Button size="xs" color="red" variant="light" loading={remove.isPending} onClick={() => modals.openConfirmModal({
      title: `Remove ${p.name}'s personal data?`,
      children: (
        <Stack gap="xs">
          <Text size="sm">Their name, contact details, ID, tax and bank numbers and date of birth are removed from their record and payslips. Their next of kin, documents, change requests and exit interview are deleted, and the audit trail keeps who changed what and when, but not the values.</Text>
          <Text size="sm">Employment and pay history stay, as “Former employee {p.employeeNumber}”. <b>This can't be undone.</b></Text>
        </Stack>
      ),
      labels: { confirm: 'Remove personal data', cancel: 'Cancel' }, confirmProps: { color: 'red' }, onConfirm: () => remove.mutate(undefined),
    })}>Remove personal data</Button>
  )
}
