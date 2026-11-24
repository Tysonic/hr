import {
  ActionIcon, Alert, Badge, Button, Checkbox, Group, Menu, Modal, NumberInput, Select, SimpleGrid, Stack, Switch, Table, Tabs, Text,
  Textarea, TextInput,
} from '@mantine/core'
import { DateInput } from '@mantine/dates'
import { useForm } from '@mantine/form'
import { IconDots, IconPlus, IconWand } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { DayOfWeek, Holiday, LeaveSettings, LeaveType, SaveLeaveType } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { date, months } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'
import { useLeaveTypes } from './components'
import { useLeaveSettings } from './MyLeavePage'

const accrualLabel = { Monthly: 'Earned monthly', Yearly: 'Granted yearly', NoBalance: 'No balance' }

export function LeaveSettingsPage() {
  return (
    <>
      <PageHeader title="Leave settings" description="Leave types, public holidays and the working week used to count leave days." />
      <Tabs defaultValue="types" keepMounted={false}>
        <Tabs.List mb="md">
          <Tabs.Tab value="types">Leave types</Tabs.Tab>
          <Tabs.Tab value="holidays">Public holidays</Tabs.Tab>
          <Tabs.Tab value="week">Working week</Tabs.Tab>
        </Tabs.List>
        <Tabs.Panel value="types"><TypesTab /></Tabs.Panel>
        <Tabs.Panel value="holidays"><HolidaysTab /></Tabs.Panel>
        <Tabs.Panel value="week"><WeekTab /></Tabs.Panel>
      </Tabs>
    </>
  )
}

function TypesTab() {
  const canEdit = useCan('leaveSetup')
  const types = useLeaveTypes()
  const [editing, setEditing] = useState<LeaveType | 'new' | null>(null)
  const defaults = useApiMutation(() => api.post<LeaveType[]>('/leave/types/country-defaults'), { invalidate: ['leave'], success: 'Country leave types added.' })
  const toggle = useApiMutation((t: LeaveType) => api.post<LeaveType>(`/leave/types/${t.id}/${t.isActive ? 'deactivate' : 'activate'}`), {
    invalidate: ['leave'], success: (t) => `${t.name} ${t.isActive ? 'offered again' : 'no longer offered'}.`,
  })
  if (types.isPending) return <Loading />
  if (types.isError) return <LoadError error={types.error} />
  return (
    <Stack maw={1100}>
      <Alert color="blue">The starting types follow your country's employment act. Check them against current law and your own policy; carry-forward limits are a company choice.</Alert>
      {canEdit && (
        <Group justify="flex-end">
          <Button variant="default" leftSection={<IconWand size={16} />} loading={defaults.isPending} onClick={() => defaults.mutate(undefined)}>Add missing country types</Button>
          <Button leftSection={<IconPlus size={16} />} onClick={() => setEditing('new')}>New leave type</Button>
        </Group>
      )}
      <Table.ScrollContainer minWidth={800}>
        <Table striped>
          <Table.Thead>
            <Table.Tr><Table.Th>Leave type</Table.Th><Table.Th>How it's earned</Table.Th><Table.Th>Rules</Table.Th><Table.Th>Status</Table.Th><Table.Th /></Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {types.data.map((t) => (
              <Table.Tr key={t.id} opacity={t.isActive ? 1 : 0.6}>
                <Table.Td><Text fw={500} size="sm">{t.name} <Text span c="dimmed" size="xs">{t.code}</Text></Text>{t.description && <Text size="xs" c="dimmed">{t.description}</Text>}</Table.Td>
                <Table.Td><Text size="sm">{accrualLabel[t.accrual]}</Text>{t.daysPerYear !== null && <Text size="xs" c="dimmed">{t.daysPerYear} days a year{t.carryForwardMax > 0 ? `, up to ${t.carryForwardMax} carried` : ', none carried'}</Text>}</Table.Td>
                <Table.Td>
                  <Group gap={4}>
                    <Badge size="sm" variant="light" color={t.isPaid ? 'green' : 'gray'}>{t.isPaid ? 'Paid' : 'Unpaid'}</Badge>
                    {t.countsCalendarDays && <Badge size="sm" variant="light">Calendar days</Badge>}
                    {t.eligibleGender && <Badge size="sm" variant="light" color="grape">{t.eligibleGender} only</Badge>}
                    {t.maxDaysPerRequest !== null && <Badge size="sm" variant="light" color="gray">Max {t.maxDaysPerRequest} days</Badge>}
                    {t.minServiceMonths > 0 && <Badge size="sm" variant="light" color="gray">After {t.minServiceMonths} months</Badge>}
                    {t.documentRequiredAfterDays !== null && <Badge size="sm" variant="light" color="orange">Document after {t.documentRequiredAfterDays} days</Badge>}
                  </Group>
                </Table.Td>
                <Table.Td><Badge variant="light" color={t.isActive ? 'green' : 'gray'}>{t.isActive ? 'Offered' : 'Not offered'}</Badge></Table.Td>
                <Table.Td w={40}>
                  {canEdit && (
                    <Menu position="bottom-end">
                      <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${t.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                      <Menu.Dropdown>
                        <Menu.Item onClick={() => setEditing(t)}>Edit</Menu.Item>
                        <Menu.Item onClick={() => toggle.mutate(t)}>{t.isActive ? 'Stop offering' : 'Offer again'}</Menu.Item>
                      </Menu.Dropdown>
                    </Menu>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Table.ScrollContainer>
      <Modal opened={editing !== null} onClose={() => setEditing(null)} title={editing === 'new' ? 'New leave type' : 'Edit leave type'} size="lg">
        {editing !== null && <TypeForm type={editing === 'new' ? null : editing} done={() => setEditing(null)} />}
      </Modal>
    </Stack>
  )
}

function TypeForm({ type, done }: { type: LeaveType | null; done: () => void }) {
  const form = useForm<SaveLeaveType>({
    initialValues: type ?? {
      code: '', name: '', accrual: 'Monthly', daysPerYear: 21, maxDaysPerRequest: null, carryForwardMax: 0, countsCalendarDays: false, isPaid: true,
      eligibleGender: null, minServiceMonths: 0, documentRequiredAfterDays: null, description: null,
    },
    validate: {
      code: (v) => (v.trim() ? null : 'Required'),
      name: (v) => (v.trim() ? null : 'Required'),
      daysPerYear: (v, all) => (all.accrual !== 'NoBalance' && !v ? 'How many days a year?' : null),
    },
  })
  const save = useApiMutation((v: SaveLeaveType) => type ? api.put<LeaveType>(`/leave/types/${type.id}`, v) : api.post<LeaveType>('/leave/types', v), {
    invalidate: ['leave'], success: (t) => `${t.name} saved.`, onSuccess: done,
  })
  const v = form.getValues()
  const num = (name: keyof SaveLeaveType) => ({ ...form.getInputProps(name), value: (form.getValues()[name] as number | null) ?? '', onChange: (x: string | number) => form.setFieldValue(name, (x === '' ? null : Number(x)) as never) })
  return (
    <form onSubmit={form.onSubmit((x) => save.mutate({ ...x, description: x.description?.trim() || null }))}>
      <Stack>
        <SimpleGrid cols={{ base: 1, sm: 2 }}>
          <TextInput label="Name" required {...form.getInputProps('name')} />
          <TextInput label="Code" required maxLength={20} {...form.getInputProps('code')} />
          <Select label="How it's earned" allowDeselect={false} data={[
            { value: 'Monthly', label: 'Earned monthly (a twelfth a month)' }, { value: 'Yearly', label: 'Granted at the start of each year' },
            { value: 'NoBalance', label: 'No balance (just recorded)' }]} {...form.getInputProps('accrual')} />
          {v.accrual !== 'NoBalance' && <NumberInput label="Days per year" required min={0.5} step={0.5} decimalScale={1} {...num('daysPerYear')} />}
          {v.accrual !== 'NoBalance' && <NumberInput label="Most days carried into next year" min={0} step={0.5} decimalScale={1} {...num('carryForwardMax')} />}
          <NumberInput label="Most days per request" description="Empty: no limit" min={0.5} step={0.5} decimalScale={1} {...num('maxDaysPerRequest')} />
          <NumberInput label="Months of service before use" min={0} max={120} {...num('minServiceMonths')} />
          <NumberInput label="Document needed after (days)" description="Empty: never" min={0} {...num('documentRequiredAfterDays')} />
          <Select label="Who can take it" clearable placeholder="Everyone" data={[{ value: 'Female', label: 'Women only' }, { value: 'Male', label: 'Men only' }]}
            {...form.getInputProps('eligibleGender')} />
        </SimpleGrid>
        <Group>
          <Switch label="Paid" {...form.getInputProps('isPaid', { type: 'checkbox' })} />
          <Switch label="Counts calendar days (not just working days)" {...form.getInputProps('countsCalendarDays', { type: 'checkbox' })} />
        </Group>
        <Textarea label="Description" description="Shown to employees when they choose it" autosize minRows={2} {...form.getInputProps('description')} value={v.description ?? ''} />
        <Group justify="flex-end">
          <Button variant="default" onClick={done}>Cancel</Button>
          <Button type="submit" loading={save.isPending}>Save</Button>
        </Group>
      </Stack>
    </form>
  )
}

function HolidaysTab() {
  const canEdit = useCan('leaveSetup')
  const [year, setYear] = useState(new Date().getFullYear())
  const holidays = useQuery({ queryKey: ['leave', 'holidays', year], queryFn: () => api.get<Holiday[]>('/leave/holidays', { year }) })
  const [adding, setAdding] = useState(false)
  const [note, setNote] = useState<string | null>(null)
  const generate = useApiMutation(() => api.post<{ added: number; alreadyThere: number; note: string }>('/leave/holidays/generate', undefined, { year }), {
    invalidate: ['leave'],
    success: (r) => r.added === 0 ? `${year}'s holidays were already there.` : `${r.added} holidays added for ${year}.`,
    onSuccess: (r) => setNote(r.note),
  })
  const remove = useApiMutation((h: Holiday) => api.delete<void>(`/leave/holidays/${h.id}`), { invalidate: ['leave'], success: (_, h) => `${h.name} removed.` })
  return (
    <Stack maw={700}>
      <Group justify="space-between">
        <Select w={120} allowDeselect={false} value={String(year)} onChange={(v) => { setYear(Number(v)); setNote(null) }}
          data={[-1, 0, 1, 2].map((d) => String(new Date().getFullYear() + d))} />
        {canEdit && <Group>
          <Button variant="default" leftSection={<IconWand size={16} />} loading={generate.isPending} onClick={() => generate.mutate(undefined)}>Add {year}'s holidays</Button>
          <Button leftSection={<IconPlus size={16} />} onClick={() => setAdding(true)}>Add a holiday</Button>
        </Group>}
      </Group>
      {note && <Alert color="yellow" withCloseButton onClose={() => setNote(null)}>{note}</Alert>}
      {holidays.isPending ? <Loading /> : holidays.isError ? <LoadError error={holidays.error} /> : (
        <Table striped>
          <Table.Tbody>
            {holidays.data.length === 0 && <Table.Tr><Table.Td><Text c="dimmed" ta="center" py="md">No holidays for {year} yet.</Text></Table.Td></Table.Tr>}
            {holidays.data.map((h) => (
              <Table.Tr key={h.id}>
                <Table.Td w={170}>{date(h.date)}</Table.Td>
                <Table.Td c="dimmed" w={110}>{new Date(h.date).toLocaleDateString('en-GB', { weekday: 'long' })}</Table.Td>
                <Table.Td>{h.name}</Table.Td>
                <Table.Td w={40}>{canEdit && (
                  <Menu position="bottom-end">
                    <Menu.Target><ActionIcon variant="subtle" aria-label={`Actions for ${h.name}`}><IconDots size={16} /></ActionIcon></Menu.Target>
                    <Menu.Dropdown><Menu.Item color="red" onClick={() => remove.mutate(h)}>Remove</Menu.Item></Menu.Dropdown>
                  </Menu>
                )}</Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      )}
      <Modal opened={adding} onClose={() => setAdding(false)} title="Add a public holiday">
        {adding && <HolidayForm done={() => setAdding(false)} />}
      </Modal>
    </Stack>
  )
}

function HolidayForm({ done }: { done: () => void }) {
  const form = useForm({ initialValues: { date: null as string | null, name: '' }, validate: { date: (v) => (v ? null : 'Required'), name: (v) => (v.trim() ? null : 'Required') } })
  const add = useApiMutation((v: { date: string; name: string }) => api.post<Holiday>('/leave/holidays', v), {
    invalidate: ['leave'], success: (h) => `${h.name} added.`, onSuccess: done,
  })
  return (
    <form onSubmit={form.onSubmit((v) => add.mutate({ date: v.date!, name: v.name.trim() }))}>
      <Stack>
        <DateInput label="Date" required valueFormat="ddd D MMM YYYY" {...form.getInputProps('date')} />
        <TextInput label="Name" required placeholder="Eid al-Fitr" {...form.getInputProps('name')} />
        <Group justify="flex-end"><Button variant="default" onClick={done}>Cancel</Button><Button type="submit" loading={add.isPending}>Add</Button></Group>
      </Stack>
    </form>
  )
}

const week: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']

function WeekTab() {
  const settings = useLeaveSettings()
  if (settings.isPending) return <Loading />
  if (settings.isError) return <LoadError error={settings.error} />
  return <WeekForm settings={settings.data} />
}

function WeekForm({ settings }: { settings: LeaveSettings }) {
  const canEdit = useCan('leaveSetup')
  const [days, setDays] = useState<string[]>(settings.workingDays)
  const save = useApiMutation(() => api.put<LeaveSettings>('/leave/settings/work-week', { workingDays: days }), { invalidate: ['leave'], success: 'Working week saved.' })
  return (
    <Stack maw={560}>
      <Text size="sm">Leave is counted in working days: days off on these days use up leave, other days and public holidays don't. Types set to count calendar days (such as Kenyan maternity leave) count every day.</Text>
      <Checkbox.Group value={days} onChange={setDays} label="Working days">
        <Group mt="xs">{week.map((d) => <Checkbox key={d} value={d} label={d.slice(0, 3)} disabled={!canEdit} />)}</Group>
      </Checkbox.Group>
      <Text size="sm" c="dimmed">The leave year starts in {months[settings.leaveYearStartMonth - 1].label}, with the company's financial year (Settings → Company).</Text>
      {canEdit && <Group><Button disabled={days.length === 0} loading={save.isPending} onClick={() => save.mutate(undefined)}>Save working week</Button></Group>}
      <Alert color="yellow">Changing the week doesn't change requests already made; their days were counted when they were made.</Alert>
    </Stack>
  )
}
