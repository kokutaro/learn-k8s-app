import { joinClassNames } from './utils'

export interface StatusBadgeProps {
  label: string
  tone?: 'default' | 'positive' | 'warning' | 'muted'
}

export function StatusBadge({ label, tone = 'default' }: StatusBadgeProps) {
  return (
    <span
      className={joinClassNames(
        'chip',
        tone === 'positive' && 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)] text-[var(--color-primary-800)] dark:border-[var(--color-primary-800)] dark:bg-[var(--color-primary-950)]/60 dark:text-[var(--color-primary-200)]',
        tone === 'warning' && 'border-amber-200 bg-amber-50 text-amber-700 dark:border-amber-800 dark:bg-amber-950/60 dark:text-amber-200',
        tone === 'muted' && 'border-slate-200 bg-slate-100 text-slate-500 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-400',
        tone === 'default' && 'border-[var(--glass-border)] bg-[var(--color-surface)] text-[var(--color-text-secondary)]',
      )}
    >
      {label}
    </span>
  )
}
