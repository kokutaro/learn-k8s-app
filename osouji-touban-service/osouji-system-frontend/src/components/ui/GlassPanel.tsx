import { type ReactNode } from 'react'
import { joinClassNames } from './utils'

export interface GlassPanelProps {
  className?: string
  children: ReactNode
}

export function GlassPanel({ className, children }: GlassPanelProps) {
  return <section className={joinClassNames('glass-panel min-w-0 rounded-4xl p-5', className)}>{children}</section>
}
