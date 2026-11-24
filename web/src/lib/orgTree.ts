// Builds the org chart tree from the API's flat list (each node names its manager). Kept free of React so it can be
// unit-tested.

export interface FlatNode { employeeId: string; managerId: string | null }

export interface TreeNode<T> { node: T; reports: TreeNode<T>[] }

/**
 * Roots are people without a manager, or whose manager isn't in the list (e.g. has left), so nobody is dropped.
 * Reports keep the list's order.
 */
export function buildOrgTree<T extends FlatNode>(nodes: readonly T[]): TreeNode<T>[] {
  const ids = new Set(nodes.map((n) => n.employeeId))
  const byManager = new Map<string, T[]>()
  for (const n of nodes) {
    if (n.managerId && ids.has(n.managerId)) byManager.set(n.managerId, [...(byManager.get(n.managerId) ?? []), n])
  }
  const seen = new Set<string>()
  const build = (n: T): TreeNode<T> => {
    seen.add(n.employeeId)
    return { node: n, reports: (byManager.get(n.employeeId) ?? []).filter((r) => !seen.has(r.employeeId)).map(build) }
  }
  return nodes.filter((n) => !n.managerId || !ids.has(n.managerId)).map(build)
}

/** How many people report to this node, directly or further down. */
export function teamSize<T>(tree: TreeNode<T>): number {
  return tree.reports.reduce((sum, r) => sum + 1 + teamSize(r), 0)
}
