import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { Button } from './Button'

describe('Button', () => {
  it('fires click handlers and uses the requested tone', async () => {
    const user = userEvent.setup()
    const onClick = vi.fn()

    render(<Button tone="danger" onClick={onClick}>Delete</Button>)
    await user.click(screen.getByRole('button', { name: 'Delete' }))

    expect(onClick).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Delete' })).toHaveClass('bg-rose-600', 'text-white')
  })
})
