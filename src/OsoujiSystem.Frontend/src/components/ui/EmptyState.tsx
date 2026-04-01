export interface EmptyStateProps {
  title: string
  message: string
}

export function EmptyState({ title, message }: EmptyStateProps) {
  return (
    <div className="rounded-3xl border border-dashed border-[var(--glass-border)] bg-[var(--color-surface)]/50 px-6 py-12 text-center">
      <h3 className="text-lg font-bold text-[var(--color-text)]">{title}</h3>
      <p className="mt-2 text-sm text-[var(--color-text-secondary)]">{message}</p>
    </div>
  )
}
