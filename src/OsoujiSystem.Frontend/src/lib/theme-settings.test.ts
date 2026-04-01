import { afterEach, describe, expect, it, vi } from 'vitest'
import {
    THEME_SETTINGS_KEY,
    applyColorSchemeToRoot,
    applyDarkClassToRoot,
    defaultThemeSettings,
    loadThemeSettings,
    resolveIsDark,
    saveThemeSettings,
    themeSettingsSchema,
} from './theme-settings'

describe('themeSettingsSchema', () => {
  it('accepts valid settings', () => {
    const result = themeSettingsSchema.safeParse({ colorScheme: 'teal', darkMode: 'system' })
    expect(result.success).toBe(true)
  })

  it('rejects invalid color scheme', () => {
    const result = themeSettingsSchema.safeParse({ colorScheme: 'pink', darkMode: 'light' })
    expect(result.success).toBe(false)
  })

  it('rejects invalid dark mode', () => {
    const result = themeSettingsSchema.safeParse({ colorScheme: 'teal', darkMode: 'auto' })
    expect(result.success).toBe(false)
  })

  it.each(['teal', 'blue', 'violet', 'emerald', 'amber', 'rose'] as const)(
    'accepts color scheme: %s',
    (scheme) => {
      const result = themeSettingsSchema.safeParse({ colorScheme: scheme, darkMode: 'light' })
      expect(result.success).toBe(true)
    },
  )

  it.each(['light', 'dark', 'system'] as const)(
    'accepts dark mode: %s',
    (mode) => {
      const result = themeSettingsSchema.safeParse({ colorScheme: 'teal', darkMode: mode })
      expect(result.success).toBe(true)
    },
  )
})

describe('loadThemeSettings', () => {
  afterEach(() => {
    window.localStorage.clear()
  })

  it('returns default settings when localStorage is empty', () => {
    expect(loadThemeSettings()).toEqual(defaultThemeSettings)
  })

  it('returns saved settings from localStorage', () => {
    const settings = { colorScheme: 'blue', darkMode: 'dark' }
    window.localStorage.setItem(THEME_SETTINGS_KEY, JSON.stringify(settings))
    expect(loadThemeSettings()).toEqual(settings)
  })

  it('returns default settings when localStorage contains invalid JSON', () => {
    window.localStorage.setItem(THEME_SETTINGS_KEY, 'not-json')
    expect(loadThemeSettings()).toEqual(defaultThemeSettings)
  })

  it('returns default settings when localStorage contains invalid schema', () => {
    window.localStorage.setItem(THEME_SETTINGS_KEY, JSON.stringify({ colorScheme: 'pink' }))
    expect(loadThemeSettings()).toEqual(defaultThemeSettings)
  })
})

describe('saveThemeSettings', () => {
  afterEach(() => {
    window.localStorage.clear()
  })

  it('saves settings to localStorage', () => {
    const settings = { colorScheme: 'violet' as const, darkMode: 'dark' as const }
    saveThemeSettings(settings)
    const raw = window.localStorage.getItem(THEME_SETTINGS_KEY)
    expect(JSON.parse(raw!)).toEqual(settings)
  })
})

describe('resolveIsDark', () => {
  it('returns false for light mode', () => {
    expect(resolveIsDark('light')).toBe(false)
  })

  it('returns true for dark mode', () => {
    expect(resolveIsDark('dark')).toBe(true)
  })

  it('returns system preference for system mode', () => {
    const matchMedia = vi.fn().mockReturnValue({ matches: true })
    vi.stubGlobal('matchMedia', matchMedia)
    expect(resolveIsDark('system')).toBe(true)
    expect(matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)')
  })
})

describe('applyColorSchemeToRoot', () => {
  it('sets CSS custom properties on document root', () => {
    applyColorSchemeToRoot('teal')
    const root = document.documentElement
    expect(root.style.getPropertyValue('--color-primary-700')).toBe('#0f766e')
    expect(root.style.getPropertyValue('--color-primary-50')).toBe('#f0fdfa')
  })

  it('overrides previous scheme', () => {
    applyColorSchemeToRoot('teal')
    applyColorSchemeToRoot('blue')
    const root = document.documentElement
    expect(root.style.getPropertyValue('--color-primary-700')).toBe('#1d4ed8')
  })
})

describe('applyDarkClassToRoot', () => {
  afterEach(() => {
    document.documentElement.classList.remove('dark')
  })

  it('adds dark class when isDark is true', () => {
    applyDarkClassToRoot(true)
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('removes dark class when isDark is false', () => {
    document.documentElement.classList.add('dark')
    applyDarkClassToRoot(false)
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })
})
