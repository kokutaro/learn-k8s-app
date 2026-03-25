import { render, screen } from '@testing-library/react'
import { vi } from 'vitest'
import { DataTable } from './DataTable'

describe('DataTable', () => {
  it('renders headers and row content', () => {
    render(
      <DataTable headers={['Name', 'Status']}>
        <tr>
          <td>Alice</td>
          <td>Active</td>
        </tr>
      </DataTable>,
    )

    expect(screen.getByRole('columnheader', { name: 'Name' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Status' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Alice' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'Active' })).toBeInTheDocument()
  })

  it('provides a horizontal scroll container and keeps a readable default width on mobile', () => {
    render(
      <DataTable headers={['Name', 'Status']}>
        <tr>
          <td>Alice</td>
          <td>Active</td>
        </tr>
      </DataTable>,
    )

    const scrollContainer = screen.getByTestId('data-table-scroll')
    expect(scrollContainer.className).toContain('overflow-x-auto')
    expect(scrollContainer.className).toContain('rounded-3xl')
    expect(scrollContainer.className).toContain('border')

    const table = screen.getByRole('table')
    expect(table.className).toContain('min-w-[44rem]')
  })

  it('applies per-column min width classes when provided', () => {
    render(
      <DataTable
        headers={['Name', 'Status']}
        columnClassNames={['min-w-[12rem]', 'min-w-[10rem]']}
      >
        <tr>
          <td>Alice</td>
          <td>Active</td>
        </tr>
      </DataTable>,
    )

    expect(screen.getByRole('columnheader', { name: 'Name' }).className).toContain('min-w-[12rem]')
    expect(screen.getByRole('columnheader', { name: 'Status' }).className).toContain('min-w-[10rem]')
  })

  it('renders duplicate headers without duplicate key warnings', () => {
    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    try {
      render(
        <DataTable headers={['Name', 'Name']}>
          <tr>
            <td>Alice</td>
            <td>Active</td>
          </tr>
        </DataTable>,
      )

      expect(screen.getAllByRole('columnheader', { name: 'Name' })).toHaveLength(2)

      const hasDuplicateKeyWarning = consoleErrorSpy.mock.calls.some((callArgs) =>
        callArgs.some(
          (arg) =>
            typeof arg === 'string'
            && arg.includes('Encountered two children with the same key'),
        ),
      )

      expect(hasDuplicateKeyWarning).toBe(false)
    } finally {
      consoleErrorSpy.mockRestore()
    }
  })
})
