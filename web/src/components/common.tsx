import { Alert, Center, Group, Loader, Stack, Text, Title } from '@mantine/core'
import type { ReactNode } from 'react'

export function PageHeader({ title, description, actions }: { title: string; description?: ReactNode; actions?: ReactNode }) {
  return (
    <Group justify="space-between" align="flex-start" mb="lg" wrap="wrap">
      <Stack gap={2}>
        <Title order={2}>{title}</Title>
        {description && <Text c="dimmed" size="sm">{description}</Text>}
      </Stack>
      {actions}
    </Group>
  )
}

export function Loading() {
  return <Center py="xl"><Loader /></Center>
}

export function LoadError({ error }: { error: Error }) {
  return <Alert color="red" title="Could not load">{error.message}</Alert>
}
