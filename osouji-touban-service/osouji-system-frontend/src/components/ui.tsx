import { type ReactNode } from 'react'

function joinClassNames(...values: Array<string | false | null | undefined>) {
  return values.filter(Boolean).join(' ')
}

export function PageHeader({
  title,
  description,
  action,
}: {
  title: string
  description: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
      <div>
        <p className="text-sm font-semibold uppercase tracking-[0.24em] text-teal-700/70">Osouji System</p>
        <h1 className="mt-2 text-3xl font-bold text-slate-900">{title}</h1>
        <p className="mt-2 max-w-3xl text-sm text-slate-600">{description}</p>
      </div>
      {action}
    </div>
  )
}

export function GlassPanel({ className, children }: { className?: string; children: ReactNode }) {
  return <section className={joinClassNames('glass-panel rounded-[2rem] p-5', className)}>{children}</section>
}

export function SectionCard({
  title,
  action,
  children,
}: {
  title: string
  action?: ReactNode
  children: ReactNode
}) {
  return (
    <GlassPanel className="space-y-4">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-xl font-bold text-slate-900">{title}</h2>
        {action}
      </div>
      {children}
    </GlassPanel>
  )
}

export function StatusBadge({
  label,
  tone = 'default',
}: {
  label: string
  tone?: 'default' | 'positive' | 'warning' | 'muted'
}) {
  return (
    <span
      className={joinClassNames(
        'chip',
        tone === 'positive' && 'border-teal-200 bg-teal-50 text-teal-700',
        tone === 'warning' && 'border-amber-200 bg-amber-50 text-amber-700',
        tone === 'muted' && 'border-slate-200 bg-slate-100 text-slate-500',
        tone === 'default' && 'border-slate-200 bg-white/80 text-slate-700',
      )}
    >
      {label}
    </span>
  )
}

export function Banner({
  kind,
  message,
}: {
  kind: 'success' | 'error'
  message: string
}) {
  return (
    <div
      className={joinClassNames(
        'rounded-2xl border px-4 py-3 text-sm font-medium',
        kind === 'success' && 'border-teal-200 bg-teal-50/90 text-teal-800',
        kind === 'error' && 'border-rose-200 bg-rose-50/90 text-rose-800',
      )}
    >
      {message}
    </div>
  )
}

export function Button({
  children,
  tone = 'primary',
  onClick,
  type = 'button',
  disabled,
}: {
  children: ReactNode
  tone?: 'primary' | 'secondary' | 'danger' | 'ghost'
  onClick?: () => void
  type?: 'button' | 'submit'
  disabled?: boolean
}) {
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

export function Field({
  label,
  children,
}: {
  label: string
  children: ReactNode
}) {
  return (
    <label className="flex flex-col gap-2 text-sm font-medium text-slate-700">
      <span>{label}</span>
      {children}
    </label>
  )
}

export function TextInput(props: React.InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={joinClassNames('field-shell', props.className)} />
}

export function SelectInput(props: React.SelectHTMLAttributes<HTMLSelectElement>) {
  return <select {...props} className={joinClassNames('field-shell', props.className)} />
}

export function TextArea(props: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea {...props} className={joinClassNames('field-shell min-h-28', props.className)} />
}

export function Modal({
  open,
  title,
  description,
  onClose,
  children,
}: {
  open: boolean
  title: string
  description?: string
  onClose: () => void
  children: ReactNode
}) {
  if (!open) {
    return null
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/25 px-4 py-8 backdrop-blur-md">
      <div className="glass-panel max-h-full w-full max-w-2xl overflow-auto rounded-[2rem] p-6">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-2xl font-bold text-slate-900">{title}</h3>
            {description ? <p className="mt-2 text-sm text-slate-600">{description}</p> : null}
          </div>
          <Button tone="ghost" onClick={onClose}>閉じる</Button>
        </div>
        <div className="mt-6">{children}</div>
      </div>
    </div>
  )
}

export function DataTable({
  headers,
  children,
}: {
  headers: string[]
  children: ReactNode
}) {
  return (
    <div className="overflow-hidden rounded-[1.5rem] border border-white/60">
      <table className="min-w-full divide-y divide-white/60 text-left">
        <thead className="bg-white/65">
          <tr>
            {headers.map((header) => (
              <th key={header} className="px-4 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-white/60 bg-white/45">{children}</tbody>
      </table>
    </div>
  )
}

export function EmptyState({
  title,
  message,
}: {
  title: string
  message: string
}) {
  return (
    <div className="rounded-[1.5rem] border border-dashed border-white/70 bg-white/50 px-6 py-12 text-center">
      <h3 className="text-lg font-bold text-slate-900">{title}</h3>
      <p className="mt-2 text-sm text-slate-600">{message}</p>
    </div>
  )
}

export function MetricChip({
  label,
  value,
}: {
  label: string
  value: string | number
}) {
  return (
    <div className="rounded-[1.5rem] border border-white/70 bg-white/65 px-4 py-3">
      <div className="text-xs uppercase tracking-[0.18em] text-slate-500">{label}</div>
      <div className="mt-2 text-xl font-bold text-slate-900">{value}</div>
    </div>
  )
}

export function StackedFieldRow({ children }: { children: ReactNode }) {
  return <div className="grid gap-4 md:grid-cols-2">{children}</div>
}
