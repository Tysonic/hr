import { Box, Group, Stack, Text, Tooltip } from '@mantine/core'

/**
 * Small single-series charts in the theme's primary colour, so they follow light and dark mode. Values sit in text
 * colours beside the marks; every mark has a hover tooltip, and each chart sits next to a table of the same numbers.
 */

const bar = 'var(--mantine-primary-color-filled)'

/** Horizontal bars for categories (largest first unless the order means something, e.g. age bands). */
export function BarList({ items, format = (v) => v.toLocaleString('en-GB') }: { items: { label: string; value: number }[]; format?: (v: number) => string }) {
  const max = Math.max(...items.map((i) => i.value), 0)
  if (items.length === 0) return <Text size="sm" c="dimmed">Nothing to show.</Text>
  return (
    <Stack gap={6}>
      {items.map((i) => (
        <Tooltip key={i.label} label={`${i.label}: ${format(i.value)}`} openDelay={150}>
          <Box>
            <Group justify="space-between" gap="xs" wrap="nowrap">
              <Text size="sm" truncate>{i.label}</Text>
              <Text size="sm" fw={500} style={{ fontVariantNumeric: 'tabular-nums' }}>{format(i.value)}</Text>
            </Group>
            <Box h={8} mt={2} style={{ background: 'var(--mantine-color-default-border)', borderRadius: 4 }}>
              <Box h={8} w={`${max > 0 ? Math.max((i.value / max) * 100, i.value > 0 ? 1 : 0) : 0}%`} style={{ background: bar, borderRadius: 4 }} />
            </Box>
          </Box>
        </Tooltip>
      ))}
    </Stack>
  )
}

/** Columns over time, one per month, labelled under the axis; the value shows on hover and for the latest month. */
export function ColumnChart({ points, format = (v) => v.toLocaleString('en-GB'), height = 140 }: {
  points: { key: string; label: string; tooltip: string; value: number }[]; format?: (v: number) => string; height?: number
}) {
  const max = Math.max(...points.map((p) => p.value), 0)
  const last = points[points.length - 1]
  return (
    <Box>
      {last && <Text size="xs" c="dimmed" ta="right">{last.tooltip}: <Text span size="xs" fw={600} c="var(--mantine-color-text)">{format(last.value)}</Text></Text>}
      <Group gap={2} align="flex-end" wrap="nowrap" h={height} mt={4} style={{ borderBottom: '1px solid var(--mantine-color-default-border)' }}>
        {points.map((p) => (
          <Tooltip key={p.key} label={`${p.tooltip}: ${format(p.value)}`} openDelay={50}>
            <Box h="100%" style={{ flex: 1, display: 'flex', alignItems: 'flex-end', cursor: 'default' }}>
              <Box w="100%" h={`${max > 0 ? (Math.max(p.value, 0) / max) * 100 : 0}%`} mah="100%"
                style={{ background: bar, borderRadius: '4px 4px 0 0', minHeight: p.value > 0 ? 2 : 0 }} />
            </Box>
          </Tooltip>
        ))}
      </Group>
      <Group gap={2} wrap="nowrap" mt={4}>
        {points.map((p) => <Text key={p.key} size="10px" c="dimmed" ta="center" style={{ flex: 1 }} truncate>{p.label}</Text>)}
      </Group>
    </Box>
  )
}

export const shortMonth = (year: number, month: number) => new Date(year, month - 1, 1).toLocaleDateString('en-GB', { month: 'short' })
export const longMonth = (year: number, month: number) => new Date(year, month - 1, 1).toLocaleDateString('en-GB', { month: 'long', year: 'numeric' })
