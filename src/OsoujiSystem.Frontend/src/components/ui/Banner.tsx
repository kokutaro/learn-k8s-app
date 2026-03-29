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
        kind === 'success' && 'border-teal-200 bg-teal-50/90 text-teal-800',
        kind === 'error' && 'border-rose-200 bg-rose-50/90 text-rose-800',
      )}
    >
      {message}
    </div>
  )
}
