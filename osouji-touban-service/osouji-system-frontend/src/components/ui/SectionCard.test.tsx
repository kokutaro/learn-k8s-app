import { render, screen } from '@testing-library/react'
import { SectionCard } from './SectionCard'

describe('SectionCard', () => {
  it('renders title, action, and content inside a panel', () => {
    render(
      <SectionCard title="Facilities" action={<button>Open</button>}>
        <p>Section content</p>
      </SectionCard>,
    )

    expect(screen.getByRole('heading', { level: 2, name: 'Facilities' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument()
    expect(screen.getByText('Section content')).toBeInTheDocument()
  })
})
