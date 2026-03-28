import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryHistory, createRouter, RouterProvider } from '@tanstack/react-router'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { routeTree } from '../../routeTree.gen'
import { server } from '../../test/server'

const areaId = '11111111-1111-4111-8111-111111111111'
const planId = '22222222-2222-4222-8222-222222222222'
const secondPlanId = '55555555-5555-4555-8555-555555555555'

function renderRoute(path = '/weekly-duty-plans') {
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

function mockWeeklyDutyPlanApis(status: 'draft' | 'published' = 'draft') {
  server.use(
    http.get('/api/v1/cleaning-areas', () => HttpResponse.json({
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
    })),
    http.get('/api/v1/cleaning-areas/:selectedAreaId/current-week', () => HttpResponse.json({
      data: {
        areaId,
        weekId: '2026-W10',
        weekLabel: '2026/3/2 週',
        timeZoneId: 'Asia/Tokyo',
      },
    })),
    http.get('/api/v1/cleaning-areas/:selectedAreaId', () => HttpResponse.json({
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
        spots: [
          { id: '33333333-3333-4333-8333-333333333333', name: 'Pantry', sortOrder: 10 },
        ],
        members: [],
        version: 1,
      },
    }, { headers: { ETag: '"1"' } })),
    http.get('/api/v1/weekly-duty-plans', () => HttpResponse.json({
      data: [
        {
          id: planId,
          areaId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          revision: 1,
          status,
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
    })),
    http.get('/api/v1/weekly-duty-plans/:selectedPlanId', () => HttpResponse.json({
      data: {
        id: planId,
        areaId,
        weekId: '2026-W10',
        weekLabel: '2026/3/2 週',
        revision: 1,
        status,
        version: 1,
        assignmentPolicy: { fairnessWindowWeeks: 4 },
        assignments: [
          {
            spotId: '33333333-3333-4333-8333-333333333333',
            userId: '44444444-4444-4444-8444-444444444444',
            user: {
              userId: '44444444-4444-4444-8444-444444444444',
              employeeNumber: '123456',
              displayName: 'Hanako',
              departmentCode: 'OPS',
              lifecycleStatus: 'active',
            },
          },
        ],
        offDutyEntries: [],
      },
    }, { headers: { ETag: '"1"' } })),
  )
}

describe('weekly duty plans page', () => {
  it('keeps generate disabled until an area is selected', async () => {
    mockWeeklyDutyPlanApis()

    renderRoute()

    expect(await screen.findByRole('button', { name: '今週の計画を作成' })).toBeDisabled()
  })

  it('disables publish when the selected plan is already published', async () => {
    mockWeeklyDutyPlanApis('published')

    renderRoute(`/weekly-duty-plans?areaId=${areaId}&planId=${planId}`)

    expect(await screen.findByRole('button', { name: '発行する' })).toBeDisabled()
  })

  it('blocks invalid fairness window values before calling the API', async () => {
    let generateCalls = 0
    mockWeeklyDutyPlanApis()
    server.use(
      http.post('/api/v1/weekly-duty-plans', () => {
        generateCalls += 1
        return HttpResponse.json({
          data: {
            planId,
            weekId: '2026-W10',
          },
        })
      }),
    )

    const { user } = renderRoute(`/weekly-duty-plans?areaId=${areaId}`)

    const fairnessInput = await screen.findByLabelText('公平性ウィンドウ')
    await user.clear(fairnessInput)
    await user.type(fairnessInput, '0')
    await user.click(screen.getByRole('button', { name: '今週の計画を作成' }))

    expect(generateCalls).toBe(0)
    expect(screen.getByText('Too small: expected number to be >=1')).toBeVisible()
  })

  it('keeps the area history list when opening plan details', async () => {
    mockWeeklyDutyPlanApis()
    server.use(
      http.get('/api/v1/weekly-duty-plans', ({ request }) => {
        const url = new URL(request.url)
        const weekId = url.searchParams.get('weekId')

        const plans = [
          {
            id: planId,
            areaId,
            weekId: '2026-W10',
            weekLabel: '2026/3/2 週',
            revision: 1,
            status: 'draft' as const,
            version: 1,
          },
          {
            id: secondPlanId,
            areaId,
            weekId: '2026-W09',
            weekLabel: '2026/2/23 週',
            revision: 1,
            status: 'published' as const,
            version: 1,
          },
        ]

        return HttpResponse.json({
          data: weekId ? plans.filter((plan) => plan.weekId === weekId) : plans,
          meta: {
            limit: 20,
            hasNext: false,
            nextCursor: null,
          },
          links: {
            self: '/api/v1/weekly-duty-plans',
          },
        })
      }),
      http.get('/api/v1/weekly-duty-plans/:selectedPlanId', ({ params }) => HttpResponse.json({
        data: {
          id: params.selectedPlanId,
          areaId,
          weekId: params.selectedPlanId === secondPlanId ? '2026-W09' : '2026-W10',
          weekLabel: params.selectedPlanId === secondPlanId ? '2026/2/23 週' : '2026/3/2 週',
          revision: 1,
          status: params.selectedPlanId === secondPlanId ? 'published' : 'draft',
          version: 1,
          assignmentPolicy: { fairnessWindowWeeks: 4 },
          assignments: [
            {
              spotId: '33333333-3333-4333-8333-333333333333',
              userId: '44444444-4444-4444-8444-444444444444',
              user: {
                userId: '44444444-4444-4444-8444-444444444444',
                employeeNumber: '123456',
                displayName: 'Hanako',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
          offDutyEntries: [],
        },
      }, { headers: { ETag: '"1"' } })),
    )

    const { user } = renderRoute(`/weekly-duty-plans?areaId=${areaId}`)

    expect(await screen.findByText('2026/3/2 週')).toBeVisible()
    expect(await screen.findByText('2026/2/23 週')).toBeVisible()

    const detailButtons = await screen.findAllByRole('button', { name: '詳細' })
    await user.click(detailButtons[1]!)

    expect(await screen.findByText('2026/3/2 週')).toBeVisible()
    expect(await screen.findByText('2026/2/23 週')).toBeVisible()
  })
})
