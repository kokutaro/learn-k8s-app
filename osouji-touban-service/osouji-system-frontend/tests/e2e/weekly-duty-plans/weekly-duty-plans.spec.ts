import { type Page, expect, test } from '@playwright/test'

const areaId = '11111111-1111-4111-8111-111111111111'
const planId = '22222222-2222-4222-8222-222222222222'

function createAssignments() {
  return Array.from({ length: 24 }, (_, index) => ({
    spotId: `33333333-3333-4333-8333-${(index + 1).toString().padStart(12, '0')}`,
    userId: `44444444-4444-4444-8444-${(index + 1).toString().padStart(12, '0')}`,
    user: {
      userId: `44444444-4444-4444-8444-${(index + 1).toString().padStart(12, '0')}`,
      employeeNumber: `${(100000 + index).toString()}`,
      displayName: `User ${index + 1}`,
      departmentCode: 'OPS',
      lifecycleStatus: 'active',
    },
  }))
}

async function assertScrollYIsKeptAfterUpdate(page: Page, baseline: number) {
  const after = await page.evaluate(() => window.scrollY)
  expect(Math.abs(after - baseline)).toBeLessThanOrEqual(8)
  return after
}

async function assertCanScrollTableHorizontally(page: Page) {
  const containers = page.getByTestId('data-table-scroll')
  await expect(containers.first()).toBeVisible()

  const targetIndex = await containers.evaluateAll((elements) => {
    const index = elements.findIndex((element) => element.scrollWidth > element.clientWidth)
    return index
  })
  expect(targetIndex).toBeGreaterThanOrEqual(0)

  const targetContainer = containers.nth(targetIndex)

  const metrics = await targetContainer.evaluate((element) => {
    const node = element as HTMLElement
    return {
      clientWidth: node.clientWidth,
      scrollWidth: node.scrollWidth,
    }
  })
  expect(metrics.scrollWidth).toBeGreaterThan(metrics.clientWidth)

  const moved = await targetContainer.evaluate((element) => {
    const node = element as HTMLElement
    const beforeScrollLeft = node.scrollLeft
    const maxScrollLeft = Math.max(0, node.scrollWidth - node.clientWidth)
    node.scrollLeft = maxScrollLeft

    return {
      beforeScrollLeft,
      afterScrollLeft: node.scrollLeft,
      maxScrollLeft,
    }
  })
  expect(moved.maxScrollLeft).toBeGreaterThan(0)
  expect(moved.afterScrollLeft).toBeGreaterThan(moved.beforeScrollLeft)
}

async function setupWeeklyDutyPlanRoutes(page: Page, includeInitialPlan: boolean) {
  const assignments = createAssignments()
  let plans = includeInitialPlan
    ? [
        {
          id: planId,
          areaId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          revision: 1,
          status: 'draft',
          version: 1,
        },
      ]
    : []

  let currentPlan = {
    id: planId,
    areaId,
    weekId: '2026-W10',
    weekLabel: '2026/3/2 週',
    revision: 1,
    status: 'draft',
    version: 1,
    assignmentPolicy: { fairnessWindowWeeks: 4 },
    assignments,
    offDutyEntries: [],
  }

  await page.route('**/api/v1/cleaning-areas?*', async (route) => {
    await route.fulfill({
      json: {
        data: [
          {
            id: areaId,
            facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            name: '3F East',
            currentWeekRule: {
              startDay: 'monday',
              startTime: '09:00:00',
              timeZoneId: 'Asia/Tokyo',
              effectiveFromWeek: '2026-W10',
              effectiveFromWeekLabel: '2026/3/2 週',
            },
            memberCount: 24,
            spotCount: 24,
            version: 1,
          },
        ],
        meta: {
          limit: 100,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/cleaning-areas',
        },
      },
    })
  })

  await page.route(`**/api/v1/cleaning-areas/${areaId}/current-week`, async (route) => {
    await route.fulfill({
      json: {
        data: {
          areaId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          timeZoneId: 'Asia/Tokyo',
        },
      },
    })
  })

  await page.route(`**/api/v1/cleaning-areas/${areaId}`, async (route) => {
    await route.fulfill({
      json: {
        data: {
          id: areaId,
          facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          name: '3F East',
          currentWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
            effectiveFromWeekLabel: '2026/3/2 週',
          },
          pendingWeekRule: null,
          rotationCursor: 0,
          spots: assignments.map((assignment, index) => ({
            id: assignment.spotId,
            name: `Spot ${index + 1}`,
            sortOrder: (index + 1) * 10,
          })),
          members: [],
          version: 1,
        },
      },
      headers: { ETag: '"1"' },
    })
  })

  await page.route('**/api/v1/weekly-duty-plans?*', async (route) => {
    const url = new URL(route.request().url())
    const status = url.searchParams.get('status')
    const filtered = status ? plans.filter((item) => item.status === status) : plans

    await route.fulfill({
      json: {
        data: filtered,
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/weekly-duty-plans',
        },
      },
    })
  })

  await page.route('**/api/v1/weekly-duty-plans', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }

    plans = [
      {
        id: planId,
        areaId,
        weekId: '2026-W10',
        weekLabel: '2026/3/2 週',
        revision: 1,
        status: 'draft',
        version: 1,
      },
      ...plans,
    ]

    await route.fulfill({
      status: 201,
      json: {
        data: {
          planId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
        },
      },
    })
  })

  await page.route(`**/api/v1/weekly-duty-plans/${planId}`, async (route) => {
    await route.fulfill({
      json: {
        data: currentPlan,
      },
      headers: { ETag: '"1"' },
    })
  })

  await page.route(`**/api/v1/weekly-duty-plans/${planId}/publication`, async (route) => {
    if (route.request().method() !== 'PUT') {
      await route.fallback()
      return
    }

    if (route.request().headers()['if-match'] !== '"1"') {
      await route.fulfill({ status: 412, json: { error: { code: 'PreconditionFailed', message: 'ETag mismatch' } } })
      return
    }

    currentPlan = {
      ...currentPlan,
      status: 'published',
      version: currentPlan.version + 1,
    }
    plans = plans.map((plan) => (plan.id === planId ? { ...plan, status: 'published', version: plan.version + 1 } : plan))

    await route.fulfill({
      json: {
        data: {
          planId,
          status: 'published',
        },
      },
    })
  })
}

for (const width of [360, 390, 430]) {
  test(`weekly-duty-plans mobile ${width}px keeps table readable without page overflow`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 })
    await setupWeeklyDutyPlanRoutes(page, true)

    await page.goto(`/weekly-duty-plans?areaId=${areaId}&planId=${planId}`)

    const primaryTableScroll = page.getByTestId('data-table-scroll').first()
    await expect(primaryTableScroll).toBeVisible()

    await assertCanScrollTableHorizontally(page)

    const hasPageHorizontalOverflow = await page.evaluate(() => {
      const htmlOverflow = document.documentElement.scrollWidth > document.documentElement.clientWidth
      const bodyOverflow = document.body.scrollWidth > document.body.clientWidth
      return htmlOverflow || bodyOverflow
    })
    expect(hasPageHorizontalOverflow).toBe(false)

    await page.evaluate(() => window.scrollTo(0, 500))
    const beforeUpdate = await page.evaluate(() => window.scrollY)

    await page.getByLabel('状態').selectOption('draft')
    await expect(page.getByRole('button', { name: '今週の計画を作成' })).toBeVisible()
    await assertScrollYIsKeptAfterUpdate(page, beforeUpdate)
  })
}

test('weekly-duty-plans can generate and publish a plan', async ({ page }) => {
  await setupWeeklyDutyPlanRoutes(page, false)

  await page.goto(`/weekly-duty-plans?areaId=${areaId}`)
  await expect(page.getByRole('heading', { name: '清掃計画' })).toBeVisible()

  await page.getByLabel('公平性ウィンドウ').fill('6')
  await page.getByRole('button', { name: '今週の計画を作成' }).click()

  await expect(page.getByText('今週の清掃計画を作成しました。')).toBeVisible()
  await expect(page.getByRole('button', { name: '発行する' })).toBeEnabled()

  await page.getByRole('button', { name: '発行する' }).click()

  await expect(page.getByText('清掃計画を発行しました。')).toBeVisible()
  await expect(page.getByRole('button', { name: '発行する' })).toBeDisabled()
})
