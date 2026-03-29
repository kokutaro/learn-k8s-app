import { render } from '@testing-library/react'
import { StackedFieldRow } from './StackedFieldRow'

describe('StackedFieldRow', () => {
  it('renders children in the shared grid layout', () => {
    const { container } = render(
      <StackedFieldRow>
        <div>Left</div>
        <div>Right</div>
      </StackedFieldRow>,
    )

    expect(container.firstChild).toHaveClass('grid', 'gap-4', 'md:grid-cols-2')
    expect(container).toHaveTextContent('Left')
    expect(container).toHaveTextContent('Right')
  })
})
