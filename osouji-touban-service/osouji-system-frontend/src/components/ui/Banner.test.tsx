import { render, screen } from '@testing-library/react'
import { Banner } from './Banner'

describe('Banner', () => {
  it('renders the message with success styling', () => {
    render(<Banner kind="success" message="Saved successfully" />)

    expect(screen.getByText('Saved successfully')).toHaveClass('border-teal-200', 'bg-teal-50/90', 'text-teal-800')
  })
})
