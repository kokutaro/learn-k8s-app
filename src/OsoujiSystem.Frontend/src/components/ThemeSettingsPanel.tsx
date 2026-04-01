import { useCallback, useEffect, useRef, useState } from 'react'
import { ColorPalettePicker } from './ui/ColorPalettePicker'
import { DarkModeToggle } from './ui/DarkModeToggle'

export function ThemeSettingsPanel() {
  const [open, setOpen] = useState(false)
  const panelRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)

  const toggle = useCallback(() => setOpen((prev) => !prev), [])

  useEffect(() => {
    if (!open) return

    function handleClickOutside(e: MouseEvent) {
      if (
        panelRef.current &&
        !panelRef.current.contains(e.target as Node) &&
        buttonRef.current &&
        !buttonRef.current.contains(e.target as Node)
      ) {
        setOpen(false)
      }
    }

    function handleEscape(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setOpen(false)
        buttonRef.current?.focus()
      }
    }

    document.addEventListener('mousedown', handleClickOutside)
    document.addEventListener('keydown', handleEscape)
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
      document.removeEventListener('keydown', handleEscape)
    }
  }, [open])

  return (
    <div className="relative">
      <button
        ref={buttonRef}
        type="button"
        onClick={toggle}
        aria-expanded={open}
        aria-label="テーマ設定"
        className="flex h-10 w-10 items-center justify-center rounded-xl text-lg text-[var(--color-text-secondary)] transition hover:bg-[var(--color-surface)] hover:text-[var(--color-text)]"
      >
        🎨
      </button>
      {open && (
        <div
          ref={panelRef}
          data-testid="theme-settings-panel"
          aria-label="テーマ設定"
          className="glass-panel absolute bottom-full left-0 z-50 mb-2 w-64 space-y-5 rounded-2xl p-4 shadow-xl"
        >
          <ColorPalettePicker />
          <DarkModeToggle />
        </div>
      )}
    </div>
  )
}
