import { render, screen } from '@testing-library/react'
import { Banner } from './Banner'

describe('Banner', () => {
  it('renders the message with success styling', () => {
    render(<Banner kind="success" message="Saved successfully" />)

    expect(screen.getByText('Saved successfully')).toHaveClass('border-[var(--color-primary-200)]', 'bg-[var(--color-primary-50)]/90', 'text-[var(--color-primary-800)]')
  })
})
