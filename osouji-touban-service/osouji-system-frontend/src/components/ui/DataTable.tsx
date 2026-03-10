import { type ReactNode } from 'react'

export interface DataTableProps {
  headers: string[]
  children: ReactNode
}

export function DataTable({ headers, children }: DataTableProps) {
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
