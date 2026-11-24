import { describe, expect, it } from 'vitest'
import source from '../../../api/src/TyHr.Application/Common/Permissions.cs?raw'
import { policies } from './policies'

// policies.ts mirrors the API's Permissions.cs; this keeps the two from drifting apart.
const roleValues: Record<string, string> = {
  PlatformAdmin: 'platform-admin', HrAdmin: 'hr-admin', HrOfficer: 'hr-officer', PayrollOfficer: 'payroll-officer', LineManager: 'line-manager', Employee: 'employee',
}
const company = ['hr-admin', 'hr-officer', 'payroll-officer', 'line-manager', 'employee']
/** UI-only groupings with no single API permission. */
const uiOnly = ['inbox', 'plans']

function apiPolicies(): Record<string, string[]> {
  const result: Record<string, string[]> = {}
  for (const [, name, roles] of source.matchAll(/\[(\w+)\] = (Roles\.Company|\[[^\]]*\]),/g)) {
    const key = name[0].toLowerCase() + name.slice(1)
    result[key] = roles === 'Roles.Company' ? company : [...roles.matchAll(/Roles\.(\w+)/g)].map((m) => roleValues[m[1]])
  }
  return result
}

describe('policies', () => {
  it('match the API permissions role for role', () => {
    const api = apiPolicies()
    expect(Object.keys(api).length).toBeGreaterThan(30)
    const web = Object.fromEntries(Object.entries(policies).filter(([k]) => !uiOnly.includes(k)).map(([k, v]) => [k, [...v].sort()]))
    expect(web).toEqual(Object.fromEntries(Object.entries(api).map(([k, v]) => [k, [...v].sort()])))
  })
})
