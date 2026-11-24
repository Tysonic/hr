import { Stack, Text, Title } from '@mantine/core'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { ClearanceTable, useMyClearance } from '../exits/components'
import { useMyTasks } from '../me/hooks'
import { TaskTable } from './components'

export function MyTasksPage() {
  const tasks = useMyTasks()
  const clearance = useMyClearance()
  if (tasks.isPending || clearance.isPending) return <Loading />
  if (tasks.isError) return <LoadError error={tasks.error} />
  if (clearance.isError) return <LoadError error={clearance.error} />
  const none = tasks.data.length === 0 && clearance.data.length === 0
  return (
    <>
      <PageHeader title="My tasks" description="Onboarding tasks and exit clearance given to you. Tick each one off when it's done." />
      {none ? <Text c="dimmed">You have no tasks.</Text> : (
        <Stack gap="lg">
          {tasks.data.length > 0 && <Stack gap="xs"><Title order={4}>Onboarding</Title><TaskTable tasks={tasks.data} showEmployee /></Stack>}
          {clearance.data.length > 0 && <Stack gap="xs"><Title order={4}>Exit clearance</Title><ClearanceTable items={clearance.data} showEmployee /></Stack>}
        </Stack>
      )}
    </>
  )
}
