import { expect, test } from '@playwright/test'

test.describe('Theme settings', () => {
  test('opens theme settings panel from sidebar button', async ({ page }) => {
    await page.goto('/facilities')
    const themeButton = page.getByRole('button', { name: 'テーマ設定' })
    await expect(themeButton).toBeVisible()

    await themeButton.click()
    const panel = page.getByTestId('theme-settings-panel')
    await expect(panel).toBeVisible()
  })

  test('changes color scheme and persists after reload', async ({ page }) => {
    await page.goto('/facilities')

    // Clear any existing theme settings
    await page.evaluate(() => window.localStorage.removeItem('osouji.theme.v1'))
    await page.reload()

    // Open theme panel
    await page.getByRole('button', { name: 'テーマ設定' }).click()

    // Select blue palette
    await page.getByRole('radio', { name: 'ブルー' }).click()

    // Verify CSS variable changed
    const primaryColor = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--color-primary-700').trim(),
    )
    expect(primaryColor).toBe('#1d4ed8')

    // Reload and verify persistence
    await page.reload()
    const persistedColor = await page.evaluate(() =>
      getComputedStyle(document.documentElement).getPropertyValue('--color-primary-700').trim(),
    )
    expect(persistedColor).toBe('#1d4ed8')
  })

  test('switches to dark mode', async ({ page }) => {
    await page.goto('/facilities')

    await page.getByRole('button', { name: 'テーマ設定' }).click()
    await page.getByRole('radio', { name: 'ダーク' }).click()

    const hasDarkClass = await page.evaluate(() =>
      document.documentElement.classList.contains('dark'),
    )
    expect(hasDarkClass).toBe(true)
  })

  test('switches to light mode', async ({ page }) => {
    await page.goto('/facilities')

    await page.getByRole('button', { name: 'テーマ設定' }).click()
    await page.getByRole('radio', { name: 'ダーク' }).click()
    await page.getByRole('radio', { name: 'ライト' }).click()

    const hasDarkClass = await page.evaluate(() =>
      document.documentElement.classList.contains('dark'),
    )
    expect(hasDarkClass).toBe(false)
  })

  test('system mode follows prefers-color-scheme', async ({ page }) => {
    // Emulate dark color scheme
    await page.emulateMedia({ colorScheme: 'dark' })
    await page.goto('/facilities')

    await page.getByRole('button', { name: 'テーマ設定' }).click()
    await page.getByRole('radio', { name: 'システム' }).click()

    const hasDarkClass = await page.evaluate(() =>
      document.documentElement.classList.contains('dark'),
    )
    expect(hasDarkClass).toBe(true)

    // Switch to light system preference
    await page.emulateMedia({ colorScheme: 'light' })

    // Wait for the change to propagate
    await page.waitForFunction(() => !document.documentElement.classList.contains('dark'))
  })

  test('closes panel on Escape key', async ({ page }) => {
    await page.goto('/facilities')

    await page.getByRole('button', { name: 'テーマ設定' }).click()
    await expect(page.getByTestId('theme-settings-panel')).toBeVisible()

    await page.keyboard.press('Escape')
    await expect(page.getByTestId('theme-settings-panel')).not.toBeVisible()
  })

  test('closes panel on click outside', async ({ page }) => {
    await page.goto('/facilities')

    await page.getByRole('button', { name: 'テーマ設定' }).click()
    await expect(page.getByTestId('theme-settings-panel')).toBeVisible()

    // Click on main content area
    await page.locator('main').click()
    await expect(page.getByTestId('theme-settings-panel')).not.toBeVisible()
  })
})
