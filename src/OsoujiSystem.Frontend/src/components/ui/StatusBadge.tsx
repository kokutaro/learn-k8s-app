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
        tone === 'positive' && 'border-teal-200 bg-teal-50 text-teal-700',
        tone === 'warning' && 'border-amber-200 bg-amber-50 text-amber-700',
        tone === 'muted' && 'border-slate-200 bg-slate-100 text-slate-500',
        tone === 'default' && 'border-slate-200 bg-white/80 text-slate-700',
      )}
    >
      {label}
    </span>
  )
}
