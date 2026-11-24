import { Badge } from '@mantine/core'
import type { ApplicationStage, PostingStatus, RequisitionStatus } from '../../api/types'

export const requisitionColor: Record<RequisitionStatus, string> = { Pending: 'yellow', Approved: 'green', Rejected: 'red', Closed: 'gray' }
export const requisitionLabel: Record<RequisitionStatus, string> = { Pending: 'Waiting for approval', Approved: 'Approved', Rejected: 'Declined', Closed: 'Closed' }
export const RequisitionBadge = ({ status }: { status: RequisitionStatus }) =>
  <Badge variant="light" color={requisitionColor[status]}>{requisitionLabel[status]}</Badge>

export const postingColor: Record<PostingStatus, string> = { Draft: 'gray', Open: 'green', Closed: 'dark' }
export const PostingBadge = ({ status, accepting }: { status: PostingStatus; accepting?: boolean }) =>
  <Badge variant="light" color={status === 'Open' && accepting === false ? 'orange' : postingColor[status]}>
    {status === 'Open' && accepting === false ? 'Past closing date' : status}
  </Badge>

export const stages: { value: ApplicationStage; label: string; color: string }[] = [
  { value: 'Applied', label: 'Applied', color: 'blue' },
  { value: 'Screening', label: 'Screening', color: 'cyan' },
  { value: 'Interview', label: 'Interview', color: 'violet' },
  { value: 'Offer', label: 'Offer', color: 'orange' },
  { value: 'Hired', label: 'Hired', color: 'green' },
  { value: 'Rejected', label: 'Not selected', color: 'red' },
  { value: 'Withdrawn', label: 'Withdrawn', color: 'gray' },
]
export const stage = (s: ApplicationStage) => stages.find((x) => x.value === s)!
export const StageBadge = ({ value }: { value: ApplicationStage }) => <Badge variant="light" color={stage(value).color}>{stage(value).label}</Badge>
