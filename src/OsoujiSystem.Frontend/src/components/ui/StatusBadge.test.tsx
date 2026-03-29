import { render, screen } from '@testing-library/react'
import { StatusBadge } from './StatusBadge'

describe('StatusBadge', () => {
  it('applies the requested tone classes', () => {
    render(<StatusBadge label="Published" tone="positive" />)

    expect(screen.getByText('Published')).toHaveClass('chip', 'border-teal-200', 'bg-teal-50', 'text-teal-700')
  })
})
