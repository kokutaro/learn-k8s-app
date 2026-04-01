import { type DarkMode, darkModeValues } from '../../lib/theme-settings'
import { useTheme } from '../ThemeProvider'
import { joinClassNames } from './utils'

const modeLabels: Record<DarkMode, { label: string; icon: string }> = {
  light: { label: 'ライト', icon: '☀️' },
  dark: { label: 'ダーク', icon: '🌙' },
  system: { label: 'システム', icon: '💻' },
}

export function DarkModeToggle() {
  const { darkMode, setDarkMode } = useTheme()

  return (
    <fieldset>
      <legend className="text-xs font-semibold uppercase tracking-[0.18em] text-[var(--color-text-secondary)]">
        表示モード
      </legend>
      <div className="mt-3 flex gap-1 rounded-2xl bg-[var(--color-surface)] p-1" role="radiogroup" aria-label="表示モード選択">
        {darkModeValues.map((mode) => {
          const isSelected = mode === darkMode
          const { label, icon } = modeLabels[mode]
          return (
            <button
              key={mode}
              type="button"
              role="radio"
              aria-checked={isSelected}
              aria-label={label}
              onClick={() => setDarkMode(mode)}
              className={joinClassNames(
                'flex-1 rounded-xl px-3 py-2 text-xs font-semibold transition',
                isSelected
                  ? 'bg-[var(--color-primary-700)] text-white shadow'
                  : 'text-[var(--color-text-secondary)] hover:text-[var(--color-text)]',
              )}
            >
              <span aria-hidden="true">{icon}</span> {label}
            </button>
          )
        })}
      </div>
    </fieldset>
  )
}
