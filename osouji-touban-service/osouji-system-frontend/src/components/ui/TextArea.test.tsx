import { render, screen } from '@testing-library/react'
import { TextArea } from './TextArea'

describe('TextArea', () => {
  it('renders a textarea with the shared shell classes', () => {
    render(<TextArea aria-label="Notes" className="custom-area" defaultValue="Pending" />)

    expect(screen.getByRole('textbox', { name: 'Notes' })).toHaveClass('field-shell', 'min-h-28', 'custom-area')
    expect(screen.getByDisplayValue('Pending')).toBeInTheDocument()
  })
})
