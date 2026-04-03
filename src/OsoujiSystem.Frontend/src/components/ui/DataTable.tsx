import { type ReactNode } from 'react'
import { joinClassNames } from './utils'

export interface DataTableProps {
  headers: string[]
  children: ReactNode
  columnClassNames?: string[]
  minTableWidthClassName?: string
  containerClassName?: string
  stickyHeader?: boolean
  testId?: string
}

export function DataTable({
  headers,
  children,
  columnClassNames,
  minTableWidthClassName = 'min-w-[44rem]',
  containerClassName,
  stickyHeader = false,
  testId = 'data-table-scroll',
}: DataTableProps) {
  return (
    <div
      data-testid={testId}
      className={joinClassNames(
        'min-w-0 max-w-full overflow-x-auto overscroll-x-contain rounded-3xl border border-[var(--glass-border)]',
        containerClassName,
      )}
    >
      <table className={`w-full ${minTableWidthClassName} divide-y divide-[var(--glass-border)] text-left`}>
        <thead className="bg-[var(--color-surface)]">
          <tr>
            {headers.map((header, index) => (
              <th
                key={`${header}-${index}`}
                className={joinClassNames(
                  'px-4 py-3 text-xs font-semibold uppercase tracking-[0.18em] text-[var(--color-text)]',
                  stickyHeader && 'sticky top-0 z-10 bg-[var(--color-surface)] shadow-[inset_0_-1px_0_0_var(--glass-border)]',
                  columnClassNames?.[index],
                )}
              >
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-[var(--glass-border)] bg-[var(--color-surface)] text-[var(--color-text)]">{children}</tbody>
      </table>
    </div>
  )
}
