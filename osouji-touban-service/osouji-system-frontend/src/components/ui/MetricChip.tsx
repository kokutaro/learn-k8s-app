export interface MetricChipProps {
  label: string
  value: string | number
}

export function MetricChip({ label, value }: MetricChipProps) {
  return (
    <div className="rounded-[1.5rem] border border-white/70 bg-white/65 px-4 py-3">
      <div className="text-xs uppercase tracking-[0.18em] text-slate-500">{label}</div>
      <div className="mt-2 text-xl font-bold text-slate-900">{value}</div>
    </div>
  )
}
