import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { server } from '../test/server'
import {
  ApiError,
  addCleaningSpot,
  assignUserToArea,
  changeFacilityActivation,
  changeUserLifecycle,
  createCleaningArea,
  createFacility,
  createUser,
  explainApiError,
  generateWeeklyDutyPlan,
  getCleaningArea,
  getCleaningAreaCurrentWeek,
  getFacility,
  getUser,
  getWeeklyDutyPlan,
  listCleaningAreas,
  listFacilities,
  listUsers,
  listWeeklyDutyPlans,
  publishWeeklyDutyPlan,
  queryKeys,
  removeCleaningSpot,
  resolveEffectiveFromWeekLabel,
  resolveLifecycleTone,
  resolvePlanStatusLabel,
  resolveSpotName,
  resolveWeekLabel,
  resolveWeekRuleDraft,
  scheduleWeekRule,
  unassignUserFromArea,
  updateFacility,
  updateUser,
} from './api'

const facilityId = '00000000-0000-0000-0000-000000000010'
const userId = '00000000-0000-0000-0000-000000000011'
const areaId = '00000000-0000-0000-0000-000000000020'
const spotId = '00000000-0000-0000-0000-000000000021'
const planId = '00000000-0000-0000-0000-000000000030'

const weekRule = {
  startDay: 'monday',
  startTime: '09:00:00',
  timeZoneId: 'Asia/Tokyo',
  effectiveFromWeek: '2026-W10',
  effectiveFromWeekLabel: '2026/3/2 週',
} as const

const cleaningAreaDetail = {
  id: areaId,
  facilityId,
  name: '3F East',
  currentWeekRule: weekRule,
  pendingWeekRule: null,
  rotationCursor: 1,
  spots: [
    { id: spotId, name: 'Pantry', sortOrder: 10 },
    { id: '00000000-0000-0000-0000-000000000022', name: 'Meeting Room', sortOrder: 20 },
  ],
  members: [
    { id: '00000000-0000-0000-0000-000000000023', userId, employeeNumber: '123456' },
  ],
  version: 2,
}

beforeEach(() => {
  vi.restoreAllMocks()
})

describe('api client', () => {
  it('parses facility lists and exposes stable query keys', async () => {
    server.use(
      http.get('/api/v1/facilities', ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get('status')).toBe('active')
        expect(url.searchParams.get('sort')).toBe('name')
        expect(url.searchParams.get('limit')).toBe('20')
        expect(url.searchParams.has('query')).toBe(false)

        return HttpResponse.json({
          data: [
            {
              id: facilityId,
              facilityCode: 'TOKYO-HQ',
              name: 'Tokyo HQ',
              timeZoneId: 'Asia/Tokyo',
              lifecycleStatus: 'active',
              version: 1,
            },
          ],
          meta: { limit: 20, hasNext: false, nextCursor: null },
          links: { self: '/api/v1/facilities?status=active&sort=name&limit=20' },
        })
      }),
    )

    const response = await listFacilities({ query: '', status: 'active', sort: 'name', limit: 20 })

    expect(response.data[0]?.name).toBe('Tokyo HQ')
    expect(queryKeys.facilities({ status: 'active' })).toEqual(['facilities', { status: 'active' }])
    expect(queryKeys.facility(facilityId)).toEqual(['facility', facilityId])
    expect(queryKeys.users({ status: 'active' })).toEqual(['users', { status: 'active' }])
    expect(queryKeys.user(userId)).toEqual(['user', userId])
    expect(queryKeys.cleaningAreas({ facilityId })).toEqual(['cleaningAreas', { facilityId }])
    expect(queryKeys.cleaningArea(areaId)).toEqual(['cleaningArea', areaId])
    expect(queryKeys.cleaningAreaCurrentWeek(areaId)).toEqual(['cleaningAreaCurrentWeek', areaId])
    expect(queryKeys.weeklyDutyPlans({ areaId })).toEqual(['weeklyDutyPlans', { areaId }])
    expect(queryKeys.weeklyDutyPlan(planId)).toEqual(['weeklyDutyPlan', planId])
  })

  it('gets facility detail and preserves etag', async () => {
    server.use(
      http.get('/api/v1/facilities/:facilityId', ({ params }) => HttpResponse.json({
        data: {
          id: params.facilityId,
          facilityCode: 'TOKYO-HQ',
          name: 'Tokyo HQ',
          description: 'Main office',
          timeZoneId: 'Asia/Tokyo',
          lifecycleStatus: 'active',
          version: 4,
        },
      }, { headers: { ETag: '"facility-v4"' } })),
    )

    const response = await getFacility(facilityId)

    expect(response.data.description).toBe('Main office')
    expect(response.etag).toBe('"facility-v4"')
  })

  it('creates and updates facilities with the expected payloads', async () => {
    server.use(
      http.post('/api/v1/facilities', async ({ request }) => {
        expect(request.headers.get('accept')).toContain('application/json')
        expect(request.headers.get('content-type')).toContain('application/json')
        expect(await request.json()).toEqual({
          facilityCode: 'TOKYO-HQ',
          name: 'Tokyo HQ',
          description: 'Main office',
          timeZoneId: 'Asia/Tokyo',
        })

        return HttpResponse.json({
          data: { facilityId },
        }, {
          headers: {
            ETag: '"facility-v1"',
            Location: `/api/v1/facilities/${facilityId}`,
          },
        })
      }),
      http.put('/api/v1/facilities/:facilityId', async ({ request, params }) => {
        expect(request.headers.get('if-match')).toBe('"facility-v1"')
        expect(await request.json()).toEqual({
          name: 'Tokyo HQ Updated',
          description: 'Revised',
          timeZoneId: 'Asia/Tokyo',
        })

        return HttpResponse.json({
          data: {
            facilityId: params.facilityId,
            version: 2,
          },
        })
      }),
      http.put('/api/v1/facilities/:facilityId/activation', async ({ request, params }) => {
        expect(request.headers.get('if-match')).toBe('"facility-v1"')
        expect(await request.json()).toEqual({ lifecycleStatus: 'inactive' })

        return HttpResponse.json({
          data: {
            facilityId: params.facilityId,
            lifecycleStatus: 'inactive',
            version: 3,
          },
        })
      }),
    )

    await expect(createFacility({
      facilityCode: 'TOKYO-HQ',
      name: 'Tokyo HQ',
      description: 'Main office',
      timeZoneId: 'Asia/Tokyo',
    })).resolves.toMatchObject({
      data: { data: { facilityId } },
      etag: '"facility-v1"',
      location: `/api/v1/facilities/${facilityId}`,
    })

    await expect(updateFacility(facilityId, '"facility-v1"', {
      name: 'Tokyo HQ Updated',
      description: 'Revised',
      timeZoneId: 'Asia/Tokyo',
    })).resolves.toMatchObject({
      data: { data: { facilityId, version: 2 } },
    })

    await expect(changeFacilityActivation(facilityId, '"facility-v1"', 'inactive')).resolves.toMatchObject({
      data: { data: { facilityId, lifecycleStatus: 'inactive', version: 3 } },
    })
  })

  it('lists, fetches, creates, updates, and changes lifecycle for users', async () => {
    server.use(
      http.get('/api/v1/users', ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get('query')).toBe('hanako')
        expect(url.searchParams.get('status')).toBe('active')

        return HttpResponse.json({
          data: [
            {
              userId,
              employeeNumber: '123456',
              displayName: 'Hanako',
              lifecycleStatus: 'active',
              departmentCode: 'OPS',
              version: 1,
            },
          ],
          meta: { limit: 10, hasNext: false, nextCursor: null },
          links: { self: '/api/v1/users' },
        })
      }),
      http.get('/api/v1/users/:userId', ({ params }) => HttpResponse.json({
        data: {
          userId: params.userId,
          employeeNumber: '123456',
          displayName: 'Hanako',
          emailAddress: 'hanako@example.com',
          lifecycleStatus: 'active',
          departmentCode: 'OPS',
          version: 1,
        },
      }, { headers: { ETag: '"user-v1"' } })),
      http.post('/api/v1/users', async ({ request }) => {
        expect(await request.json()).toEqual({
          employeeNumber: '123456',
          displayName: 'Hanako',
          registrationSource: 'adminPortal',
          emailAddress: undefined,
          departmentCode: undefined,
        })

        return HttpResponse.json({ data: { userId } })
      }),
      http.patch('/api/v1/users/:userId', async ({ request, params }) => {
        expect(request.headers.get('if-match')).toBe('"user-v1"')
        expect(await request.json()).toEqual({
          displayName: 'Hanako Updated',
          emailAddress: undefined,
          departmentCode: undefined,
        })

        return HttpResponse.json({
          data: {
            userId: params.userId,
            version: 2,
          },
        })
      }),
      http.post('/api/v1/users/:userId/lifecycle', async ({ request, params }) => {
        expect(request.headers.get('if-match')).toBe('"user-v1"')
        expect(await request.json()).toEqual({ lifecycleStatus: 'suspended' })

        return HttpResponse.json({
          data: {
            userId: params.userId,
            lifecycleStatus: 'suspended',
            version: 3,
          },
        })
      }),
    )

    await expect(listUsers({ query: 'hanako', status: 'active', limit: 10 })).resolves.toMatchObject({
      data: [{ userId, displayName: 'Hanako' }],
    })

    await expect(getUser(userId)).resolves.toMatchObject({
      data: { userId, emailAddress: 'hanako@example.com' },
      etag: '"user-v1"',
    })

    await expect(createUser({
      employeeNumber: '123456',
      displayName: 'Hanako',
      emailAddress: '',
      departmentCode: '',
    })).resolves.toMatchObject({
      data: { data: { userId } },
    })

    await expect(updateUser(userId, '"user-v1"', {
      displayName: 'Hanako Updated',
      emailAddress: '',
      departmentCode: '',
    })).resolves.toMatchObject({
      data: { data: { userId, version: 2 } },
    })

    await expect(changeUserLifecycle(userId, '"user-v1"', 'suspended')).resolves.toMatchObject({
      data: { data: { userId, lifecycleStatus: 'suspended', version: 3 } },
    })
  })

  it('lists and fetches cleaning areas and current week detail', async () => {
    server.use(
      http.get('/api/v1/cleaning-areas', ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get('facilityId')).toBe(facilityId)
        expect(url.searchParams.get('userId')).toBe(userId)

        return HttpResponse.json({
          data: [
            {
              id: areaId,
              facilityId,
              name: '3F East',
              currentWeekRule: weekRule,
              memberCount: 1,
              spotCount: 2,
              version: 1,
            },
          ],
          meta: { limit: 20, hasNext: false, nextCursor: null },
          links: { self: '/api/v1/cleaning-areas' },
        })
      }),
      http.get('/api/v1/cleaning-areas/:areaId', ({ params }) => HttpResponse.json({
        data: {
          ...cleaningAreaDetail,
          id: params.areaId,
        },
      }, { headers: { ETag: '"area-v2"' } })),
      http.get('/api/v1/cleaning-areas/:areaId/current-week', ({ params }) => HttpResponse.json({
        data: {
          areaId: params.areaId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          timeZoneId: 'Asia/Tokyo',
        },
      })),
    )

    await expect(listCleaningAreas({ facilityId, userId, sort: 'name', limit: 20 })).resolves.toMatchObject({
      data: [{ id: areaId, name: '3F East' }],
    })

    await expect(getCleaningArea(areaId)).resolves.toMatchObject({
      data: { id: areaId, spots: cleaningAreaDetail.spots },
      etag: '"area-v2"',
    })

    await expect(getCleaningAreaCurrentWeek(areaId)).resolves.toMatchObject({
      data: { areaId, weekId: '2026-W10', weekLabel: '2026/3/2 週' },
    })
  })

  it('creates cleaning areas and spots using generated ids', async () => {
    const randomUuidSpy = vi
      .spyOn(globalThis.crypto, 'randomUUID')
      .mockReturnValueOnce(areaId)
      .mockReturnValueOnce(spotId)
      .mockReturnValueOnce('00000000-0000-0000-0000-000000000022')

    server.use(
      http.post('/api/v1/cleaning-areas', async ({ request }) => {
        expect(await request.json()).toEqual({
          facilityId,
          areaId,
          name: '3F East',
          initialWeekRule: {
            startDay: 'monday',
            startTime: '09:00:00',
            timeZoneId: 'Asia/Tokyo',
            effectiveFromWeek: '2026-W10',
          },
          initialSpots: [
            { spotId, spotName: 'Pantry', sortOrder: 10 },
            { spotId: '00000000-0000-0000-0000-000000000022', spotName: 'Meeting Room', sortOrder: 20 },
          ],
        })

        return HttpResponse.json({ data: { areaId } })
      }),
      http.post('/api/v1/cleaning-areas/:areaId/spots', async ({ request, params }) => {
        expect(params.areaId).toBe(areaId)
        expect(request.headers.get('if-match')).toBe('"area-v2"')
        expect(await request.json()).toEqual({
          spotId: '00000000-0000-0000-0000-000000000099',
          name: 'Entry',
          sortOrder: 30,
        })

        return HttpResponse.json({
          data: { spotId: '00000000-0000-0000-0000-000000000099' },
        })
      }),
    )

    await expect(createCleaningArea({
      facilityId,
      name: '3F East',
      initialWeekRule: {
        startDay: 'monday',
        startTime: '09:00:00',
        timeZoneId: 'Asia/Tokyo',
        effectiveFromWeek: '2026-W10',
      },
      initialSpots: [
        { name: 'Pantry', sortOrder: 10 },
        { name: 'Meeting Room', sortOrder: 20 },
      ],
    })).resolves.toMatchObject({
      data: { data: { areaId } },
    })

    randomUuidSpy.mockReturnValueOnce('00000000-0000-0000-0000-000000000099')

    await expect(addCleaningSpot(areaId, '"area-v2"', {
      name: 'Entry',
      sortOrder: 30,
    })).resolves.toMatchObject({
      data: { data: { spotId: '00000000-0000-0000-0000-000000000099' } },
    })
  })

  it('handles area member and spot mutations including 204 responses', async () => {
    server.use(
      http.post('/api/v1/cleaning-areas/:areaId/members', async ({ request, params }) => {
        expect(params.areaId).toBe(areaId)
        expect(request.headers.get('if-match')).toBe('"area-v2"')
        expect(await request.json()).toEqual({ userId })

        return HttpResponse.json({ data: { userId } })
      }),
      http.delete('/api/v1/cleaning-areas/:areaId/spots/:spotId', ({ request, params }) => {
        expect(params.areaId).toBe(areaId)
        expect(params.spotId).toBe(spotId)
        expect(request.headers.get('if-match')).toBe('"area-v2"')

        return new HttpResponse(null, {
          status: 204,
          headers: { ETag: '"area-v3"' },
        })
      }),
      http.delete('/api/v1/cleaning-areas/:areaId/members/:userId', ({ request, params }) => {
        expect(params.areaId).toBe(areaId)
        expect(params.userId).toBe(userId)
        expect(request.headers.get('if-match')).toBe('"area-v3"')

        return new HttpResponse(null, {
          status: 204,
          headers: { ETag: '"area-v4"' },
        })
      }),
      http.put('/api/v1/cleaning-areas/:areaId/pending-week-rule', async ({ request, params }) => {
        expect(params.areaId).toBe(areaId)
        expect(request.headers.get('if-match')).toBe('"area-v4"')
        expect(await request.json()).toEqual({
          startDay: 'tuesday',
          startTime: '10:00:00',
          timeZoneId: 'Asia/Tokyo',
          effectiveFromWeek: '2026-W11',
        })

        return HttpResponse.json({
          data: {
            ...cleaningAreaDetail,
            id: params.areaId,
            pendingWeekRule: {
              startDay: 'tuesday',
              startTime: '10:00:00',
              timeZoneId: 'Asia/Tokyo',
              effectiveFromWeek: '2026-W11',
            },
          },
        }, { headers: { ETag: '"area-v5"' } })
      }),
    )

    await expect(assignUserToArea(areaId, '"area-v2"', userId)).resolves.toMatchObject({
      data: { data: { userId } },
    })

    await expect(removeCleaningSpot(areaId, spotId, '"area-v2"')).resolves.toMatchObject({
      data: {},
      etag: '"area-v3"',
    })

    await expect(unassignUserFromArea(areaId, userId, '"area-v3"')).resolves.toMatchObject({
      data: {},
      etag: '"area-v4"',
    })

    await expect(scheduleWeekRule(areaId, '"area-v4"', {
      startDay: 'tuesday',
      startTime: '10:00:00',
      timeZoneId: 'Asia/Tokyo',
      effectiveFromWeek: '2026-W11',
    })).resolves.toMatchObject({
      data: {
        id: areaId,
        pendingWeekRule: {
          startDay: 'tuesday',
          startTime: '10:00:00',
          timeZoneId: 'Asia/Tokyo',
          effectiveFromWeek: '2026-W11',
        },
      },
      etag: '"area-v5"',
    })
  })

  it('lists, fetches, generates, and publishes weekly duty plans', async () => {
    server.use(
      http.get('/api/v1/weekly-duty-plans', ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get('areaId')).toBe(areaId)
        expect(url.searchParams.get('weekId')).toBe('2026-W10')
        expect(url.searchParams.get('status')).toBe('published')

        return HttpResponse.json({
          data: [
            {
              id: planId,
              areaId,
              weekId: '2026-W10',
              weekLabel: '2026/3/2 週',
              revision: 1,
              status: 'published',
              version: 1,
            },
          ],
          meta: { limit: 10, hasNext: false, nextCursor: null },
          links: { self: '/api/v1/weekly-duty-plans' },
        })
      }),
      http.get('/api/v1/weekly-duty-plans/:planId', ({ params }) => HttpResponse.json({
        data: {
          id: params.planId,
          areaId,
          weekId: '2026-W10',
          weekLabel: '2026/3/2 週',
          revision: 1,
          status: 'draft',
          version: 1,
          assignmentPolicy: { fairnessWindowWeeks: 4 },
          assignments: [
            {
              spotId,
              userId,
              user: {
                userId,
                employeeNumber: '123456',
                displayName: 'Hanako',
                departmentCode: 'OPS',
                lifecycleStatus: 'active',
              },
            },
          ],
          offDutyEntries: [],
        },
      }, { headers: { ETag: '"plan-v1"' } })),
      http.post('/api/v1/weekly-duty-plans', async ({ request }) => {
        expect(await request.json()).toEqual({
          areaId,
          weekId: '2026-W10',
          policy: { fairnessWindowWeeks: 4 },
        })

        return HttpResponse.json({
          data: {
            planId,
            weekId: '2026-W10',
            weekLabel: '2026/3/2 週',
          },
        })
      }),
      http.put('/api/v1/weekly-duty-plans/:planId/publication', ({ request, params }) => {
        expect(params.planId).toBe(planId)
        expect(request.headers.get('if-match')).toBe('"plan-v1"')

        return HttpResponse.json({
          data: {
            planId,
            status: 'published',
          },
        })
      }),
    )

    await expect(listWeeklyDutyPlans({ areaId, weekId: '2026-W10', status: 'published', limit: 10 })).resolves.toMatchObject({
      data: [{ id: planId, status: 'published' }],
    })

    await expect(getWeeklyDutyPlan(planId)).resolves.toMatchObject({
      data: { id: planId, assignmentPolicy: { fairnessWindowWeeks: 4 } },
      etag: '"plan-v1"',
    })

    await expect(generateWeeklyDutyPlan(areaId, '2026-W10', { fairnessWindowWeeks: 4 })).resolves.toMatchObject({
      data: { data: { planId, weekId: '2026-W10', weekLabel: '2026/3/2 週' } },
    })

    await expect(publishWeeklyDutyPlan(planId, '"plan-v1"')).resolves.toMatchObject({
      data: { data: { planId, status: 'published' } },
    })
  })

  it('surfaces structured and fallback api errors', async () => {
    server.use(
      http.get('/api/v1/users/:userId', () => HttpResponse.json({
        error: {
          code: 'NotFound',
          message: 'ManagedUser was not found.',
          details: [{ field: 'userId', message: 'User does not exist.', code: 'Missing' }],
        },
      }, { status: 404 })),
    )

    await expect(getUser(userId)).rejects.toBeInstanceOf(ApiError)
    await expect(getUser(userId)).rejects.toMatchObject({
      status: 404,
      code: 'NotFound',
      details: [{ field: 'userId', message: 'User does not exist.', code: 'Missing' }],
    })

    server.use(
      http.get('/api/v1/facilities/:facilityId', () => new HttpResponse('boom', {
        status: 500,
        statusText: 'Server Error',
        headers: { 'Content-Type': 'text/plain' },
      })),
    )

    await expect(getFacility(facilityId)).rejects.toMatchObject({
      status: 500,
      code: 'UnknownError',
      message: 'Server Error',
    })
  })

  it('provides helper resolutions for labels, tones, and spot names', () => {
    expect(explainApiError(new ApiError(409, 'RepositoryConcurrency', 'Version mismatch'))).toBe(
      '最新状態に更新されました。内容を確認して再度操作してください。',
    )
    expect(explainApiError(new ApiError(409, 'DuplicateAreaMemberError', 'Conflict'))).toBe(
      'このユーザーはすでに担当エリアに割り当てられています。',
    )
    expect(explainApiError(new ApiError(409, 'UserAlreadyAssignedToAnotherAreaError', 'Conflict'))).toBe(
      'このユーザーは別の担当エリアに割り当て済みです。割り当てを解除してから再度操作してください。',
    )
    expect(explainApiError(new ApiError(409, 'DuplicateCleaningSpotError', 'Conflict'))).toBe(
      '同じ名前の掃除スポットがすでに存在します。別の名前で登録してください。',
    )
    expect(explainApiError(new ApiError(409, 'UnknownConflict', 'Conflict fallback message', [
      { field: 'name', message: '詳細メッセージ', code: 'Conflict' },
    ]))).toBe('詳細メッセージ')
    expect(explainApiError(new ApiError(409, 'UnknownConflict', 'Conflict fallback message'))).toBe('Conflict fallback message')
    expect(explainApiError(new ApiError(400, 'ValidationError', 'Invalid', [
      { field: 'name', message: 'Name is required.', code: 'Required' },
    ]))).toBe('Name is required.')
    expect(explainApiError(new ApiError(404, 'NotFound', 'ManagedUser was not found.'))).toBe('ManagedUser was not found.')
    expect(explainApiError(new ApiError(500, 'InternalServerError', 'Internal Server Error'))).toBe('Internal Server Error')
    expect(explainApiError(new Error('network'))).toBe('通信に失敗しました。時間をおいて再試行してください。')

    expect(resolveSpotName(cleaningAreaDetail, spotId)).toBe('Pantry')
    expect(resolveSpotName(cleaningAreaDetail, 'missing-spot')).toBe('missing-spot')
    expect(resolveWeekRuleDraft(weekRule)).toEqual({
      startDay: 'monday',
      startTime: '09:00:00',
      timeZoneId: 'Asia/Tokyo',
      effectiveFromWeek: '2026-W10',
    })
    expect(resolvePlanStatusLabel('published')).toBe('公開済み')
    expect(resolvePlanStatusLabel('closed')).toBe('完了')
    expect(resolvePlanStatusLabel('draft')).toBe('下書き')
    expect(resolveWeekLabel({ weekId: '2026-W10', weekLabel: '2026/3/2 週' })).toBe('2026/3/2 週')
    expect(resolveWeekLabel({ weekId: '2026-W10' })).toBe('2026-W10')
    expect(resolveEffectiveFromWeekLabel(weekRule)).toBe('2026/3/2 週')
    expect(resolveEffectiveFromWeekLabel({ effectiveFromWeek: '2026-W11' })).toBe('2026-W11')
    expect(resolveLifecycleTone('active')).toBe('positive')
    expect(resolveLifecycleTone('published')).toBe('positive')
    expect(resolveLifecycleTone('inactive')).toBe('muted')
    expect(resolveLifecycleTone('archived')).toBe('muted')
    expect(resolveLifecycleTone('closed')).toBe('muted')
    expect(resolveLifecycleTone('suspended')).toBe('warning')
    expect(resolveLifecycleTone('pendingActivation')).toBe('default')
  })
})
