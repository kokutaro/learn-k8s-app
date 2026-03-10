import { render, screen } from '@testing-library/react'
import { EmptyState } from './EmptyState'

describe('EmptyState', () => {
  it('renders title and message', () => {
    render(<EmptyState title="No data" message="Create a record to get started." />)

    expect(screen.getByRole('heading', { level: 3, name: 'No data' })).toBeInTheDocument()
    expect(screen.getByText('Create a record to get started.')).toBeInTheDocument()
  })
})
