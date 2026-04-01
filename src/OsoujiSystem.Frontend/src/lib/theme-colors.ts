export const colorSchemeNames = ['teal', 'blue', 'violet', 'emerald', 'amber', 'rose'] as const

export type ColorSchemeName = (typeof colorSchemeNames)[number]

export interface ColorScale {
  readonly 50: string
  readonly 100: string
  readonly 200: string
  readonly 300: string
  readonly 400: string
  readonly 500: string
  readonly 600: string
  readonly 700: string
  readonly 800: string
  readonly 900: string
  readonly 950: string
}

const teal: ColorScale = {
  50: '#f0fdfa',
  100: '#ccfbf1',
  200: '#99f6e4',
  300: '#5eead4',
  400: '#2dd4bf',
  500: '#14b8a6',
  600: '#0d9488',
  700: '#0f766e',
  800: '#115e59',
  900: '#134e4a',
  950: '#042f2e',
}

const blue: ColorScale = {
  50: '#eff6ff',
  100: '#dbeafe',
  200: '#bfdbfe',
  300: '#93c5fd',
  400: '#60a5fa',
  500: '#3b82f6',
  600: '#2563eb',
  700: '#1d4ed8',
  800: '#1e40af',
  900: '#1e3a8a',
  950: '#172554',
}

const violet: ColorScale = {
  50: '#f5f3ff',
  100: '#ede9fe',
  200: '#ddd6fe',
  300: '#c4b5fd',
  400: '#a78bfa',
  500: '#8b5cf6',
  600: '#7c3aed',
  700: '#6d28d9',
  800: '#5b21b6',
  900: '#4c1d95',
  950: '#2e1065',
}

const emerald: ColorScale = {
  50: '#ecfdf5',
  100: '#d1fae5',
  200: '#a7f3d0',
  300: '#6ee7b7',
  400: '#34d399',
  500: '#10b981',
  600: '#059669',
  700: '#047857',
  800: '#065f46',
  900: '#064e3b',
  950: '#022c22',
}

const amber: ColorScale = {
  50: '#fffbeb',
  100: '#fef3c7',
  200: '#fde68a',
  300: '#fcd34d',
  400: '#fbbf24',
  500: '#f59e0b',
  600: '#d97706',
  700: '#b45309',
  800: '#92400e',
  900: '#78350f',
  950: '#451a03',
}

const rose: ColorScale = {
  50: '#fff1f2',
  100: '#ffe4e6',
  200: '#fecdd3',
  300: '#fda4af',
  400: '#fb7185',
  500: '#f43f5e',
  600: '#e11d48',
  700: '#be123c',
  800: '#9f1239',
  900: '#881337',
  950: '#4c0519',
}

export const colorSchemes: Record<ColorSchemeName, ColorScale> = {
  teal,
  blue,
  violet,
  emerald,
  amber,
  rose,
}

export const scaleSteps = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const

export type ScaleStep = (typeof scaleSteps)[number]

export function getCssVariables(scheme: ColorSchemeName): Record<string, string> {
  const scale = colorSchemes[scheme]
  const vars: Record<string, string> = {}
  for (const step of scaleSteps) {
    vars[`--color-primary-${step}`] = scale[step]
  }
  return vars
}
