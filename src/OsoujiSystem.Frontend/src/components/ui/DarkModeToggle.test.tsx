import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { type ReactNode } from 'react'
import { describe, expect, it, afterEach } from 'vitest'
import { ThemeProvider } from '../ThemeProvider'
import { DarkModeToggle } from './DarkModeToggle'

function wrapper({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>
}

describe('DarkModeToggle', () => {
  afterEach(() => {
    window.localStorage.clear()
    document.documentElement.classList.remove('dark')
    document.documentElement.removeAttribute('style')
  })

  it('renders 3 mode buttons', () => {
    render(<DarkModeToggle />, { wrapper })
    const buttons = screen.getAllByRole('radio')
    expect(buttons).toHaveLength(3)
  })

  it('marks system as default selected', () => {
    render(<DarkModeToggle />, { wrapper })
    const systemButton = screen.getByRole('radio', { name: 'システム' })
    expect(systemButton).toHaveAttribute('aria-checked', 'true')
  })

  it('switches to dark mode on click', async () => {
    const user = userEvent.setup()
    render(<DarkModeToggle />, { wrapper })

    const darkButton = screen.getByRole('radio', { name: 'ダーク' })
    await user.click(darkButton)

    expect(darkButton).toHaveAttribute('aria-checked', 'true')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('switches to light mode on click', async () => {
    const user = userEvent.setup()
    render(<DarkModeToggle />, { wrapper })

    // First go dark
    await user.click(screen.getByRole('radio', { name: 'ダーク' }))
    expect(document.documentElement.classList.contains('dark')).toBe(true)

    // Then go light
    await user.click(screen.getByRole('radio', { name: 'ライト' }))
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('has accessible labels', () => {
    render(<DarkModeToggle />, { wrapper })
    expect(screen.getByRole('radio', { name: 'ライト' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'ダーク' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'システム' })).toBeInTheDocument()
  })
})
