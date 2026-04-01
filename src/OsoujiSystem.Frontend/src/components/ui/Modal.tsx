import { type ReactNode } from 'react'
import { Button } from './Button'

export interface ModalProps {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
}

export function Modal({ open, title, description, onClose, children }: ModalProps) {
  if (!open) {
    return null
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/25 dark:bg-slate-950/50 px-4 py-8 backdrop-blur-md">
      <div className="glass-panel max-h-full w-full max-w-2xl overflow-auto rounded-4xl p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-2xl font-bold text-[var(--color-text)]">{title}</h3>
            {description ? <p className="mt-2 text-sm text-[var(--color-text-secondary)]">{description}</p> : null}
          </div>
          <Button tone="ghost" onClick={onClose}>閉じる</Button>
        </div>
        <div className="mt-6">{children}</div>
      </div>
    </div>
  )
}
