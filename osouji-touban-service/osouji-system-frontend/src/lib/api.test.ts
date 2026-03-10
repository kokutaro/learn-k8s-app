import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../test/server'
import { ApiError, assignUserToArea, getUser, listFacilities, updateFacility, updateUser } from './api'

describe('api client', () => {
  it('parses paged facility responses', async () => {
    server.use(
      http.get('/api/v1/facilities', () => HttpResponse.json({
        data: [
          {
            id: '11111111-1111-1111-1111-111111111111',
            facilityCode: 'TOKYO-HQ',
            name: 'Tokyo HQ',
            timeZoneId: 'Asia/Tokyo',
            lifecycleStatus: 'active',
            version: 1,
          },
        ],
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/facilities',
        },
      })),
    )

    const response = await listFacilities({ status: 'active', sort: 'name', limit: 20 })

    expect(response.data).toHaveLength(1)
    expect(response.data[0]?.name).toBe('Tokyo HQ')
  })

  it('accepts guid values that are not RFC 4122 versioned uuids', async () => {
    server.use(
      http.get('/api/v1/facilities', () => HttpResponse.json({
        data: [
          {
            id: '00000000-0000-0000-0000-000000000001',
            facilityCode: 'OSAKA-01',
            name: 'Osaka Office',
            timeZoneId: 'Asia/Tokyo',
            lifecycleStatus: 'active',
            version: 2,
          },
        ],
        meta: {
          limit: 20,
          hasNext: false,
          nextCursor: null,
        },
        links: {
          self: '/api/v1/facilities',
        },
      })),
    )

    const response = await listFacilities({ sort: 'name', limit: 20 })

    expect(response.data[0]?.id).toBe('00000000-0000-0000-0000-000000000001')
  })

  it('surfaces api errors with status and message', async () => {
    server.use(
      http.get('/api/v1/users/:userId', () => HttpResponse.json({
        error: {
          code: 'NotFound',
          message: 'ManagedUser was not found.',
        },
      }, { status: 404 })),
    )

    await expect(getUser('11111111-1111-1111-1111-111111111111')).rejects.toBeInstanceOf(ApiError)
    await expect(getUser('11111111-1111-1111-1111-111111111111')).rejects.toMatchObject({
      status: 404,
      code: 'NotFound',
    })
  })

  it('sends application/json for put requests with if-match headers', async () => {
    server.use(
      http.put('/api/v1/facilities/:facilityId', ({ request, params }) => {
        if (!request.headers.get('content-type')?.startsWith('application/json')) {
          return HttpResponse.json({
            error: { code: 'UnsupportedMediaType', message: 'Expected application/json.' },
          }, { status: 415 })
        }

        if (request.headers.get('if-match') !== '"facility-v1"') {
          return HttpResponse.json({
            error: { code: 'PreconditionRequired', message: 'Missing If-Match.' },
          }, { status: 428 })
        }

        return HttpResponse.json({
          data: {
            facilityId: params.facilityId,
            version: 2,
          },
        })
      }),
    )

    await expect(updateFacility('00000000-0000-0000-0000-000000000010', '"facility-v1"', {
      name: 'Updated Facility',
      description: 'Revised',
      timeZoneId: 'Asia/Tokyo',
    })).resolves.toMatchObject({
      data: {
        data: {
          facilityId: '00000000-0000-0000-0000-000000000010',
          version: 2,
        },
      },
    })
  })

  it('sends application/json for patch requests with if-match headers', async () => {
    server.use(
      http.patch('/api/v1/users/:userId', ({ request, params }) => {
        if (!request.headers.get('content-type')?.startsWith('application/json')) {
          return HttpResponse.json({
            error: { code: 'UnsupportedMediaType', message: 'Expected application/json.' },
          }, { status: 415 })
        }

        if (request.headers.get('if-match') !== '"user-v1"') {
          return HttpResponse.json({
            error: { code: 'PreconditionRequired', message: 'Missing If-Match.' },
          }, { status: 428 })
        }

        return HttpResponse.json({
          data: {
            userId: params.userId,
            version: 3,
          },
        })
      }),
    )

    await expect(updateUser('00000000-0000-0000-0000-000000000011', '"user-v1"', {
      displayName: 'Updated User',
      emailAddress: 'updated@example.com',
      departmentCode: 'OPS',
    })).resolves.toMatchObject({
      data: {
        data: {
          userId: '00000000-0000-0000-0000-000000000011',
          version: 3,
        },
      },
    })
  })

  it('sends application/json for post requests with if-match headers', async () => {
    server.use(
      http.post('/api/v1/cleaning-areas/:areaId/members', ({ request }) => {
        if (!request.headers.get('content-type')?.startsWith('application/json')) {
          return HttpResponse.json({
            error: { code: 'UnsupportedMediaType', message: 'Expected application/json.' },
          }, { status: 415 })
        }

        if (request.headers.get('if-match') !== '"area-v1"') {
          return HttpResponse.json({
            error: { code: 'PreconditionRequired', message: 'Missing If-Match.' },
          }, { status: 428 })
        }

        return HttpResponse.json({
          data: {
            userId: '00000000-0000-0000-0000-000000000012',
          },
        })
      }),
    )

    await expect(assignUserToArea(
      '00000000-0000-0000-0000-000000000020',
      '"area-v1"',
      '00000000-0000-0000-0000-000000000012',
    )).resolves.toMatchObject({
      data: {
        data: {
          userId: '00000000-0000-0000-0000-000000000012',
        },
      },
    })
  })
})
