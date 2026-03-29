import { type Page, expect, test } from '@playwright/test'

const areaId = '11111111-1111-4111-8111-111111111111'
const facilityAId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const facilityBId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
const userAId = '55555555-5555-4555-8555-555555555555'
const userBId = '66666666-6666-4666-8666-666666666666'
const userCId = '77777777-7777-4777-8777-777777777777'

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
    members: [
      {
        id: '12121212-1212-4121-8121-000000000001',
        userId: userAId,
        employeeNumber: '123456',
        displayName: 'Area Member 1',
      },
      {
        id: '12121212-1212-4121-8121-000000000002',
        userId: userBId,
        employeeNumber: '234567',
        displayName: 'Area Member 2',
      },
    ],
    version: 1,
  }
}

async function assertScrollYIsKeptAfterUpdate(page: Page, baseline: number) {
  const after = await page.evaluate(() => window.scrollY)
  expect(Math.abs(after - baseline)).toBeLessThanOrEqual(8)
  return after
}

async function setupCleaningAreasRoutes(page: Page) {
  const areas = createCleaningAreaSummaryItems()
  let areaDetail = createCleaningAreaDetail()

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
          {
            userId: userCId,
            employeeNumber: '345678',
            displayName: 'User C',
            emailAddress: 'c@example.com',
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

  await page.route('**/api/v1/cleaning-areas/*/members', async (route) => {
    if (route.request().method() !== 'POST') {
      await route.fallback()
      return
    }

    if (route.request().headers()['if-match'] !== '"1"') {
      await route.fulfill({ status: 412, json: { error: { code: 'PreconditionFailed', message: 'ETag mismatch' } } })
      return
    }

    const body = route.request().postDataJSON() as { userId: string }
    const userId = body.userId
    const memberRecord = {
      id: userId === userAId
        ? '12121212-1212-4121-8121-100000000001'
        : userId === userBId
          ? '12121212-1212-4121-8121-100000000002'
          : '12121212-1212-4121-8121-100000000003',
      userId,
      employeeNumber: userId === userAId ? '123456' : userId === userBId ? '234567' : '345678',
      displayName: userId === userAId ? 'User A' : userId === userBId ? 'User B' : 'User C',
    }
    areaDetail = {
      ...areaDetail,
      members: areaDetail.members.some((member) => member.userId === userId)
        ? areaDetail.members
        : [...areaDetail.members, memberRecord],
    }

    await route.fulfill({
      status: 201,
      json: {
        data: {
          userId,
        },
      },
    })
  })

  await page.route('**/api/v1/cleaning-areas/*/members/*', async (route) => {
    if (route.request().method() !== 'DELETE') {
      await route.fallback()
      return
    }

    if (route.request().headers()['if-match'] !== '"1"') {
      await route.fulfill({ status: 412, json: { error: { code: 'PreconditionFailed', message: 'ETag mismatch' } } })
      return
    }

    const url = new URL(route.request().url())
    const userId = url.pathname.split('/').at(-1)
    if (userId) {
      areaDetail = {
        ...areaDetail,
        members: areaDetail.members.filter((member) => member.userId !== userId),
      }
    }

    await route.fulfill({
      status: 204,
      body: '',
    })
  })
}

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

test('cleaning-areas renders member cards on mobile and keeps unassign behavior', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await setupCleaningAreasRoutes(page)

  await page.goto(`/cleaning-areas?areaId=${areaId}`)
  await expect(page.getByRole('heading', { name: '掃除エリア管理' })).toBeVisible()

  const memberCards = page.getByTestId('member-cards')
  const memberTable = page.getByTestId('member-table')

  await expect(memberCards).toBeVisible()
  await expect(memberTable).toBeHidden()
  const firstMemberName = memberCards.getByText('Area Member 1', { exact: true })
  await expect(firstMemberName).toBeVisible()

  const firstUnassignButton = memberCards.getByRole('button', { name: '解除' }).first()
  await firstUnassignButton.click()

  await expect(page.getByText('メンバー割当を解除しました。')).toBeVisible()
  await expect(firstMemberName).toBeHidden()
})

test('cleaning-areas can assign a user in desktop table view', async ({ page }) => {
  await page.setViewportSize({ width: 1024, height: 900 })
  await setupCleaningAreasRoutes(page)

  await page.goto(`/cleaning-areas?areaId=${areaId}`)
  await expect(page.getByRole('heading', { name: '掃除エリア管理' })).toBeVisible()

  await page.getByLabel('アサインするユーザー').selectOption(userCId)
  await page.getByRole('button', { name: 'アサイン' }).click()

  await expect(page.getByText('ユーザーをアサインしました。')).toBeVisible()
  await expect(page.getByTestId('member-table').getByText('User C', { exact: true })).toBeVisible()
})

test('cleaning-areas keeps member table on desktop', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 900 })
  await setupCleaningAreasRoutes(page)

  await page.goto(`/cleaning-areas?areaId=${areaId}`)
  await expect(page.getByRole('heading', { name: '掃除エリア管理' })).toBeVisible()

  await expect(page.getByTestId('member-table')).toBeVisible()
  await expect(page.getByTestId('member-cards')).toBeHidden()
  await expect(page.getByRole('columnheader', { name: '社員番号' })).toBeVisible()
})
