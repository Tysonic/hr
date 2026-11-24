import { Alert, Button, Card, FileInput, Group, List, Select, SimpleGrid, Stack, Table, Text } from '@mantine/core'
import { IconCircleCheck, IconFileText } from '@tabler/icons-react'
import { useState } from 'react'
import { api } from '../../api/client'
import type { PunchImportResult } from '../../api/types'
import { PageHeader } from '../../components/common'
import { date } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'

type Columns = { idColumn: string | null; dateTimeColumn: string | null; dateColumn: string | null; timeColumn: string | null; directionColumn: string | null }
const auto: Columns = { idColumn: null, dateTimeColumn: null, dateColumn: null, timeColumn: null, directionColumn: null }

/** The file's heading row, read in the browser so columns can be chosen even when TyHR can't tell them apart. */
async function headings(file: File) {
  const first = (await file.slice(0, 4096).text()).replace(/^﻿/, '').split(/\r?\n/)[0] ?? ''
  const delimiter = ['\t', ';', ','].reduce((a, b) => (first.split(b).length > first.split(a).length ? b : a))
  return first.split(delimiter).map((h) => h.trim().replace(/^"|"$/g, '')).filter(Boolean)
}

/** Upload a biometric device's export; see what matches; import the new punches. Importing the same file twice adds nothing. */
export function ImportPunchesPage() {
  const [file, setFile] = useState<File | null>(null)
  const [found, setFound] = useState<string[]>([])
  const [columns, setColumns] = useState<Columns>(auto)
  const [checked, setChecked] = useState<PunchImportResult | null>(null)
  const send = (commit: boolean) => {
    const body = new FormData()
    body.set('file', file!)
    return api.upload<PunchImportResult>('/attendance/import', body, { commit, ...columns })
  }
  const check = useApiMutation(() => send(false), { onSuccess: setChecked })
  const commit = useApiMutation(() => send(true), {
    invalidate: ['attendance'],
    success: (r) => (r.imported ? `${r.imported} punches imported.` : 'Nothing new to import.'),
    onSuccess: (r) => setChecked(r),
  })
  const choose = (f: File | null) => {
    setFile(f)
    setChecked(null)
    setColumns(auto)
    setFound([])
    if (f) void headings(f).then(setFound)
  }
  const pick = (key: keyof Columns) => ({
    value: columns[key], placeholder: 'Detect', clearable: true, data: found,
    onChange: (v: string | null) => { setColumns({ ...columns, [key]: v }); setChecked(null) },
  })
  const newPunches = checked ? checked.matched - checked.alreadyImported : 0

  return (
    <>
      <PageHeader title="Import punches" description="Bring in clockings from fingerprint or face-recognition devices." />
      <Stack maw={860}>
        <List size="sm" spacing={4}>
          <List.Item>On the device or its software (ZKTeco, Hikvision and the like), export the attendance log as CSV or text.</List.Item>
          <List.Item>Punches are matched to people by the user number on the device (set on each employee's Attendance tab) or by employee number.</List.Item>
          <List.Item>Times are read as your company's local time. Dates should be day first (23/11/2026) or year first (2026-11-23).</List.Item>
          <List.Item>Importing an overlapping period again is safe: punches already in TyHR are skipped.</List.Item>
        </List>
        <FileInput label="Device export" placeholder="Choose a .csv or .txt file" accept=".csv,.txt,.tsv,.dat" clearable
          leftSection={<IconFileText size={16} />} value={file} onChange={choose} />

        {found.length > 0 && (
          <Card withBorder>
            <Text size="sm" fw={500} mb="xs">Columns (leave on Detect unless the check can't tell them apart)</Text>
            <SimpleGrid cols={{ base: 1, sm: 3 }}>
              <Select label="User number" {...pick('idColumn')} />
              <Select label="Date and time" {...pick('dateTimeColumn')} />
              <Select label="In / out" {...pick('directionColumn')} />
              <Select label="Date (if separate)" {...pick('dateColumn')} />
              <Select label="Time (if separate)" {...pick('timeColumn')} />
            </SimpleGrid>
          </Card>
        )}

        {checked && (
          <Alert color={checked.imported || newPunches ? 'green' : 'blue'} icon={<IconCircleCheck size={18} />}
            title={checked.imported ? `${checked.imported} punches imported` : `${newPunches} new punches ready to import`}>
            <Text size="sm">
              {checked.lines} lines{checked.from && `, ${date(checked.from)} to ${date(checked.to)}`}. {checked.matched} matched to employees
              {checked.alreadyImported > 0 && `, ${checked.alreadyImported} already in TyHR`}.
            </Text>
            <Text size="xs" c="dimmed">Read as: user number “{checked.columns.id}”, {checked.columns.dateTime ? `time “${checked.columns.dateTime}”` : `date “${checked.columns.date}” and time “${checked.columns.time}”`}
              {checked.columns.direction ? `, in/out “${checked.columns.direction}”` : ', no in/out column'}.</Text>
          </Alert>
        )}
        {checked && checked.unknownUsers.length > 0 && (
          <Alert color="yellow" title={`${checked.unknownUsers.length} device users aren't matched to anyone`}>
            <Text size="sm" mb="xs">Their punches are skipped. Set these numbers on the employees' Attendance tab, then import the file again.</Text>
            <Table fz="sm" verticalSpacing={2} maw={300}>
              <Table.Tbody>
                {checked.unknownUsers.slice(0, 20).map((u) => <Table.Tr key={u.deviceUserId}><Table.Td>User {u.deviceUserId}</Table.Td><Table.Td>{u.punches} punches</Table.Td></Table.Tr>)}
              </Table.Tbody>
            </Table>
          </Alert>
        )}
        {checked && checked.errors.length > 0 && (
          <Alert color="red" title={`${checked.errors.length} lines couldn't be read and are skipped`}>
            {checked.errors.slice(0, 10).map((e) => <Text key={e} size="sm">{e}</Text>)}
          </Alert>
        )}

        <Group justify="flex-end">
          <Button variant="default" disabled={!file} loading={check.isPending} onClick={() => check.mutate(undefined)}>Check file</Button>
          <Button disabled={!checked || newPunches === 0 || checked.imported > 0} loading={commit.isPending} onClick={() => commit.mutate(undefined)}>
            Import {newPunches > 0 && !checked?.imported ? `${newPunches} punches` : ''}
          </Button>
        </Group>
      </Stack>
    </>
  )
}
