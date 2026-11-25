/** Whether the page is the link's own or one under it ("/employees" covers /employees/5). Home covers only itself. */
const covers = (link: string, pathname: string) => pathname === link || (link !== '/' && pathname.startsWith(`${link}/`))

/**
 * The one menu link to highlight for the page: of those covering it, the most specific, so /leave/approvals lights
 * "Leave requests" but not "My leave" too.
 */
export function activeLink(links: string[], pathname: string): string | undefined {
  return links.filter((link) => covers(link, pathname)).sort((a, b) => b.length - a.length)[0]
}
