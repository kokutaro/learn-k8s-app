import { render, screen } from '@testing-library/react'
import { SelectInput } from './SelectInput'

describe('SelectInput', () => {
  it('renders options and merges classes', () => {
    render(
      <SelectInput aria-label="Status" className="custom-select" defaultValue="active">
        <option value="active">Active</option>
        <option value="inactive">Inactive</option>
      </SelectInput>,
    )

    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveClass('field-shell', 'custom-select')
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveValue('active')
  })
})
