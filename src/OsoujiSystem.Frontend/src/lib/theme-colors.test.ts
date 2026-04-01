import { describe, expect, it } from 'vitest'
import {
  type ColorSchemeName,
  colorSchemeNames,
  colorSchemes,
  getCssVariables,
  scaleSteps,
} from './theme-colors'

describe('theme-colors', () => {
  it('exports exactly 6 color scheme names', () => {
    expect(colorSchemeNames).toHaveLength(6)
    expect(colorSchemeNames).toEqual(['teal', 'blue', 'violet', 'emerald', 'amber', 'rose'])
  })

  it('each scheme has all 11 scale steps', () => {
    for (const name of colorSchemeNames) {
      const scale = colorSchemes[name]
      for (const step of scaleSteps) {
        expect(scale[step]).toBeDefined()
        expect(scale[step]).toMatch(/^#[0-9a-f]{6}$/i)
      }
    }
  })

  it('each scheme has distinct values across steps', () => {
    for (const name of colorSchemeNames) {
      const scale = colorSchemes[name]
      const values = scaleSteps.map((s) => scale[s])
      const unique = new Set(values)
      expect(unique.size).toBe(scaleSteps.length)
    }
  })

  describe('getCssVariables', () => {
    it('returns CSS custom properties for a given scheme', () => {
      const vars = getCssVariables('teal')
      expect(Object.keys(vars)).toHaveLength(scaleSteps.length)
      expect(vars['--color-primary-700']).toBe('#0f766e')
      expect(vars['--color-primary-50']).toBe('#f0fdfa')
      expect(vars['--color-primary-950']).toBe('#042f2e')
    })

    it('returns different values for different schemes', () => {
      const tealVars = getCssVariables('teal')
      const blueVars = getCssVariables('blue')
      expect(tealVars['--color-primary-700']).not.toBe(blueVars['--color-primary-700'])
    })

    it.each(colorSchemeNames)('generates valid CSS variables for %s', (name: ColorSchemeName) => {
      const vars = getCssVariables(name)
      for (const [key, value] of Object.entries(vars)) {
        expect(key).toMatch(/^--color-primary-\d+$/)
        expect(value).toMatch(/^#[0-9a-f]{6}$/i)
      }
    })
  })
})
