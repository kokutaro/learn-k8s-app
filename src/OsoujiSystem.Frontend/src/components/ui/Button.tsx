import { type ReactNode } from 'react'
import { joinClassNames } from './utils'

export interface ButtonProps {
  children: ReactNode
  tone?: 'primary' | 'secondary' | 'danger' | 'ghost'
  onClick?: () => void
  type?: 'button' | 'submit'
  disabled?: boolean
}

export function Button({
  children,
  tone = 'primary',
  onClick,
  type = 'button',
  disabled,
}: ButtonProps) {
  return (
    <button
      type={type}
      onClick={onClick}
      disabled={disabled}
      className={joinClassNames(
        'rounded-full px-5 py-3 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-50',
        tone === 'primary' && 'bg-[var(--color-primary-700)] text-white shadow-lg shadow-[var(--color-primary-700)]/20 hover:bg-[var(--color-primary-800)]',
        tone === 'secondary' && 'bg-[var(--color-surface)] text-[var(--color-text)] ring-1 ring-[var(--glass-border)] hover:bg-[var(--color-surface-hover)]',
        tone === 'danger' && 'bg-rose-600 text-white shadow-lg shadow-rose-600/20 hover:bg-rose-700',
        tone === 'ghost' && 'bg-transparent text-[var(--color-text-secondary)] hover:bg-[var(--color-surface)]',
      )}
    >
      {children}
    </button>
  )
}
