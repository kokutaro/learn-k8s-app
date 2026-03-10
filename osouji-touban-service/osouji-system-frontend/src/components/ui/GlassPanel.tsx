import { type ReactNode } from 'react'
import { joinClassNames } from './utils'

export interface GlassPanelProps {
  className?: string
  children: ReactNode
}

export function GlassPanel({ className, children }: GlassPanelProps) {
  return <section className={joinClassNames('glass-panel rounded-[2rem] p-5', className)}>{children}</section>
}
