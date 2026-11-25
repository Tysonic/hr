import { Accordion, ActionIcon, Badge, Tooltip } from '@mantine/core'
import { IconChevronLeft } from '@tabler/icons-react'
import { useEffect, useState, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { activeLink } from '../lib/activeLink'
import './layout.css'

export interface MenuItem {
  to: string
  label: string
  icon: ReactNode
  /** A count needing attention, shown when above 0. */
  badge?: number
}

export interface MenuSection {
  title: string
  icon: ReactNode
  items: MenuItem[]
}

/**
 * The menu, laid out like Tycount's: the brand, the everyday links, then one collapsible section per area. The section
 * of the page you're on is open. As a rail it shows icons only.
 */
export function Sidebar({ items, sections, rail, onClose, onNavigate }: {
  items: MenuItem[]
  sections: MenuSection[]
  rail: boolean
  onClose: () => void
  onNavigate: () => void
}) {
  const { pathname } = useLocation()
  const active = activeLink([...items, ...sections.flatMap((s) => s.items)].map((i) => i.to), pathname)
  const here = sections.find((s) => s.items.some((i) => i.to === active))?.title
  const [opened, setOpened] = useState<string | null>(here ?? null)
  useEffect(() => {
    if (here) setOpened(here)
  }, [here])

  const list = (entries: MenuItem[]) => (
    <ul className="app-sidebar-list">
      {entries.map((item) => (
        <li key={item.to}>
          <Tooltip label={item.label} position="right" disabled={!rail}>
            <Link to={item.to} onClick={onNavigate} className={`app-sidebar-link${item.to === active ? ' active' : ''}`}
              aria-current={item.to === active ? 'page' : undefined}>
              {item.icon}
              <span>{item.label}</span>
              {!!item.badge && <Badge className="app-sidebar-badge" size="sm" color="red" circle={item.badge < 10}>{item.badge}</Badge>}
            </Link>
          </Tooltip>
        </li>
      ))}
    </ul>
  )

  return (
    <>
      <div className="app-sidebar-header">
        <Link to="/" className="app-sidebar-brand" aria-label="TyHR home" onClick={onNavigate}>
          <span className="app-sidebar-mark" aria-hidden>HR</span>
          {!rail && <span>TyHR</span>}
        </Link>
        {!rail && (
          <ActionIcon className="frame-btn" variant="default" aria-label="Close menu" onClick={onClose}>
            <IconChevronLeft size={16} />
          </ActionIcon>
        )}
      </div>
      <nav className="app-sidebar-menu" aria-label="Main menu">
        {list(items)}
        <Accordion value={opened} onChange={setOpened} radius={0} classNames={{
          item: 'app-sidebar-section', control: 'app-sidebar-control', label: 'app-sidebar-control-label', icon: 'app-sidebar-icon',
          chevron: 'app-sidebar-chevron', content: 'app-sidebar-panel',
        }}>
          {sections.map((section) => (
            <Accordion.Item key={section.title} value={section.title}>
              <Tooltip label={section.title} position="right" disabled={!rail}>
                <Accordion.Control icon={section.icon}>{section.title}</Accordion.Control>
              </Tooltip>
              <Accordion.Panel>{list(section.items)}</Accordion.Panel>
            </Accordion.Item>
          ))}
        </Accordion>
      </nav>
    </>
  )
}
