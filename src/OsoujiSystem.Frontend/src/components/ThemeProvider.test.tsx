import { act, renderHook } from '@testing-library/react'
import { type ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { THEME_SETTINGS_KEY, defaultThemeSettings } from '../lib/theme-settings'
import { ThemeProvider, useTheme } from './ThemeProvider'

function wrapper({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>
}

describe('ThemeProvider', () => {
  afterEach(() => {
    window.localStorage.clear()
    document.documentElement.classList.remove('dark')
    document.documentElement.removeAttribute('style')
  })

  it('provides default theme values', () => {
    const { result } = renderHook(() => useTheme(), { wrapper })
    expect(result.current.colorScheme).toBe(defaultThemeSettings.colorScheme)
    expect(result.current.darkMode).toBe(defaultThemeSettings.darkMode)
  })

  it('loads settings from localStorage on mount', () => {
    window.localStorage.setItem(
      THEME_SETTINGS_KEY,
      JSON.stringify({ colorScheme: 'blue', darkMode: 'dark' }),
    )
    const { result } = renderHook(() => useTheme(), { wrapper })
    expect(result.current.colorScheme).toBe('blue')
    expect(result.current.darkMode).toBe('dark')
  })

  it('applies CSS variables for color scheme on mount', () => {
    const { result } = renderHook(() => useTheme(), { wrapper })
    expect(result.current.colorScheme).toBe('teal')
    expect(document.documentElement.style.getPropertyValue('--color-primary-700')).toBe('#0f766e')
  })

  it('updates CSS variables when color scheme changes', () => {
    const { result } = renderHook(() => useTheme(), { wrapper })
    act(() => {
      result.current.setColorScheme('violet')
    })
    expect(result.current.colorScheme).toBe('violet')
    expect(document.documentElement.style.getPropertyValue('--color-primary-700')).toBe('#6d28d9')
  })

  it('persists settings to localStorage on change', () => {
    const { result } = renderHook(() => useTheme(), { wrapper })
    act(() => {
      result.current.setColorScheme('amber')
    })
    const raw = JSON.parse(window.localStorage.getItem(THEME_SETTINGS_KEY)!)
    expect(raw.colorScheme).toBe('amber')
  })

  it('applies dark class when dark mode is set to dark', () => {
    const { result } = renderHook(() => useTheme(), { wrapper })
    act(() => {
      result.current.setDarkMode('dark')
    })
    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(result.current.isDark).toBe(true)
  })

  it('removes dark class when dark mode is set to light', () => {
    document.documentElement.classList.add('dark')
    const { result } = renderHook(() => useTheme(), { wrapper })
    act(() => {
      result.current.setDarkMode('light')
    })
    expect(document.documentElement.classList.contains('dark')).toBe(false)
    expect(result.current.isDark).toBe(false)
  })

  it('responds to system preference in system mode', () => {
    const matchMedia = vi.fn().mockReturnValue({
      matches: true,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    })
    vi.stubGlobal('matchMedia', matchMedia)

    const { result } = renderHook(() => useTheme(), { wrapper })
    act(() => {
      result.current.setDarkMode('system')
    })
    expect(result.current.isDark).toBe(true)
  })
})

describe('useTheme outside provider', () => {
  it('throws when used without ThemeProvider', () => {
    expect(() => {
      renderHook(() => useTheme())
    }).toThrow('useTheme must be used within a ThemeProvider')
  })
})
