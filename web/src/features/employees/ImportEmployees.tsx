import { Alert, Button, FileInput, Group, List, ScrollArea, Stack, Table, Text } from '@mantine/core'
import { notifications } from '@mantine/notifications'
import { IconCircleCheck, IconDownload, IconFileSpreadsheet } from '@tabler/icons-react'
import { useState } from 'react'
import { api, download } from '../../api/client'
import type { ImportResult } from '../../api/types'
import { useApiMutation } from '../../lib/mutation'

/** Download the template, upload it filled in, see every problem by row; import only when the whole file is clean. */
export function ImportEmployees({ onDone }: { onDone: () => void }) {
  const [file, setFile] = useState<File | null>(null)
  const [checked, setChecked] = useState<ImportResult | null>(null)
  const send = (commit: boolean) => {
    const body = new FormData()
    body.set('file', file!)
    return api.upload<ImportResult>('/employees/import', body, { commit })
  }
  const check = useApiMutation(() => send(false), { onSuccess: setChecked })
  const commit = useApiMutation(() => send(true), {
    invalidate: ['employees', 'org', 'directory', 'expiries'],
    success: (r) => r.committed ? `${r.created} employees imported (${r.numbers[0]} to ${r.numbers[r.numbers.length - 1]}).` : 'Nothing was imported.',
    onSuccess: (r) => (r.committed ? onDone() : setChecked(r)),
  })
  const template = () => download('/employees/import/template', 'tyhr-employee-import.xlsx')
    .catch((e: Error) => notifications.show({ color: 'red', title: 'Not downloaded', message: e.message }))
  const ready = checked && checked.errors.length === 0

  return (
    <Stack>
      <List size="sm" spacing={4}>
        <List.Item>Download the template: it has dropdowns for your branches, departments and positions, and instructions.</List.Item>
        <List.Item>One employee per row. Managers can be existing employees (number or work email) or people higher up the same file.</List.Item>
        <List.Item>The whole file is checked first. Nothing is imported unless every row is right.</List.Item>
      </List>
      <Group>
        <Button variant="light" leftSection={<IconDownload size={16} />} onClick={() => void template()}>Download template</Button>
      </Group>
      <FileInput label="Filled-in template" placeholder="Choose an .xlsx file" accept=".xlsx" clearable
        leftSection={<IconFileSpreadsheet size={16} />} value={file} onChange={(f) => { setFile(f); setChecked(null) }} />

      {checked && checked.errors.length > 0 && (
        <Alert color="red" title={`${new Set(checked.errors.map((e) => e.row)).size} of ${checked.rows} rows need fixing; nothing was imported`}>
          <ScrollArea.Autosize mah={300}>
            <Table fz="sm" verticalSpacing={4}>
              <Table.Tbody>
                {checked.errors.map((e, i) => (
                  <Table.Tr key={i}><Table.Td w={70} fw={500}>Row {e.row}</Table.Td><Table.Td>{e.message}</Table.Td></Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea.Autosize>
          <Text size="sm" mt="xs">Fix these in the spreadsheet, save it, and choose it again.</Text>
        </Alert>
      )}
      {ready && (
        <Alert color="green" icon={<IconCircleCheck size={18} />} title={`All ${checked.rows} rows are ready to import`}>
          They'll get the next employee numbers. Importing adds them all at once.
        </Alert>
      )}

      <Group justify="flex-end">
        <Button variant="default" onClick={onDone}>Cancel</Button>
        {ready
          ? <Button loading={commit.isPending} onClick={() => commit.mutate(undefined)}>Import {checked.rows} employees</Button>
          : <Button disabled={!file} loading={check.isPending} onClick={() => check.mutate(undefined)}>Check file</Button>}
      </Group>
    </Stack>
  )
}
