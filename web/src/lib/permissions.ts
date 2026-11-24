import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Country } from '../api/types'
import { can, type Policy, type Role } from './policies'

export { can, policies, roleLabels, type Policy, type Role } from './policies'

export interface Me {
  userName: string
  displayName: string
  email: string | null
  roles: Role[]
  company: { slug: string; name: string; country: Country } | null
}

export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: () => api.get<Me>('/me'), staleTime: Infinity })
}

/** Whether the signed-in user holds a role the policy allows. False while loading. */
export function useCan(policy: Policy): boolean {
  const { data } = useMe()
  return can(data?.roles ?? [], policy)
}
