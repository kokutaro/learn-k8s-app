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
        tone === 'primary' && 'bg-teal-700 text-white shadow-lg shadow-teal-700/20 hover:bg-teal-800',
        tone === 'secondary' && 'bg-white/85 text-slate-900 ring-1 ring-white/80 hover:bg-white',
        tone === 'danger' && 'bg-rose-600 text-white shadow-lg shadow-rose-600/20 hover:bg-rose-700',
        tone === 'ghost' && 'bg-transparent text-slate-700 hover:bg-white/60',
      )}
    >
      {children}
    </button>
  )
}
