import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import type { Branch, Department, JobGrade, OrgChartNode, Position } from '../../api/types'

// The org lists are small and change rarely; every page shares these cached copies (query key prefix 'org').
export const useBranches = () => useQuery({ queryKey: ['org', 'branches'], queryFn: () => api.get<Branch[]>('/org/branches') })
export const useDepartments = () => useQuery({ queryKey: ['org', 'departments'], queryFn: () => api.get<Department[]>('/org/departments') })
export const useGrades = () => useQuery({ queryKey: ['org', 'grades'], queryFn: () => api.get<JobGrade[]>('/org/grades') })
export const usePositions = () => useQuery({ queryKey: ['org', 'positions'], queryFn: () => api.get<Position[]>('/org/positions') })
export const useOrgChart = () => useQuery({ queryKey: ['org', 'chart'], queryFn: () => api.get<OrgChartNode[]>('/org/chart') })

/** Select options for active items, keeping the current value even if it has since been deactivated. */
export function options<T extends { id: string; isActive: boolean }>(items: T[] | undefined, label: (t: T) => string, current?: string | null) {
  return (items ?? []).filter((i) => i.isActive || i.id === current).map((i) => ({ value: i.id, label: label(i) }))
}
