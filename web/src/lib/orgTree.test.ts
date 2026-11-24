import { describe, expect, it } from 'vitest'
import { buildOrgTree, teamSize } from './orgTree'

const n = (employeeId: string, managerId: string | null = null) => ({ employeeId, managerId })

describe('buildOrgTree', () => {
  it('nests reports under their managers', () => {
    const tree = buildOrgTree([n('ceo'), n('cfo', 'ceo'), n('acc', 'cfo'), n('cto', 'ceo')])
    expect(tree).toHaveLength(1)
    expect(tree[0].reports.map((r) => r.node.employeeId)).toEqual(['cfo', 'cto'])
    expect(teamSize(tree[0])).toBe(3)
  })

  it('makes people whose manager is not listed into roots, so nobody is lost', () => {
    const tree = buildOrgTree([n('a', 'gone'), n('b', 'a'), n('c')])
    expect(tree.map((t) => t.node.employeeId)).toEqual(['a', 'c'])
    expect(tree[0].reports[0].node.employeeId).toBe('b')
  })
})
