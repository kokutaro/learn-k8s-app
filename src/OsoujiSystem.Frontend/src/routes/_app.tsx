import { Link, Outlet, createFileRoute } from '@tanstack/react-router'
import { ThemeSettingsPanel } from '../components/ThemeSettingsPanel'

const navItems = [
  { to: '/facilities', label: '施設管理' },
  { to: '/users', label: 'ユーザー管理' },
  { to: '/cleaning-areas', label: '掃除エリア' },
  { to: '/weekly-duty-plans', label: '清掃計画' },
  { to: '/dashboard', label: 'ダッシュボード' },
] as const

export const Route = createFileRoute('/_app')({
  component: AppLayout,
})

function AppLayout() {
  return (
    <div className="min-h-dvh px-4 py-4 lg:px-6">
      <div className="mx-auto grid min-h-[calc(100dvh-2rem)] max-w-400 gap-4 lg:grid-cols-[260px_1fr]">
        <aside className="glass-panel flex flex-col rounded-4xl p-5">
          <div className="rounded-[1.75rem] bg-[var(--color-primary-900)] px-5 py-6 text-white">
            <p className="text-xs uppercase tracking-[0.28em] text-[var(--color-primary-100)]/70">Cleaning Ops</p>
            <h1 className="mt-3 text-3xl font-bold">Osouji</h1>
            <p className="mt-3 text-sm text-[var(--color-primary-50)]/80">
              施設、ユーザー、掃除エリア、今週の計画までをひとつの運用面で管理します。
            </p>
          </div>
          <nav className="mt-6 space-y-2">
            {navItems.map((item) => (
              <Link
                key={item.to}
                to={item.to}
                className="block rounded-[1.35rem] px-4 py-3 text-sm font-semibold text-[var(--color-text-secondary)] transition hover:bg-[var(--color-surface-hover)] hover:text-[var(--color-text)]"
                activeProps={{
                  className: 'block rounded-[1.35rem] bg-[var(--color-surface-hover)] px-4 py-3 text-sm font-semibold text-[var(--color-text)] shadow',
                }}
              >
                {item.label}
              </Link>
            ))}
          </nav>
          <div className="mt-auto pt-4">
            <ThemeSettingsPanel />
          </div>
        </aside>
        <main className="min-w-0 space-y-6 pb-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
