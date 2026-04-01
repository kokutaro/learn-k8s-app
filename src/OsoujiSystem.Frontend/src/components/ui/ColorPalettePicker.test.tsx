import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { type ReactNode } from 'react'
import { describe, expect, it, afterEach } from 'vitest'
import { ThemeProvider } from '../ThemeProvider'
import { ColorPalettePicker } from './ColorPalettePicker'

function wrapper({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>
}

describe('ColorPalettePicker', () => {
  afterEach(() => {
    window.localStorage.clear()
    document.documentElement.removeAttribute('style')
  })

  it('renders 6 color swatch buttons', () => {
    render(<ColorPalettePicker />, { wrapper })
    const buttons = screen.getAllByRole('radio')
    expect(buttons).toHaveLength(6)
  })

  it('marks default scheme as selected', () => {
    render(<ColorPalettePicker />, { wrapper })
    const tealButton = screen.getByRole('radio', { name: 'ティール' })
    expect(tealButton).toHaveAttribute('aria-checked', 'true')
  })

  it('selects a different scheme on click', async () => {
    const user = userEvent.setup()
    render(<ColorPalettePicker />, { wrapper })

    const blueButton = screen.getByRole('radio', { name: 'ブルー' })
    await user.click(blueButton)

    expect(blueButton).toHaveAttribute('aria-checked', 'true')
    const tealButton = screen.getByRole('radio', { name: 'ティール' })
    expect(tealButton).toHaveAttribute('aria-checked', 'false')
  })

  it('has accessible labels for all schemes', () => {
    render(<ColorPalettePicker />, { wrapper })
    expect(screen.getByRole('radio', { name: 'ティール' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'ブルー' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'バイオレット' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'エメラルド' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'アンバー' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'ローズ' })).toBeInTheDocument()
  })
})
