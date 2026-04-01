import { joinClassNames } from './utils'

export interface BannerProps {
  kind: 'success' | 'error'
  message: string
}

export function Banner({ kind, message }: BannerProps) {
  return (
    <div
      className={joinClassNames(
        'rounded-2xl border px-4 py-3 text-sm font-medium',
        kind === 'success' && 'border-[var(--color-primary-200)] bg-[var(--color-primary-50)]/90 text-[var(--color-primary-800)] dark:border-[var(--color-primary-800)] dark:bg-[var(--color-primary-950)]/60 dark:text-[var(--color-primary-200)]',
        kind === 'error' && 'border-rose-200 bg-rose-50/90 text-rose-800 dark:border-rose-800 dark:bg-rose-950/60 dark:text-rose-200',
      )}
    >
      {message}
    </div>
  )
}
