import { render, screen } from '@testing-library/react'
import { MetricChip } from './MetricChip'

describe('MetricChip', () => {
  it('renders the label and numeric value', () => {
    render(<MetricChip label="Members" value={12} />)

    expect(screen.getByText('Members')).toBeInTheDocument()
    expect(screen.getByText('12')).toBeInTheDocument()
  })
})
