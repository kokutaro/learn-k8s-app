import { type Page, expect, test } from '@playwright/test'

const areaId = '11111111-1111-4111-8111-111111111111'
const planId = '22222222-2222-4222-8222-222222222222'
const facilityAId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const facilityBId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const userAId = '55555555-5555-4555-8555-555555555555'
const userBId = '66666666-6666-4666-8666-666666666666'

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

function createFacilities(count = 32) {
  return Array.from({ length: count }, (_, index) => ({
    id: `${index % 2 === 0 ? facilityAId : facilityBId}`.replace('aaaaaaaaaaaa', (index + 1).toString().padStart(12, '0')).replace('bbbbbbbbbbbb', (index + 1).toString().padStart(12, '0')),
    facilityCode: `FAC-${(1000 + index).toString()}`,
    name: `Facility ${index + 1}`,
    description: 'Mock facility',
    timeZoneId: 'Asia/Tokyo',
    lifecycleStatus: index % 2 === 0 ? 'active' : 'inactive',
    version: 1,
  }))
}

function createUsers(count = 32) {
  return Array.from({ length: count }, (_, index) => ({
    userId: `77777777-7777-4777-8777-${(index + 1).toString().padStart(12, '0')}`,
    employeeNumber: `${(200000 + index).toString()}`,
    displayName: `Member ${index + 1}`,
    emailAddress: `member${index + 1}@example.com`,
    departmentCode: index % 2 === 0 ? 'OPS' : 'ENG',
    lifecycleStatus: index % 2 === 0 ? 'active' : 'archived',
    authIdentityLinks: [],
    version: 1,
  }))
}

function createCleaningAreaSummaryItems(count = 24) {
  return Array.from({ length: count }, (_, index) => ({
    id: `88888888-8888-4888-8888-${(index + 1).toString().padStart(12, '0')}`,
    facilityId: index % 2 === 0 ? facilityAId : facilityBId,
    name: `Area ${index + 1}`,
    currentWeekRule: {
      startDay: 'monday',
      startTime: '09:00:00',
      timeZoneId: 'Asia/Tokyo',
      effectiveFromWeek: '2026-W10',
      effectiveFromWeekLabel: '2026/3/2 週',
    },
    memberCount: 20,
    spotCount: 20,
    version: 1,
  }))
}

function createCleaningAreaDetail() {
  return {
    id: areaId,
    facilityId: facilityAId,
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
    spots: Array.from({ length: 30 }, (_, index) => ({
      id: `99999999-9999-4999-8999-${(index + 1).toString().padStart(12, '0')}`,
      name: `Spot ${index + 1}`,
      sortOrder: (index + 1) * 10,
    })),
    members: Array.from({ length: 30 }, (_, index) => ({
      id: `12121212-1212-4121-8121-${(index + 1).toString().padStart(12, '0')}`,
      userId: `13131313-1313-4131-8131-${(index + 1).toString().padStart(12, '0')}`,
      employeeNumber: `${(300000 + index).toString()}`,
      displayName: `Area Member ${index + 1}`,
    })),
    version: 1,
  }
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
  expect(Math.abs(moved.maxScrollLeft - moved.afterScrollLeft)).toBeLessThanOrEqual(4)
}

async function setupWeeklyDutyPlanRoutes(page: Page) {
  const assignments = createAssignments()

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

  await page.route('**/api/v1/cleaning-areas/11111111-1111-4111-8111-111111111111/current-week', async (route) => {
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

  await page.route('**/api/v1/cleaning-areas/11111111-1111-4111-8111-111111111111', async (route) => {
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
    await route.fulfill({
      json: {
        data: [
          {
            id: planId,
            areaId,
            weekId: '2026-W10',
            weekLabel: '2026/3/2 週',
            revision: 1,
            status: 'draft',
            version: 1,
          },
        ],
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

  await page.route('**/api/v1/weekly-duty-plans/22222222-2222-4222-8222-222222222222', async (route) => {
    await route.fulfill({
      json: {
        data: {
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
        },
      },
      headers: { ETag: '"1"' },
    })
  })
}

async function setupFacilitiesRoutes(page: Page) {
  const facilities = createFacilities()

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
}

async function setupUsersRoutes(page: Page) {
  const users = createUsers()

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
}

async function setupCleaningAreasRoutes(page: Page) {
  const areas = createCleaningAreaSummaryItems()
  const areaDetail = createCleaningAreaDetail()

  await page.route('**/api/v1/facilities?*', async (route) => {
    await route.fulfill({
      json: {
        data: [
          {
            id: facilityAId,
            facilityCode: 'FAC-1000',
            name: 'Facility A',
            description: 'Mock facility A',
            timeZoneId: 'Asia/Tokyo',
            lifecycleStatus: 'active',
            version: 1,
          },
          {
            id: facilityBId,
            facilityCode: 'FAC-2000',
            name: 'Facility B',
            description: 'Mock facility B',
            timeZoneId: 'Asia/Tokyo',
            lifecycleStatus: 'active',
            version: 1,
          },
        ],
        meta: {
          limit: 100,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/facilities',
        },
      },
    })
  })

  await page.route('**/api/v1/users?*', async (route) => {
    await route.fulfill({
      json: {
        data: [
          {
            userId: userAId,
            employeeNumber: '123456',
            displayName: 'User A',
            emailAddress: 'a@example.com',
            departmentCode: 'OPS',
            lifecycleStatus: 'active',
            authIdentityLinks: [],
            version: 1,
          },
          {
            userId: userBId,
            employeeNumber: '234567',
            displayName: 'User B',
            emailAddress: 'b@example.com',
            departmentCode: 'OPS',
            lifecycleStatus: 'active',
            authIdentityLinks: [],
            version: 1,
          },
        ],
        meta: {
          limit: 100,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/users',
        },
      },
    })
  })

  await page.route('**/api/v1/cleaning-areas?*', async (route) => {
    const url = new URL(route.request().url())
    const facilityId = url.searchParams.get('facilityId')
    const userId = url.searchParams.get('userId')

    const filtered = areas.filter((item) => {
      if (facilityId && item.facilityId !== facilityId) {
        return false
      }
      if (userId && userId === userBId) {
        return Number.parseInt(item.id.slice(-1), 10) % 2 === 0
      }
      return true
    })

    await route.fulfill({
      json: {
        data: filtered,
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/cleaning-areas',
        },
      },
    })
  })

  await page.route(`**/api/v1/cleaning-areas/${areaId}`, async (route) => {
    await route.fulfill({
      json: {
        data: areaDetail,
      },
      headers: { ETag: '"1"' },
    })
  })
}

for (const width of [360, 390, 430]) {
  test(`mobile portrait ${width}px keeps table readable and prevents page-level horizontal overflow`, async ({ page }) => {
    await page.setViewportSize({ width, height: 844 })
    await setupWeeklyDutyPlanRoutes(page)

    await page.goto(`/weekly-duty-plans?areaId=${areaId}&planId=${planId}`)

    const primaryTableScroll = page.getByTestId('data-table-scroll').first()
    await expect(primaryTableScroll).toBeVisible()

    const scrollContainerClassName = await primaryTableScroll.getAttribute('class')
    expect(scrollContainerClassName ?? '').toContain('overflow-x-auto')

    const firstTableClassName = await page.getByRole('table').first().getAttribute('class')
    expect(firstTableClassName ?? '').toContain('min-w-[44rem]')
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

test('cleaning-areas keeps scrollY after same-page filter updates', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setupCleaningAreasRoutes(page)

  await page.goto(`/cleaning-areas?areaId=${areaId}`)
  await expect(page.getByRole('heading', { name: '掃除エリア管理' })).toBeVisible()
  await expect(page.getByRole('heading', { name: '3F East' })).toBeVisible()

  await page.evaluate(() => window.scrollTo(0, 1000))
  const beforeFirstUpdate = await page.evaluate(() => window.scrollY)

  await page.getByLabel('施設').selectOption(facilityBId)
  await expect(page.getByText('Area 2', { exact: true })).toBeVisible()
  const afterFirstUpdate = await assertScrollYIsKeptAfterUpdate(page, beforeFirstUpdate)

  await page.getByLabel('ユーザー所属').selectOption(userBId)
  await expect(page.getByText('Area 4', { exact: true })).toBeVisible()
  await assertScrollYIsKeptAfterUpdate(page, afterFirstUpdate)
})

for (const width of [768, 1024]) {
  test(`tablet/desktop ${width}px keeps table layout stable`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    await setupWeeklyDutyPlanRoutes(page)

    await page.goto(`/weekly-duty-plans?areaId=${areaId}&planId=${planId}`)

    const primaryTableScroll = page.getByTestId('data-table-scroll').first()
    await expect(primaryTableScroll).toBeVisible()

    const firstTableClassName = await page.getByRole('table').first().getAttribute('class')
    expect(firstTableClassName ?? '').toContain('min-w-[44rem]')

    const hasPageHorizontalOverflow = await page.evaluate(() => {
      const htmlOverflow = document.documentElement.scrollWidth > document.documentElement.clientWidth
      const bodyOverflow = document.body.scrollWidth > document.body.clientWidth
      return htmlOverflow || bodyOverflow
    })
    expect(hasPageHorizontalOverflow).toBe(false)

    await expect(page.getByRole('heading', { name: '清掃計画' })).toBeVisible()
  })
}
