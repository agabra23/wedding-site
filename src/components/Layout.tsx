import { useEffect, useState, type ReactNode } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'

function Icon({ children }: { children: ReactNode }) {
  return (
    <svg
      viewBox="0 0 24 24"
      className="size-5 shrink-0"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {children}
    </svg>
  )
}

const NAV_ITEMS = [
  {
    to: '/',
    label: 'Home',
    icon: (
      <Icon>
        <path d="M3 10.5 12 3l9 7.5" />
        <path d="M5 9.5V20h14V9.5" />
        <path d="M10 20v-5.5h4V20" />
      </Icon>
    ),
  },
  {
    to: '/faq',
    label: 'FAQ',
    icon: (
      <Icon>
        <circle cx="12" cy="12" r="9" />
        <path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5" />
        <path d="M12 16.5h.01" />
      </Icon>
    ),
  },
  {
    to: '/registry',
    label: 'Registry',
    icon: (
      <Icon>
        <rect x="3.5" y="9" width="17" height="11" rx="1.5" />
        <path d="M3 9h18v-2.5H3z" />
        <path d="M12 6.5V20" />
        <path d="M12 6.5C12 4 9.5 3 8.5 4.2S9 6.5 12 6.5Zm0 0c0-2.5 2.5-3.5 3.5-2.3S15 6.5 12 6.5Z" />
      </Icon>
    ),
  },
  {
    to: '/rsvp',
    label: 'RSVP',
    icon: (
      <Icon>
        <rect x="3" y="5" width="18" height="14" rx="2" />
        <path d="m3.5 7 8.5 6.5L20.5 7" />
      </Icon>
    ),
  },
  {
    to: '/travel',
    label: 'Travel',
    icon: (
      <Icon>
        <path d="M21 3 10.5 13.5" />
        <path d="M21 3 14.5 21l-4-7.5L3 9.5 21 3Z" />
      </Icon>
    ),
  },
]

const desktopLinkClass = ({ isActive }: { isActive: boolean }) =>
  `block ${isActive ? 'text-primary font-medium underline underline-offset-4' : 'text-ink-muted hover:text-ink'}`

const mobileLinkClass = ({ isActive }: { isActive: boolean }) =>
  `flex items-center gap-3 rounded-xl px-3 py-3 transition-colors duration-200 ${
    isActive ? 'bg-primary text-on-primary font-medium' : 'text-ink-muted hover:bg-background hover:text-ink'
  }`

export default function Layout() {
  const [open, setOpen] = useState(false)
  const { pathname } = useLocation()

  useEffect(() => setOpen(false), [pathname])

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open])

  const bar =
    'absolute left-0 h-[1.5px] w-full rounded-full bg-current transition-all duration-300 ease-out'

  return (
    <div className="min-h-screen">
      <header className="border-border bg-surface relative z-20 border-b">
        <div className="mx-auto flex max-w-4xl items-center justify-between px-4 py-4">
          <Link to="/" className="font-display text-xl">
            Our Wedding
          </Link>
          <button
            type="button"
            className="text-ink hover:bg-background -mr-2 grid size-10 place-items-center rounded-full transition-colors md:hidden"
            aria-label={open ? 'Close menu' : 'Open menu'}
            aria-expanded={open}
            aria-controls="mobile-nav"
            onClick={() => setOpen((o) => !o)}
          >
            <span className="relative block h-4 w-5" aria-hidden="true">
              <span className={`${bar} ${open ? 'top-1/2 -translate-y-1/2 rotate-45' : 'top-0'}`} />
              <span
                className={`${bar} top-1/2 -translate-y-1/2 ${open ? 'scale-x-0 opacity-0' : ''}`}
              />
              <span
                className={`${bar} ${open ? 'top-1/2 -translate-y-1/2 -rotate-45' : 'top-full -translate-y-[1.5px]'}`}
              />
            </span>
          </button>
          <nav className="hidden md:block">
            <ul className="flex gap-6">
              {NAV_ITEMS.map((item) => (
                <li key={item.to}>
                  <NavLink to={item.to} end className={desktopLinkClass}>
                    {item.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>

        <div
          id="mobile-nav"
          inert={!open}
          className={`bg-surface border-border absolute inset-x-0 top-full grid border-b shadow-lg transition-[grid-template-rows,opacity] duration-300 ease-out md:hidden ${
            open ? 'grid-rows-[1fr] opacity-100' : 'pointer-events-none grid-rows-[0fr] border-transparent opacity-0 shadow-none'
          }`}
        >
          <nav className="overflow-hidden">
            <ul className="flex flex-col gap-1 p-3">
              {NAV_ITEMS.map((item, i) => (
                <li
                  key={item.to}
                  className={`transition-all duration-300 ease-out ${
                    open ? 'translate-y-0 opacity-100' : '-translate-y-2 opacity-0'
                  }`}
                  style={{ transitionDelay: open ? `${80 + i * 45}ms` : '0ms' }}
                >
                  <NavLink to={item.to} end className={mobileLinkClass}>
                    {item.icon}
                    {item.label}
                  </NavLink>
                </li>
              ))}
            </ul>
          </nav>
        </div>
      </header>

      <div
        aria-hidden="true"
        onClick={() => setOpen(false)}
        className={`fixed inset-0 z-10 bg-ink/20 backdrop-blur-[2px] transition-opacity duration-300 md:hidden ${
          open ? 'opacity-100' : 'pointer-events-none opacity-0'
        }`}
      />

      <main className="mx-auto max-w-4xl px-4">
        <Outlet />
      </main>
    </div>
  )
}
