import { render } from '@testing-library/react'
import { GlassPanel } from './GlassPanel'

describe('GlassPanel', () => {
  it('renders children and merges classes', () => {
    const { container } = render(<GlassPanel className="extra-panel">Body</GlassPanel>)

    expect(container.querySelector('section')).toHaveClass('glass-panel', 'rounded-4xl', 'p-5', 'extra-panel')
    expect(container).toHaveTextContent('Body')
  })
})
