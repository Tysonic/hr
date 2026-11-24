import {
  Alert, Anchor, Badge, Button, Card, Checkbox, Container, FileInput, Group, SimpleGrid, Stack, Text, Textarea, TextInput, Title,
} from '@mantine/core'
import { useForm } from '@mantine/form'
import { IconArrowLeft, IconCircleCheck } from '@tabler/icons-react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { createBrowserRouter, Link, RouterProvider, useParams } from 'react-router'
import { api } from '../../api/client'
import type { Careers, PublicPosting } from '../../api/types'
import { LoadError, Loading } from '../../components/common'
import { date } from '../../lib/format'
import { contractTypes, label } from '../employees/fields'

// The public careers page: no sign-in, its own small router, nothing from the staff app's layout or navigation.

const router = createBrowserRouter([
  { path: '/careers/:slug', element: <JobsPage /> },
  { path: '/careers/:slug/jobs/:id', element: <JobPage /> },
  { path: '*', element: <Container py="xl"><Text>There's no such page.</Text></Container> },
])

export function CareersApp() {
  return <RouterProvider router={router} />
}

const useCareers = (slug: string) => useQuery({ queryKey: ['careers', slug], queryFn: () => api.get<Careers>(`/public/careers/${slug}`) })

function Shell({ company, children }: { company?: string; children: React.ReactNode }) {
  return (
    <Container size="md" py="xl">
      <Stack gap="lg">
        <Stack gap={0}>
          <Title order={2}>{company ?? 'Careers'}</Title>
          <Text c="dimmed">Careers</Text>
        </Stack>
        {children}
        <Text size="xs" c="dimmed" ta="center" mt="xl">Powered by TyHR</Text>
      </Stack>
    </Container>
  )
}

function Meta({ job }: { job: PublicPosting }) {
  return (
    <Group gap="xs">
      <Badge variant="light">{label(contractTypes, job.employmentType)}</Badge>
      {job.location && <Text size="sm" c="dimmed">{job.location}</Text>}
      {job.departmentName && <Text size="sm" c="dimmed">· {job.departmentName}</Text>}
      {job.closingDate && <Text size="sm" c="dimmed">· closes {date(job.closingDate)}</Text>}
    </Group>
  )
}

function JobsPage() {
  const { slug = '' } = useParams()
  const careers = useCareers(slug)
  if (careers.isPending) return <Loading />
  if (careers.isError) return <Shell><LoadError error={careers.error} /></Shell>
  const c = careers.data
  return (
    <Shell company={c.companyName}>
      {c.jobs.length === 0 && <Text c="dimmed">There are no open jobs right now. Please check again later.</Text>}
      {c.jobs.map((j) => (
        <Card key={j.id} withBorder component={Link} to={`/careers/${slug}/jobs/${j.id}`}>
          <Text fw={600} size="lg">{j.title}</Text>
          <Meta job={j} />
          {j.salary && <Text size="sm" mt={4}>{j.salary}</Text>}
        </Card>
      ))}
    </Shell>
  )
}

function JobPage() {
  const { slug = '', id = '' } = useParams()
  const careers = useCareers(slug)
  const job = useQuery({ queryKey: ['careers', slug, id], queryFn: () => api.get<PublicPosting>(`/public/careers/${slug}/jobs/${id}`), retry: false })
  if (job.isPending) return <Loading />
  return (
    <Shell company={careers.data?.companyName}>
      <Anchor component={Link} to={`/careers/${slug}`} size="sm"><Group gap={4}><IconArrowLeft size={14} /> All jobs</Group></Anchor>
      {job.isError ? <Alert color="yellow">{job.error.message}</Alert> : (
        <>
          <Stack gap={4}>
            <Title order={3}>{job.data.title}</Title>
            <Meta job={job.data} />
            {job.data.salary && <Text size="sm">{job.data.salary}</Text>}
          </Stack>
          <Text style={{ whiteSpace: 'pre-wrap' }}>{job.data.description}</Text>
          <ApplyForm slug={slug} job={job.data} company={careers.data?.companyName ?? 'the company'} />
        </>
      )}
    </Shell>
  )
}

function ApplyForm({ slug, job, company }: { slug: string; job: PublicPosting; company: string }) {
  const [sent, setSent] = useState(false)
  const form = useForm({
    initialValues: { firstName: '', lastName: '', email: '', phone: '', coverLetter: '', website: '', cv: null as File | null, consent: false },
    validate: {
      firstName: (v: string) => (v.trim() ? null : 'Required'),
      lastName: (v: string) => (v.trim() ? null : 'Required'),
      email: (v: string) => (/^\S+@\S+\.\S+$/.test(v.trim()) ? null : 'Enter a valid email'),
      cv: (v: File | null) => (!v ? 'Attach your CV' : v.size > 5 * 1024 * 1024 ? 'At most 5 MB' : null),
      consent: (v: boolean) => (v ? null : 'Please agree to continue'),
    },
  })
  const apply = useMutation({ mutationFn: (f: FormData) => api.upload<void>(`/public/careers/${slug}/jobs/${job.id}/apply`, f), onSuccess: () => setSent(true) })
  if (sent) {
    return (
      <Alert color="green" icon={<IconCircleCheck />} title="Application sent">
        Thank you for applying for {job.title}. {company} will contact you if you're shortlisted.
      </Alert>
    )
  }
  return (
    <Card withBorder>
      <Text fw={600} mb="sm">Apply for this job</Text>
      <form onSubmit={form.onSubmit((v) => {
        const f = new FormData()
        f.set('firstName', v.firstName.trim()); f.set('lastName', v.lastName.trim()); f.set('email', v.email.trim())
        if (v.phone.trim()) f.set('phone', v.phone.trim())
        if (v.coverLetter.trim()) f.set('coverLetter', v.coverLetter.trim())
        f.set('website', v.website)
        f.set('consent', 'true')
        if (v.cv) f.set('cv', v.cv)
        apply.mutate(f)
      })}>
        <Stack>
          <SimpleGrid cols={{ base: 1, sm: 2 }}>
            <TextInput label="First name" required autoComplete="given-name" {...form.getInputProps('firstName')} />
            <TextInput label="Last name" required autoComplete="family-name" {...form.getInputProps('lastName')} />
            <TextInput label="Email" required type="email" autoComplete="email" {...form.getInputProps('email')} />
            <TextInput label="Phone" type="tel" autoComplete="tel" {...form.getInputProps('phone')} />
          </SimpleGrid>
          {/* Hidden from people; bots fill it in and their application is dropped. */}
          <input type="text" tabIndex={-1} autoComplete="off" aria-hidden="true" style={{ position: 'absolute', left: '-10000px' }}
            {...form.getInputProps('website')} />
          <FileInput label="CV" required accept=".pdf,.docx" placeholder="PDF or Word (.docx), up to 5 MB" clearable {...form.getInputProps('cv')} />
          <Textarea label="Cover letter (optional)" autosize minRows={3} maxLength={10000} {...form.getInputProps('coverLetter')} />
          <Checkbox {...form.getInputProps('consent', { type: 'checkbox' })}
            label={`I agree that ${company} may keep and use the details and CV I send here to consider me for this job.`} />
          {apply.isError && <Alert color="red">{apply.error.message}</Alert>}
          <Group justify="flex-end"><Button type="submit" loading={apply.isPending}>Send application</Button></Group>
        </Stack>
      </form>
    </Card>
  )
}
