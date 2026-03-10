import { render, screen } from '@testing-library/react'
import { Field } from './Field'

describe('Field', () => {
  it('renders a label wrapper and child content', () => {
    render(
      <Field label="Name">
        <input aria-label="Name input" />
      </Field>,
    )

    expect(screen.getByText('Name')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Name input' })).toBeInTheDocument()
  })
})
