import { expect, test } from '@playwright/test'

test('dashboard stores selected areas and renders current assignments', async ({ page }) => {
  await page.route('**/api/v1/cleaning-areas?*', async (route) => {
    await route.fulfill({
      json: {
        data: [
          {
            id: '11111111-1111-4111-8111-111111111111',
            facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
            name: '3F East',
            currentWeekRule: {
              startDay: 'monday',
              startTime: '09:00:00',
              timeZoneId: 'Asia/Tokyo',
              effectiveFromWeek: '2026-W10',
            },
            memberCount: 2,
            spotCount: 2,
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
          areaId: '11111111-1111-4111-8111-111111111111',
          weekId: '2026-W10',
          timeZoneId: 'Asia/Tokyo',
        },
      },
    })
  })

  await page.route('**/api/v1/cleaning-areas/11111111-1111-4111-8111-111111111111', async (route) => {
    await route.fulfill({
      json: {
        data: {
          id: '11111111-1111-4111-8111-111111111111',
          facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          name: '3F East',
          currentWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
          },
          pendingWeekRule: null,
          rotationCursor: 0,
          spots: [
            { id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Pantry', sortOrder: 10 },
            { id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', name: 'Meeting Room', sortOrder: 20 },
          ],
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
            id: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
            areaId: '11111111-1111-4111-8111-111111111111',
            weekId: '2026-W10',
            revision: 1,
            status: 'published',
            version: 1,
          },
        ],
        meta: {
          limit: 1,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/weekly-duty-plans',
        },
      },
    })
  })

  await page.route('**/api/v1/weekly-duty-plans/dddddddd-dddd-4ddd-8ddd-dddddddddddd', async (route) => {
    await route.fulfill({
      json: {
        data: {
          id: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd',
          areaId: '11111111-1111-4111-8111-111111111111',
          weekId: '2026-W10',
          revision: 1,
          status: 'published',
          version: 1,
          assignmentPolicy: { fairnessWindowWeeks: 4 },
          assignments: [
            {
              spotId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
              userId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
              user: {
                userId: 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee',
                employeeNumber: '123456',
                displayName: 'Hanako',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
          offDutyEntries: [
            {
              userId: 'ffffffff-ffff-4fff-8fff-ffffffffffff',
              user: {
                userId: 'ffffffff-ffff-4fff-8fff-ffffffffffff',
                employeeNumber: '654321',
                displayName: 'Taro',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
        },
      },
      headers: { ETag: '"1"' },
    })
  })

  await page.goto('/dashboard')
  await page.getByRole('button', { name: '設定' }).click()
  await page.selectOption('select', '11111111-1111-4111-8111-111111111111')

  await expect(page.getByText('3F East')).toBeVisible()
  await expect(page.getByText('Pantry')).toBeVisible()
  await expect(page.getByText('Hanako')).toBeVisible()
  await expect(page.getByText('Taro')).toBeVisible()
})
