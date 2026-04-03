import { render, screen } from '@testing-library/react'
import { ScrollablePanel } from './ScrollablePanel'

describe('ScrollablePanel', () => {
  it('renders a fixed header area and a separately identifiable scroll body', () => {
    render(
      <ScrollablePanel
        header={<div>Filters</div>}
        bodyTestId="areas-scroll-body"
        className="lg:h-full"
        bodyClassName="lg:overflow-y-auto"
      >
        <button type="button">Area A</button>
      </ScrollablePanel>,
    )

    expect(screen.getByText('Filters')).toBeVisible()

    const body = screen.getByTestId('areas-scroll-body')
    expect(body.className).toContain('min-h-0')
    expect(body.className).toContain('lg:overflow-y-auto')
    expect(screen.getByRole('button', { name: 'Area A' })).toBeVisible()
  })
})