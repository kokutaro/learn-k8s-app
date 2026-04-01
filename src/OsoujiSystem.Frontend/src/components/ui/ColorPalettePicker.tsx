import { type ColorSchemeName, colorSchemeNames, colorSchemes } from '../../lib/theme-colors'
import { useTheme } from '../ThemeProvider'

const schemeLabels: Record<ColorSchemeName, string> = {
  teal: 'ティール',
  blue: 'ブルー',
  violet: 'バイオレット',
  emerald: 'エメラルド',
  amber: 'アンバー',
  rose: 'ローズ',
}

export function ColorPalettePicker() {
  const { colorScheme, setColorScheme } = useTheme()

  return (
    <fieldset>
      <legend className="text-xs font-semibold uppercase tracking-[0.18em] text-[var(--color-text-secondary)]">
        カラーパレット
      </legend>
      <div className="mt-3 flex flex-wrap gap-2" role="radiogroup" aria-label="カラーパレット選択">
        {colorSchemeNames.map((name) => {
          const isSelected = name === colorScheme
          const previewColor = colorSchemes[name][500]
          return (
            <button
              key={name}
              type="button"
              role="radio"
              aria-checked={isSelected}
              aria-label={schemeLabels[name]}
              onClick={() => setColorScheme(name)}
              className={`h-8 w-8 rounded-full transition-all ${isSelected ? 'ring-2 ring-offset-2 ring-[var(--color-primary-500)]' : 'hover:scale-110'}`}
              style={{ backgroundColor: previewColor }}
            />
          )
        })}
      </div>
    </fieldset>
  )
}
