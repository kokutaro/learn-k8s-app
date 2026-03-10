import { render, screen } from '@testing-library/react'
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
})
