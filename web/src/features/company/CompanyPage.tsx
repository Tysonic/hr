import { Button, Card, Group, Select, SimpleGrid, Stack, Text, TextInput, Textarea } from '@mantine/core'
import { useForm } from '@mantine/form'
import { useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { api } from '../../api/client'
import type { Company, UpdateCompanyRequest } from '../../api/types'
import { LoadError, Loading, PageHeader } from '../../components/common'
import { months } from '../../lib/format'
import { useApiMutation } from '../../lib/mutation'
import { useCan } from '../../lib/permissions'

export function CompanyPage() {
  const company = useQuery({ queryKey: ['company'], queryFn: () => api.get<Company>('/company') })
  if (company.isPending) return <Loading />
  if (company.isError) return <LoadError error={company.error} />
  return <CompanyForm company={company.data} />
}

function CompanyForm({ company }: { company: Company }) {
  const canEdit = useCan('companySettings')
  const form = useForm<UpdateCompanyRequest>({
    initialValues: toRequest(company),
    validate: {
      name: (v) => (v.trim() ? null : 'Required'),
      legalName: (v) => (v.trim() ? null : 'Required'),
      email: (v) => (/^\S+@\S+\.\S+$/.test(v) ? null : 'Enter an email address'),
    },
  })
  useEffect(() => { form.setValues(toRequest(company)); form.resetDirty() }, [company]) // eslint-disable-line react-hooks/exhaustive-deps

  const save = useApiMutation((v: UpdateCompanyRequest) => api.put<Company>('/company', v), {
    invalidate: ['company', 'me'],
    success: 'Company details saved.',
  })
  const { country } = company

  return (
    <>
      <PageHeader title="Company" description={`Company code ${company.slug} · ${country.name} country pack (${country.currencyCode})`} />
      <form onSubmit={form.onSubmit((v) => save.mutate(v))}>
        <fieldset disabled={!canEdit} style={{ border: 0, padding: 0, margin: 0 }}>
          <Stack maw={900}>
            <Card withBorder>
              <Text fw={600} mb="sm">Identity</Text>
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label="Display name" required {...form.getInputProps('name')} />
                <TextInput label="Legal name" required {...form.getInputProps('legalName')} />
                <TextInput label="Trading name" {...form.getInputProps('tradingName')} value={form.values.tradingName ?? ''} />
                <Select label="Financial year starts" data={months}
                  value={String(form.values.financialYearStartMonth)}
                  onChange={(v) => form.setFieldValue('financialYearStartMonth', Number(v ?? 1))} />
              </SimpleGrid>
            </Card>
            <Card withBorder>
              <Text fw={600} mb="sm">Registration</Text>
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label={country.taxNumberLabel} {...form.getInputProps('taxNumber')} value={form.values.taxNumber ?? ''} />
                <TextInput label={`Employer ${country.socialSecurityLabel}`} {...form.getInputProps('socialSecurityNumber')}
                  value={form.values.socialSecurityNumber ?? ''} />
              </SimpleGrid>
            </Card>
            <Card withBorder>
              <Text fw={600} mb="sm">Contacts</Text>
              <SimpleGrid cols={{ base: 1, sm: 2 }}>
                <TextInput label="HR email" required {...form.getInputProps('email')} />
                <TextInput label="Phone" placeholder={`${country.phonePrefix} …`} {...form.getInputProps('phone')} value={form.values.phone ?? ''} />
                <Textarea label="Physical address" autosize minRows={2} {...form.getInputProps('physicalAddress')} value={form.values.physicalAddress ?? ''} />
                <Textarea label="Postal address" autosize minRows={2} {...form.getInputProps('postalAddress')} value={form.values.postalAddress ?? ''} />
              </SimpleGrid>
            </Card>
            {canEdit && (
              <Group justify="flex-end">
                <Button variant="default" disabled={!form.isDirty()} onClick={() => form.reset()}>Discard</Button>
                <Button type="submit" loading={save.isPending} disabled={!form.isDirty()}>Save</Button>
              </Group>
            )}
          </Stack>
        </fieldset>
      </form>
    </>
  )
}

function toRequest(c: Company): UpdateCompanyRequest {
  const { slug: _slug, country: _country, ...rest } = c
  return rest
}
