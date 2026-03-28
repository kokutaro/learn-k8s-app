import { type Page, expect, test } from '@playwright/test'

const baseUsers = Array.from({ length: 12 }, (_, index) => ({
  userId: `77777777-7777-4777-8777-${(index + 1).toString().padStart(12, '0')}`,
  employeeNumber: `${(200000 + index).toString()}`,
  displayName: `Member ${index + 1}`,
  emailAddress: `member${index + 1}@example.com`,
  departmentCode: index % 2 === 0 ? 'OPS' : 'ENG',
  lifecycleStatus: index % 2 === 0 ? 'active' : 'archived',
  authIdentityLinks: [],
  version: 1,
}))

async function assertScrollYIsKeptAfterUpdate(page: Page, baseline: number) {
  const after = await page.evaluate(() => window.scrollY)
  expect(Math.abs(after - baseline)).toBeLessThanOrEqual(120)
  return after
}

async function setupUsersRoutes(page: Page) {
  let users = [...baseUsers]

  await page.route('**/api/v1/users?*', async (route) => {
    const url = new URL(route.request().url())
    const status = url.searchParams.get('status')
    const filtered = status ? users.filter((item) => item.lifecycleStatus === status) : users

    await route.fulfill({
      json: {
        data: filtered,
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/users',
        },
      },
    })
  })

  await page.route('**/api/v1/users', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }

    const body = route.request().postDataJSON() as {
      employeeNumber: string
      displayName: string
      emailAddress?: string
      departmentCode?: string
    }
    const userId = '88888888-8888-4888-8888-888888888888'
    users = [
      {
        userId,
        employeeNumber: body.employeeNumber,
        displayName: body.displayName,
        emailAddress: body.emailAddress ?? null,
        departmentCode: body.departmentCode ?? null,
        lifecycleStatus: 'pendingActivation',
        authIdentityLinks: [],
        version: 1,
      },
      ...users,
    ]

    await route.fulfill({
      status: 201,
      json: {
        data: {
          userId,
        },
      },
    })
  })
}

test('users keeps scrollY after consecutive same-page updates', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setupUsersRoutes(page)

  await page.goto('/users')
  await expect(page.getByRole('heading', { name: 'ユーザー管理' })).toBeVisible()

  await page.evaluate(() => window.scrollTo(0, 800))
  const beforeFirstUpdate = await page.evaluate(() => window.scrollY)

  await page.getByLabel('状態').selectOption('active')
  await expect(page.getByText('Member 1', { exact: true })).toBeVisible()
  const afterFirstUpdate = await assertScrollYIsKeptAfterUpdate(page, beforeFirstUpdate)

  await page.getByLabel('状態').selectOption('archived')
  await expect(page.getByText('Member 2', { exact: true })).toBeVisible()
  await assertScrollYIsKeptAfterUpdate(page, afterFirstUpdate)
})

test('users can create a user from modal', async ({ page }) => {
  await setupUsersRoutes(page)

  await page.goto('/users')
  await page.getByRole('button', { name: 'ユーザーを追加' }).click()

  await page.getByLabel('社員番号').fill('345678')
  await page.getByLabel('表示名').fill('Sakura')
  await page.getByLabel('メールアドレス').fill('sakura@example.com')
  await page.getByLabel('部署コード').fill('OPS')
  await page.getByRole('button', { name: '保存' }).click()

  await expect(page.getByText('ユーザーを追加しました。')).toBeVisible()
  await expect(page.getByText('Sakura', { exact: true })).toBeVisible()
  await expect(page.getByText('345678', { exact: true })).toBeVisible()
})
