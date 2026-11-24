import { Badge } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { AppraisalListItem, AppraisalStatus, CycleStatus, DirectoryEntry, MyAppraisals, PagedResult, PlanProgress, PlanStatus } from '../../api/types'

const cycleLabel: Record<CycleStatus, [string, string]> = {
  Draft: ['Draft', 'gray'], GoalSetting: ['Goal setting', 'blue'], Review: ['Reviews open', 'grape'], Closed: ['Closed', 'dark'],
}
export const CycleBadge = ({ value }: { value: CycleStatus }) => <Badge variant="light" color={cycleLabel[value][1]}>{cycleLabel[value][0]}</Badge>

/** Where an appraisal is, in words that say whose turn it is. */
export function stageText(a: { status: AppraisalStatus; goalsAgreed: boolean; goalsSubmitted: boolean; acknowledged: boolean }) {
  switch (a.status) {
    case 'GoalSetting': return a.goalsAgreed ? 'Goals agreed' : a.goalsSubmitted ? 'Goals sent to manager' : 'Drafting goals'
    case 'SelfReview': return 'Self-review'
    case 'ManagerReview': return "Manager's review"
    case 'Calibration': return 'Calibration'
    default: return a.acknowledged ? 'Complete, acknowledged' : 'Complete'
  }
}
const stageColor: Record<AppraisalStatus, string> = { GoalSetting: 'blue', SelfReview: 'orange', ManagerReview: 'grape', Calibration: 'yellow', Completed: 'green' }
export const StageBadge = ({ a }: { a: Parameters<typeof stageText>[0] }) => <Badge variant="light" color={stageColor[a.status]}>{stageText(a)}</Badge>

/** "3.6 Exceeds expectations" from the cycle's scale. */
export const ratingText = (rating: number | null | undefined, scale: string[]) =>
  rating == null ? '—' : `${rating.toFixed(1)} ${scale[Math.min(4, Math.max(0, Math.round(rating) - 1))]}`

export const useMyAppraisals = (enabled = true) =>
  useQuery({ queryKey: ['appraisals', 'mine'], queryFn: () => api.get<MyAppraisals>('/me/appraisals'), enabled, refetchInterval: 300_000 })

export type { AppraisalListItem }

/** Colleagues anyone may pick (the staff directory), e.g. to ask for feedback. */
export const useColleagues = () =>
  useQuery({ queryKey: ['directory', 'all'], queryFn: () => api.get<PagedResult<DirectoryEntry>>('/directory', { pageSize: 200 }) })

const planLabel: Record<PlanStatus, [string, string]> = {
  Active: ['Active', 'blue'], Succeeded: ['Succeeded', 'green'], NotSucceeded: ['Not succeeded', 'red'], Cancelled: ['Cancelled', 'gray'],
}
export const PlanBadge = ({ value }: { value: PlanStatus }) => <Badge variant="light" color={planLabel[value][1]}>{planLabel[value][0]}</Badge>

export const progressLabel: Record<PlanProgress, [string, string]> = { OnTrack: ['On track', 'green'], AtRisk: ['At risk', 'orange'], OffTrack: ['Off track', 'red'] }
export const ProgressBadge = ({ value }: { value: PlanProgress }) => <Badge variant="dot" color={progressLabel[value][1]}>{progressLabel[value][0]}</Badge>
