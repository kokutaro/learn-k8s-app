import { type Page, expect, test } from '@playwright/test'

const baseFacilities = Array.from({ length: 12 }, (_, index) => ({
  id: `aaaaaaaa-aaaa-4aaa-8aaa-${(index + 1).toString().padStart(12, '0')}`,
  facilityCode: `FAC-${(1000 + index).toString()}`,
  name: `Facility ${index + 1}`,
  description: 'Mock facility',
  timeZoneId: 'Asia/Tokyo',
  lifecycleStatus: index % 2 === 0 ? 'active' : 'inactive',
  version: 1,
}))

async function assertScrollYIsKeptAfterUpdate(page: Page, baseline: number) {
  const after = await page.evaluate(() => window.scrollY)
  expect(Math.abs(after - baseline)).toBeLessThanOrEqual(120)
  return after
}

async function setupFacilitiesRoutes(page: Page) {
  let facilities = [...baseFacilities]

  await page.route('**/api/v1/facilities?*', async (route) => {
    const url = new URL(route.request().url())
    const status = url.searchParams.get('status')
    const filtered = status ? facilities.filter((item) => item.lifecycleStatus === status) : facilities

    await route.fulfill({
      json: {
        data: filtered,
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/facilities',
        },
      },
    })
  })

  await page.route('**/api/v1/facilities', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }

    const body = route.request().postDataJSON() as {
      facilityCode: string
      name: string
      description?: string
      timeZoneId: string
    }
    const facilityId = 'ffffffff-ffff-4fff-8fff-ffffffffffff'
    facilities = [
      {
        id: facilityId,
        facilityCode: body.facilityCode,
        name: body.name,
        description: body.description ?? '',
        timeZoneId: body.timeZoneId,
        lifecycleStatus: 'active',
        version: 1,
      },
      ...facilities,
    ]

    await route.fulfill({
      status: 201,
      json: {
        data: {
          facilityId,
        },
      },
    })
  })
}

test('facilities keeps scrollY after consecutive same-page updates', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setupFacilitiesRoutes(page)

  await page.goto('/facilities')
  await expect(page.getByRole('heading', { name: '施設管理' })).toBeVisible()

  await page.evaluate(() => window.scrollTo(0, 800))
  const beforeFirstUpdate = await page.evaluate(() => window.scrollY)

  await page.getByLabel('状態').selectOption('active')
  await expect(page.getByText('Facility 1', { exact: true })).toBeVisible()
  const afterFirstUpdate = await assertScrollYIsKeptAfterUpdate(page, beforeFirstUpdate)

  await page.getByLabel('状態').selectOption('inactive')
  await expect(page.getByText('Facility 2', { exact: true })).toBeVisible()
  await assertScrollYIsKeptAfterUpdate(page, afterFirstUpdate)
})

test('facilities can create a new facility from modal', async ({ page }) => {
  await setupFacilitiesRoutes(page)

  await page.goto('/facilities')
  await page.getByRole('button', { name: '施設を追加' }).click()

  await page.getByLabel('施設コード').fill('FAC-9000')
  await page.getByLabel('施設名').fill('North Tower')
  await page.getByLabel('タイムゾーン').fill('Asia/Tokyo')
  await page.getByLabel('説明').fill('Critical facility for night shift')
  await page.getByRole('button', { name: '保存' }).click()

  await expect(page.getByText('施設を追加しました。')).toBeVisible()
  await expect(page.getByText('North Tower', { exact: true })).toBeVisible()
  await expect(page.getByText('FAC-9000', { exact: true })).toBeVisible()
})
