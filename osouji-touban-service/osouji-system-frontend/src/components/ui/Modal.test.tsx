import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { vi } from 'vitest'
import { Modal } from './Modal'

describe('Modal', () => {
  it('does not render when closed', () => {
    render(<Modal open={false} title="Edit" onClose={() => {}}>Content</Modal>)

    expect(screen.queryByRole('heading', { level: 3, name: 'Edit' })).not.toBeInTheDocument()
  })

  it('renders content and closes via button when open', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()

    render(
      <Modal open title="Edit" description="Update the current item" onClose={onClose}>
        <p>Modal body</p>
      </Modal>,
    )

    expect(screen.getByRole('heading', { level: 3, name: 'Edit' })).toBeInTheDocument()
    expect(screen.getByText('Update the current item')).toBeInTheDocument()
    expect(screen.getByText('Modal body')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '閉じる' }))
    expect(onClose).toHaveBeenCalledOnce()
  })
})
