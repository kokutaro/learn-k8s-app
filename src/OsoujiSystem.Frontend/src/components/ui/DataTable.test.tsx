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

  it('applies the provided minTableWidthClassName instead of the default', () => {
    render(
      <DataTable headers={['Name', 'Status']} minTableWidthClassName="min-w-full table-fixed">
        <tr>
          <td>Alice</td>
          <td>Active</td>
        </tr>
      </DataTable>,
    )

    const table = screen.getByRole('table')
    expect(table.className).toContain('min-w-full')
    expect(table.className).toContain('table-fixed')
    expect(table.className).not.toContain('min-w-[44rem]')
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

  it('supports sticky headers and custom scroll container classes', () => {
    render(
      <DataTable
        headers={['Name', 'Status']}
        stickyHeader
        testId="custom-table-scroll"
        containerClassName="lg:max-h-80 lg:overflow-y-auto"
      >
        <tr>
          <td>Alice</td>
          <td>Active</td>
        </tr>
      </DataTable>,
    )

    const scrollContainer = screen.getByTestId('custom-table-scroll')
    expect(scrollContainer.className).toContain('lg:max-h-80')
    expect(scrollContainer.className).toContain('lg:overflow-y-auto')

    const stickyHeader = screen.getByRole('columnheader', { name: 'Name' })
    expect(stickyHeader.className).toContain('sticky')
    expect(stickyHeader.className).toContain('top-0')
    expect(stickyHeader.className).toContain('z-10')
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
