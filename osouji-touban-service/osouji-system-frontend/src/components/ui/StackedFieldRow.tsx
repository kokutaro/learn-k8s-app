import { type ReactNode } from 'react'

export interface StackedFieldRowProps {
  children: ReactNode
}

export function StackedFieldRow({ children }: StackedFieldRowProps) {
  return <div className="grid gap-4 md:grid-cols-2">{children}</div>
}
