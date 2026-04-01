import { render, screen } from '@testing-library/react'
import { StatusBadge } from './StatusBadge'

describe('StatusBadge', () => {
  it('applies the requested tone classes', () => {
    render(<StatusBadge label="Published" tone="positive" />)

    expect(screen.getByText('Published')).toHaveClass('chip', 'border-[var(--color-primary-200)]', 'bg-[var(--color-primary-50)]', 'text-[var(--color-primary-800)]')
  })
})
