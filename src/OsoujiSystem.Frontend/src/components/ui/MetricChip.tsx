export interface MetricChipProps {
  label: string
  value: string | number
}

export function MetricChip({ label, value }: MetricChipProps) {
  return (
    <div className="rounded-3xl border border-[var(--glass-border)] bg-[var(--color-surface)] px-4 py-3">
      <div className="text-xs uppercase tracking-[0.18em] text-[var(--color-text-secondary)]">{label}</div>
      <div className="mt-2 text-xl font-bold text-[var(--color-text)]">{value}</div>
    </div>
  )
}
