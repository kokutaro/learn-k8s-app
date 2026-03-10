import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider, createMemoryHistory, createRouter } from '@tanstack/react-router'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { DASHBOARD_SETTINGS_KEY } from '../lib/dashboard-settings'
import { routeTree } from '../routeTree.gen'
import { server } from '../test/server'

const areaOneId = '11111111-1111-4111-8111-111111111111'
const areaTwoId = '22222222-2222-4222-8222-222222222222'
const areaOnePlanId = '33333333-3333-4333-8333-333333333333'

function renderRoute(path = '/dashboard') {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        refetchOnWindowFocus: false,
      },
    },
  })
  const router = createRouter({
    routeTree,
    history: createMemoryHistory({ initialEntries: [path] }),
    context: { queryClient },
    defaultPreload: 'intent',
    defaultPreloadStaleTime: 0,
  })

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )

  return {
    user: userEvent.setup(),
  }
}

function mockDashboardApis() {
  server.use(
    http.get('/api/v1/cleaning-areas', () => HttpResponse.json({
      data: [
        {
          id: areaOneId,
          facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          name: '3F East',
          currentWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
            effectiveFromWeekLabel: '2026/3/2 週',
          },
          memberCount: 2,
          spotCount: 2,
          version: 1,
        },
        {
          id: areaTwoId,
          facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          name: '4F West',
          currentWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
            effectiveFromWeekLabel: '2026/3/2 週',
          },
          memberCount: 1,
          spotCount: 1,
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
    })),
    http.get('/api/v1/cleaning-areas/:areaId/current-week', ({ params }) => HttpResponse.json({
      data: {
        areaId: params.areaId,
        weekId: '2026-W10',
        weekLabel: '2026/3/2 週',
        timeZoneId: 'Asia/Tokyo',
      },
    })),
    http.get('/api/v1/cleaning-areas/:areaId', ({ params }) => {
      if (params.areaId === areaOneId) {
        return HttpResponse.json({
          data: {
            id: areaOneId,
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
            spots: [
              { id: '44444444-4444-4444-8444-444444444444', name: 'Pantry', sortOrder: 10 },
              { id: '55555555-5555-4555-8555-555555555555', name: 'Meeting Room', sortOrder: 20 },
            ],
            members: [],
            version: 1,
          },
        }, { headers: { ETag: '"1"' } })
      }

      return HttpResponse.json({
        data: {
          id: areaTwoId,
          facilityId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
          name: '4F West',
          currentWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
            effectiveFromWeekLabel: '2026/3/2 週',
          },
          pendingWeekRule: null,
          rotationCursor: 0,
          spots: [
            { id: '66666666-6666-4666-8666-666666666666', name: 'Entrance', sortOrder: 10 },
          ],
          members: [],
          version: 1,
        },
      }, { headers: { ETag: '"1"' } })
    }),
    http.get('/api/v1/weekly-duty-plans', ({ request }) => {
      const url = new URL(request.url)
      const areaId = url.searchParams.get('areaId')

      if (areaId === areaOneId) {
        return HttpResponse.json({
          data: [
            {
              id: areaOnePlanId,
              areaId: areaOneId,
              weekId: '2026-W10',
              weekLabel: '2026/3/2 週',
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
        })
      }

      return HttpResponse.json({
        data: [],
        meta: {
          limit: 1,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/weekly-duty-plans',
        },
      })
    }),
    http.get('/api/v1/weekly-duty-plans/:planId', ({ params }) => {
      if (params.planId !== areaOnePlanId) {
        return HttpResponse.json({
          error: {
            code: 'NotFound',
            message: 'WeeklyDutyPlan was not found.',
          },
        }, { status: 404 })
      }

      return HttpResponse.json({
        data: {
          id: areaOnePlanId,
          areaId: areaOneId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          revision: 1,
          status: 'published',
          version: 1,
          assignmentPolicy: { fairnessWindowWeeks: 4 },
          assignments: [
            {
              spotId: '44444444-4444-4444-8444-444444444444',
              userId: '77777777-7777-4777-8777-777777777777',
              user: {
                userId: '77777777-7777-4777-8777-777777777777',
                employeeNumber: '123456',
                displayName: 'Hanako',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
          offDutyEntries: [
            {
              userId: '88888888-8888-4888-8888-888888888888',
              user: {
                userId: '88888888-8888-4888-8888-888888888888',
                employeeNumber: '654321',
                displayName: 'Taro',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
        },
      }, { headers: { ETag: '"1"' } })
    }),
  )
}

describe('dashboard page', () => {
  it('shows an empty state when no area is configured', async () => {
    mockDashboardApis()

    renderRoute()

    expect(await screen.findByText('表示エリアが未設定です')).toBeVisible()
  })

  it('persists layout changes and renders selected areas', async () => {
    mockDashboardApis()
    const { user } = renderRoute()

    await user.click(await screen.findByRole('button', { name: '設定' }))
    await user.selectOptions(screen.getByLabelText('レイアウト'), 'double')
    await user.selectOptions(screen.getByLabelText('エリア 1'), areaOneId)
    await user.selectOptions(screen.getByLabelText('エリア 2'), areaTwoId)

    await screen.findByRole('heading', { name: '3F East', level: 2 })
    await screen.findByRole('heading', { name: '4F West', level: 2 })
    await screen.findByText('Pantry')
    await screen.findByText('Hanako')
    await screen.findByText('今週の計画がありません')

    await waitFor(() => {
      expect(window.localStorage.getItem(DASHBOARD_SETTINGS_KEY)).toBe(
        JSON.stringify({
          layout: 'double',
          areaIds: [areaOneId, areaTwoId],
        }),
      )
    })
  })

  it('hydrates the dashboard from saved settings', async () => {
    mockDashboardApis()
    window.localStorage.setItem(DASHBOARD_SETTINGS_KEY, JSON.stringify({
      layout: 'single',
      areaIds: [areaOneId],
    }))

    renderRoute()

    await screen.findByRole('heading', { name: '3F East', level: 2 })
    expect(await screen.findByText('Pantry')).toBeVisible()
    expect(await screen.findByText('Hanako')).toBeVisible()
    expect(await screen.findByText('Taro')).toBeVisible()
  })
})
