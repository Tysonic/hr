import { Alert, Button, Code, Group, Stack, Text, Textarea } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { api } from '../../api/client'
import type { OfferTemplate } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { useApiMutation } from '../../lib/mutation'

export function OfferTemplatePage() {
  const q = useQuery({ queryKey: ['recruitment', 'offer-template'], queryFn: () => api.get<OfferTemplate>('/recruitment/offer-template') })
  if (q.isPending) return <Loading />
  if (q.isError) return <LoadError error={q.error} />
  return <Editor template={q.data} key={q.data.body} />
}

function Editor({ template }: { template: OfferTemplate }) {
  const [body, setBody] = useState(template.body)
  const save = useApiMutation(() => api.put<OfferTemplate>('/recruitment/offer-template', { body }), { invalidate: ['recruitment'], success: 'Offer letter saved.' })
  return (
    <>
      <PageHeader title="Offer letter" description="The wording of job offer letters. Your letterhead, the candidate's details, signature lines and an acceptance slip are added around it." />
      <Stack maw={800}>
        {template.isDefault && <Alert color="blue">This is TyHR's standard wording. Change it to suit your company; it's used for every offer from then on.</Alert>}
        <Text size="sm">
          Placeholders filled from each offer: {template.placeholders.map((p) => <Code key={p} mr={4}>{`{{${p}}}`}</Code>)}
        </Text>
        <Text size="xs" c="dimmed">Leave a blank line between paragraphs. Any extra terms on an offer are added after the last paragraph.</Text>
        <Textarea autosize minRows={14} value={body} onChange={(e) => setBody(e.currentTarget.value)} styles={{ input: { fontFamily: 'inherit' } }} />
        <Group justify="flex-end">
          <Button variant="default" disabled={body === template.body} onClick={() => setBody(template.body)}>Undo changes</Button>
          <Button disabled={!body.trim() || body === template.body} loading={save.isPending} onClick={() => save.mutate(undefined)}>Save</Button>
        </Group>
      </Stack>
    </>
  )
}
