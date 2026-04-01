import { type ReactNode } from 'react'
import { GlassPanel } from './GlassPanel'

export interface SectionCardProps {
  title: string
  action?: ReactNode
  children: ReactNode
}

export function SectionCard({ title, action, children }: SectionCardProps) {
  return (
    <GlassPanel className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-xl font-bold text-[var(--color-text)]">{title}</h2>
        {action}
      </div>
      {children}
    </GlassPanel>
  )
}
