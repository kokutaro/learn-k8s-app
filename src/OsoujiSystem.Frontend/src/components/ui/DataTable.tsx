import { type ReactNode } from 'react'

export interface DataTableProps {
  headers: string[]
  children: ReactNode
  columnClassNames?: string[]
  minTableWidthClassName?: string
}

export function DataTable({ headers, children, columnClassNames, minTableWidthClassName = 'min-w-[44rem]' }: DataTableProps) {
  return (
    <div data-testid="data-table-scroll" className="min-w-0 max-w-full overflow-x-auto overscroll-x-contain rounded-3xl border border-[var(--glass-border)]">
      <table className={`w-full ${minTableWidthClassName} divide-y divide-[var(--glass-border)] text-left`}>
        <thead className="bg-[var(--color-surface)]">
          <tr>
            {headers.map((header, index) => (
              <th
                key={`${header}-${index}`}
                className={`px-4 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-[var(--color-text-secondary)] ${columnClassNames?.[index] ?? ''}`.trim()}
              >
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--glass-border)] bg-[var(--color-surface)]/60">{children}</tbody>
      </table>
    </div>
  )
}
