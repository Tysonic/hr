import { describe, expect, it } from 'vitest'
import { activeLink } from './activeLink'

describe('activeLink', () => {
  const links = ['/', '/me', '/leave', '/leave/approvals', '/payroll', '/recruitment', '/recruitment/requisitions', '/settings/leave']

  it('lights only the most specific link covering the page', () => {
    expect(activeLink(links, '/leave')).toBe('/leave')
    expect(activeLink(links, '/leave/approvals')).toBe('/leave/approvals')
    expect(activeLink(links, '/recruitment/requisitions')).toBe('/recruitment/requisitions')
    expect(activeLink(links, '/settings/leave')).toBe('/settings/leave')
  })

  it("keeps a list lit on its records' own pages", () => {
    expect(activeLink(links, '/recruitment/postings/5')).toBe('/recruitment')
    expect(activeLink(links, '/payroll/runs/5')).toBe('/payroll')
  })

  it('lights home only on its own page, and nothing for a page that only starts the same way', () => {
    expect(activeLink(links, '/')).toBe('/')
    expect(activeLink(links, '/team')).toBeUndefined()
    expect(activeLink(links, '/payrolls')).toBeUndefined()
  })
})
