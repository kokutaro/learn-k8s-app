import { z } from 'zod'
import { type ColorSchemeName, colorSchemeNames, getCssVariables } from './theme-colors'

export const darkModeValues = ['light', 'dark', 'system'] as const

export type DarkMode = (typeof darkModeValues)[number]

export const themeSettingsSchema = z.object({
  colorScheme: z.enum(colorSchemeNames),
  darkMode: z.enum(darkModeValues),
})

export type ThemeSettings = z.infer<typeof themeSettingsSchema>

export const THEME_SETTINGS_KEY = 'osouji.theme.v1'

export const defaultThemeSettings: ThemeSettings = {
  colorScheme: 'teal',
  darkMode: 'system',
}

export function loadThemeSettings(): ThemeSettings {
  if (typeof window === 'undefined') {
    return defaultThemeSettings
  }

  const raw = window.localStorage.getItem(THEME_SETTINGS_KEY)
  if (!raw) {
    return defaultThemeSettings
  }

  let json: unknown
  try {
    json = JSON.parse(raw)
  } catch {
    return defaultThemeSettings
  }

  const parsed = themeSettingsSchema.safeParse(json)
  return parsed.success ? parsed.data : defaultThemeSettings
}

export function saveThemeSettings(settings: ThemeSettings) {
  if (typeof window === 'undefined') {
    return
  }

  window.localStorage.setItem(THEME_SETTINGS_KEY, JSON.stringify(settings))
}

export function resolveIsDark(darkMode: DarkMode): boolean {
  if (darkMode === 'light') return false
  if (darkMode === 'dark') return true
  return window.matchMedia('(prefers-color-scheme: dark)').matches
}

export function applyColorSchemeToRoot(scheme: ColorSchemeName) {
  const vars = getCssVariables(scheme)
  const root = document.documentElement
  for (const [key, value] of Object.entries(vars)) {
    root.style.setProperty(key, value)
  }
}

export function applyDarkClassToRoot(isDark: boolean) {
  document.documentElement.classList.toggle('dark', isDark)
}
