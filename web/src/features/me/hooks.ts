import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Inbox, Interview, OnboardingTask } from '../../api/types'

/** What waits for the user's decision; refreshed every minute for the nav badge. */
export const useInbox = (enabled = true) =>
  useQuery({ queryKey: ['inbox'], queryFn: () => api.get<Inbox>('/me/inbox'), enabled, refetchInterval: 60_000 })

/** Interviews the user sits on; the nav shows "My interviews" only when there are some. */
export const useMyInterviews = (enabled = true) =>
  useQuery({ queryKey: ['me', 'interviews'], queryFn: () => api.get<Interview[]>('/me/interviews'), enabled, refetchInterval: 300_000 })

/** Interviews that have started and still need the user's scorecard. */
export const scorecardsOwed = (list: Interview[] | undefined) =>
  (list ?? []).filter((i) => i.status !== 'Cancelled' && !i.myScorecard && new Date(i.startsAt) <= new Date()).length

/** Onboarding tasks given to the user; the nav shows "My tasks" only when there are some. */
export const useMyTasks = (enabled = true) =>
  useQuery({ queryKey: ['me', 'tasks'], queryFn: () => api.get<OnboardingTask[]>('/me/tasks'), enabled, refetchInterval: 300_000 })
