import { type ReactNode } from 'react'

export interface FieldProps {
  label: string
  children: ReactNode
}

export function Field({ label, children }: FieldProps) {
  return (
    <label className="flex flex-col gap-2 text-sm font-medium text-[var(--color-text-secondary)]">
      <span>{label}</span>
      {children}
    </label>
  )
}
