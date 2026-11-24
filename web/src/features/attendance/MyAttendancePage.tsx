import { Alert, Button, Card, Group, Stack, Text, Title } from '@mantine/core'
import { IconLogin, IconLogout } from '@tabler/icons-react'
import { useQuery } from '@tanstack/react-query'
import dayjs from 'dayjs'
import { useEffect, useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { ClockDirection, MyClock } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'
import { clockTime, TimesheetView, useMonth } from './components'

/** Where the browser says we are, if the person allows it; never blocks clocking for more than a few seconds. */
function position(): Promise<GeolocationCoordinates | null> {
  if (!('geolocation' in navigator)) return Promise.resolve(null)
  return new Promise((resolve) => navigator.geolocation.getCurrentPosition((p) => resolve(p.coords), () => resolve(null),
    { enableHighAccuracy: true, timeout: 6000, maximumAge: 60000 }))
}

export function MyAttendancePage() {
  const clock = useQuery({
    queryKey: ['attendance', 'me'],
    queryFn: () => api.get<MyClock>('/attendance/me'),
    retry: (count, e) => !(e instanceof ApiError && e.status === 400) && count < 2,
  })
  const month = useMonth()
  if (clock.isError && clock.error instanceof ApiError && clock.error.status === 400)
    return <><PageHeader title="My attendance" /><Alert color="yellow" title="No employee record yet">{clock.error.message}</Alert></>
  if (clock.isPending) return <Loading />
  if (clock.isError) return <LoadError error={clock.error} />

  return (
    <>
      <PageHeader title="My attendance" />
      <Stack gap="xl">
        <ClockCard clock={clock.data} />
        <Stack gap="xs">
          <Group justify="space-between"><Title order={4}>Timesheet</Title>{month.control}</Group>
          <TimesheetView from={month.from} to={month.to} />
        </Stack>
      </Stack>
    </>
  )
}

function ClockCard({ clock: c }: { clock: MyClock }) {
  const [now, setNow] = useState(() => dayjs())
  useEffect(() => {
    const timer = setInterval(() => setNow(dayjs()), 15000)
    return () => clearInterval(timer)
  }, [])
  const punch = useApiMutation(async (direction: ClockDirection) => {
    const coords = await position()
    return api.post<MyClock>('/attendance/clock', {
      direction, latitude: coords?.latitude ?? null, longitude: coords?.longitude ?? null, accuracyMeters: coords?.accuracy ?? null,
    })
  }, { invalidate: ['attendance'], success: (r) => `Clocked ${r.today[r.today.length - 1].direction.toLowerCase()} at ${clockTime(r.today[r.today.length - 1].localTime)}.` })
  const clockingIn = c.next !== 'Out'

  return (
    <Card withBorder padding="lg" maw={560}>
      <Group justify="space-between" align="flex-start">
        <Stack gap={2}>
          <Text size="sm" c="dimmed">{now.format('dddd D MMMM')}</Text>
          <Text fz={36} fw={700} lh={1.1}>{now.format('HH:mm')}</Text>
          {c.scheduleName && <Text size="sm" c="dimmed">{c.scheduleName}: {clockTime(c.startTime)}–{clockTime(c.endTime)}</Text>}
        </Stack>
        <Button size="lg" color={clockingIn ? 'green' : 'red'} loading={punch.isPending}
          leftSection={clockingIn ? <IconLogin size={20} /> : <IconLogout size={20} />} onClick={() => punch.mutate(clockingIn ? 'In' : 'Out')}>
          Clock {clockingIn ? 'in' : 'out'}
        </Button>
      </Group>
      <Text size="sm" mt="md">
        {c.today.length === 0 ? 'Not clocked in today.'
          : `Today: ${c.today.map((p) => `${p.direction === 'Out' ? 'out' : 'in'} ${clockTime(p.localTime)}`).join(', ')}`}
      </Text>
      <Text size="xs" c="dimmed" mt={4}>Your location is recorded with each clocking if your browser allows it. Missed a clocking? Ask HR to add it.</Text>
    </Card>
  )
}
