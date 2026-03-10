import { render, screen } from '@testing-library/react'
import { TextInput } from './TextInput'

describe('TextInput', () => {
  it('preserves input props and merges classes', () => {
    render(<TextInput aria-label="Employee number" className="custom-input" defaultValue="123456" />)

    expect(screen.getByRole('textbox', { name: 'Employee number' })).toHaveClass('field-shell', 'custom-input')
    expect(screen.getByDisplayValue('123456')).toBeInTheDocument()
  })
})
