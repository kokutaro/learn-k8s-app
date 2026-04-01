import {
    type ReactNode,
    createContext,
    useCallback,
    useContext,
    useEffect,
    useLayoutEffect,
    useState,
} from 'react'
import type { ColorSchemeName } from '../lib/theme-colors'
import type { DarkMode } from '../lib/theme-settings'
import {
    applyColorSchemeToRoot,
    applyDarkClassToRoot,
    loadThemeSettings,
    saveThemeSettings,
} from '../lib/theme-settings'

export interface ThemeContextValue {
  readonly colorScheme: ColorSchemeName
  readonly darkMode: DarkMode
  readonly isDark: boolean
  readonly setColorScheme: (scheme: ColorSchemeName) => void
  readonly setDarkMode: (mode: DarkMode) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext)
  if (!ctx) {
    throw new Error('useTheme must be used within a ThemeProvider')
  }
  return ctx
}

export interface ThemeProviderProps {
  readonly children: ReactNode
}

export function ThemeProvider({ children }: ThemeProviderProps) {
  const [colorScheme, setColorSchemeState] = useState<ColorSchemeName>(
    () => loadThemeSettings().colorScheme,
  )
  const [darkMode, setDarkModeState] = useState<DarkMode>(
    () => loadThemeSettings().darkMode,
  )
  const [systemIsDark, setSystemIsDark] = useState(
    () => window.matchMedia('(prefers-color-scheme: dark)').matches,
  )

  const isDark = darkMode === 'dark' ? true : darkMode === 'light' ? false : systemIsDark

  const setColorScheme = useCallback((scheme: ColorSchemeName) => {
    setColorSchemeState(scheme)
  }, [])

  const setDarkMode = useCallback((mode: DarkMode) => {
    setDarkModeState(mode)
  }, [])

  // Apply color scheme CSS variables to :root
  useLayoutEffect(() => {
    applyColorSchemeToRoot(colorScheme)
  }, [colorScheme])

  // Apply dark class synchronously
  useLayoutEffect(() => {
    applyDarkClassToRoot(isDark)
  }, [isDark])

  // Listen for system color scheme changes
  useEffect(() => {
    const mql = window.matchMedia('(prefers-color-scheme: dark)')
    const handler = (e: MediaQueryListEvent) => setSystemIsDark(e.matches)
    mql.addEventListener('change', handler)
    return () => mql.removeEventListener('change', handler)
  }, [])

  // Persist settings whenever they change
  useEffect(() => {
    saveThemeSettings({ colorScheme, darkMode })
  }, [colorScheme, darkMode])

  const value: ThemeContextValue = {
    colorScheme,
    darkMode,
    isDark,
    setColorScheme,
    setDarkMode,
  }

  return <ThemeContext value={value}>{children}</ThemeContext>
}
