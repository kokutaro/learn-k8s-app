import { z } from 'zod'
import {
  apiEnvelopeSchema,
  apiErrorSchema,
  cleaningAreaCurrentWeekSchema,
  cleaningAreaDetailSchema,
  cleaningAreaFormSchema,
  cleaningAreaSummarySchema,
  cursorPageSchema,
  facilityDetailSchema,
  facilityEditSchema,
  facilityFormSchema,
  facilitySummarySchema,
  guidSchema,
  planGenerateSchema,
  userCreateSchema,
  userDetailSchema,
  userEditSchema,
  userSummarySchema,
  weeklyDutyPlanDetailSchema,
  weeklyDutyPlanSummarySchema,
  weekRuleFormSchema,
  spotFormSchema,
  type CleaningAreaDetail,
  type WeekRule,
} from './contracts'

export class ApiError extends Error {
  status: number
  code: string
  details: Array<{ field?: string | null; message: string; code?: string | null }>

  constructor(status: number, code: string, message: string, details: Array<{ field?: string | null; message: string; code?: string | null }> = []) {
    super(message)
    this.status = status
    this.code = code
    this.details = details
  }
}

export type MutationResource<T> = {
  data: T
  etag: string | null
  location: string | null
}

export type CursorPage<T> = {
  data: T[]
  meta: {
    limit: number
    hasNext: boolean
    nextCursor: string | null
  }
  links: {
    self: string
  }
}

const apiBase = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'

const conflictMessageByCode: Record<string, string> = {
  RepositoryConcurrency: '最新状態に更新されました。内容を確認して再度操作してください。',
  RepositoryDuplicate: '重複するデータが検出されました。内容を確認して再度操作してください。',
  DuplicateAreaMemberError: 'このユーザーはすでに担当エリアに割り当てられています。',
  UserAlreadyAssignedToAnotherAreaError: 'このユーザーは別の担当エリアに割り当て済みです。割り当てを解除してから再度操作してください。',
  DuplicateCleaningSpotError: '同じ名前の掃除スポットがすでに存在します。別の名前で登録してください。',
  WeeklyPlanAlreadyExists: 'この週の清掃計画はすでに作成されています。',
  DuplicateEmployeeNumberError: '同じ社員番号のユーザーはすでに登録されています。',
  DuplicateFacilityCodeError: '同じ施設コードの施設はすでに登録されています。',
  DuplicateAuthIdentityLinkError: 'この認証アカウントはすでに別のユーザーに紐付けられています。',
  ManagedUserAlreadyArchivedError: 'アーカイブ済みのユーザーは更新できません。',
  ManagedUserNotActiveError: 'このユーザーは現在利用できません。',
  FacilityNotActiveError: 'この施設は現在利用できません。',
  WeekAlreadyClosedError: 'クローズ済みの週次計画は操作できません。',
  CleaningAreaHasNoSpotError: '清掃箇所が登録されていないため計画を作成できません。',
  NoAvailableUserForSpotError: '割り当て可能なユーザーがいません。担当メンバーを確認してください。',
  InvalidRebalanceRequestError: '再配分の内容が不正です。条件を確認してください。',
  InvalidTransferRequest: '転送リクエストの内容が不正です。',
}

function toQueryString(params: Record<string, string | number | undefined>) {
  const search = new URLSearchParams()
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== '') {
      search.set(key, String(value))
    }
  })
  const query = search.toString()
  return query ? `?${query}` : ''
}

async function parseError(response: Response) {
  const json = await response.json().catch(() => null)
  const parsed = apiErrorSchema.safeParse(json)
  if (!parsed.success) {
    throw new ApiError(response.status, 'UnknownError', response.statusText)
  }

  throw new ApiError(
    response.status,
    parsed.data.error.code,
    parsed.data.error.message,
    parsed.data.error.details ?? [],
  )
}

async function request<TSchema extends z.ZodTypeAny>(
  path: string,
  schema: TSchema,
  init?: RequestInit,
): Promise<MutationResource<z.infer<TSchema>>> {
  const headers = new Headers(init?.headers)
  if (!headers.has('Accept')) {
    headers.set('Accept', 'application/json')
  }

  if (init?.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers,
  })

  if (!response.ok) {
    await parseError(response)
  }

  if (response.status === 204) {
    return {
      data: schema.parse({}),
      etag: response.headers.get('ETag'),
      location: response.headers.get('Location'),
    }
  }

  const json = await response.json()
  return {
    data: schema.parse(json),
    etag: response.headers.get('ETag'),
    location: response.headers.get('Location'),
  }
}

function noneSchema() {
  return z.object({})
}

export const queryKeys = {
  facilities: (search: Record<string, unknown>) => ['facilities', search] as const,
  facility: (facilityId: string) => ['facility', facilityId] as const,
  users: (search: Record<string, unknown>) => ['users', search] as const,
  user: (userId: string) => ['user', userId] as const,
  cleaningAreas: (search: Record<string, unknown>) => ['cleaningAreas', search] as const,
  cleaningArea: (areaId: string) => ['cleaningArea', areaId] as const,
  cleaningAreaCurrentWeek: (areaId: string) => ['cleaningAreaCurrentWeek', areaId] as const,
  weeklyDutyPlans: (search: Record<string, unknown>) => ['weeklyDutyPlans', search] as const,
  weeklyDutyPlan: (planId: string) => ['weeklyDutyPlan', planId] as const,
}

export type ListSearch = {
  query?: string
  status?: string
  sort?: string
  cursor?: string
  limit?: number
}

export type AreaSearch = {
  facilityId?: string
  userId?: string
  sort?: string
  cursor?: string
  limit?: number
}

export type PlanSearch = {
  areaId?: string
  weekId?: string
  status?: string
  sort?: string
  cursor?: string
  limit?: number
}

export async function listFacilities(search: ListSearch) {
  return request(
    `/facilities${toQueryString(search)}`,
    cursorPageSchema(facilitySummarySchema),
  ).then((response) => response.data)
}

export function getFacility(facilityId: string) {
  return request(`/facilities/${facilityId}`, apiEnvelopeSchema(facilityDetailSchema)).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export function createFacility(values: z.infer<typeof facilityFormSchema>) {
  const body = facilityFormSchema.parse(values)
  return request('/facilities', z.object({ data: z.object({ facilityId: z.string() }) }), {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function updateFacility(facilityId: string, etag: string, values: z.infer<typeof facilityEditSchema>) {
  const body = facilityEditSchema.parse(values)
  return request(`/facilities/${facilityId}`, z.object({ data: z.object({ facilityId: z.string(), version: z.number() }) }), {
    method: 'PUT',
    headers: { 'If-Match': etag },
    body: JSON.stringify(body),
  })
}

export function changeFacilityActivation(facilityId: string, etag: string, lifecycleStatus: 'active' | 'inactive') {
  return request(`/facilities/${facilityId}/activation`, z.object({ data: z.object({ facilityId: z.string(), lifecycleStatus: z.string(), version: z.number() }) }), {
    method: 'PUT',
    headers: { 'If-Match': etag },
    body: JSON.stringify({ lifecycleStatus }),
  })
}

export async function listUsers(search: ListSearch) {
  return request(`/users${toQueryString(search)}`, cursorPageSchema(userSummarySchema)).then((response) => response.data)
}

export function getUser(userId: string) {
  return request(`/users/${userId}`, apiEnvelopeSchema(userDetailSchema)).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export function createUser(values: z.infer<typeof userCreateSchema>) {
  const body = userCreateSchema.parse(values)
  return request('/users', z.object({ data: z.object({ userId: guidSchema }) }), {
    method: 'POST',
    body: JSON.stringify({
      ...body,
      registrationSource: 'adminPortal',
      emailAddress: body.emailAddress || undefined,
      departmentCode: body.departmentCode || undefined,
    }),
  })
}

export function updateUser(userId: string, etag: string, values: z.infer<typeof userEditSchema>) {
  const body = userEditSchema.parse(values)
  return request(`/users/${userId}`, z.object({ data: z.object({ userId: guidSchema, version: z.number() }) }), {
    method: 'PATCH',
    headers: { 'If-Match': etag },
    body: JSON.stringify({
      ...body,
      emailAddress: body.emailAddress || undefined,
      departmentCode: body.departmentCode || undefined,
    }),
  })
}

export function changeUserLifecycle(userId: string, etag: string, lifecycleStatus: 'active' | 'suspended' | 'archived' | 'pendingActivation') {
  return request(`/users/${userId}/lifecycle`, z.object({ data: z.object({ userId: guidSchema, lifecycleStatus: z.string(), version: z.number() }) }), {
    method: 'POST',
    headers: { 'If-Match': etag },
    body: JSON.stringify({ lifecycleStatus }),
  })
}

export async function listCleaningAreas(search: AreaSearch) {
  return request(`/cleaning-areas${toQueryString(search)}`, cursorPageSchema(cleaningAreaSummarySchema)).then((response) => response.data)
}

export function getCleaningArea(areaId: string) {
  return request(`/cleaning-areas/${areaId}`, apiEnvelopeSchema(cleaningAreaDetailSchema)).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export function getCleaningAreaCurrentWeek(areaId: string) {
  return request(`/cleaning-areas/${areaId}/current-week`, apiEnvelopeSchema(cleaningAreaCurrentWeekSchema)).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export function createCleaningArea(values: z.infer<typeof cleaningAreaFormSchema>) {
  const body = cleaningAreaFormSchema.parse(values)
  return request('/cleaning-areas', z.object({ data: z.object({ areaId: guidSchema }) }), {
    method: 'POST',
    body: JSON.stringify({
      facilityId: body.facilityId,
      areaId: crypto.randomUUID(),
      name: body.name,
      initialWeekRule: body.initialWeekRule,
      initialSpots: body.initialSpots.map((spot) => ({
        spotId: crypto.randomUUID(),
        spotName: spot.name,
        sortOrder: spot.sortOrder,
      })),
    }),
  })
}

export function addCleaningSpot(areaId: string, etag: string, values: z.infer<typeof spotFormSchema>) {
  const body = spotFormSchema.parse(values)
  return request(`/cleaning-areas/${areaId}/spots`, z.object({ data: z.object({ spotId: guidSchema }) }), {
    method: 'POST',
    headers: { 'If-Match': etag },
    body: JSON.stringify({
      spotId: crypto.randomUUID(),
      name: body.name,
      sortOrder: body.sortOrder,
    }),
  })
}

export function removeCleaningSpot(areaId: string, spotId: string, etag: string) {
  return request(`/cleaning-areas/${areaId}/spots/${spotId}`, noneSchema(), {
    method: 'DELETE',
    headers: { 'If-Match': etag },
  })
}

export function assignUserToArea(areaId: string, etag: string, userId: string) {
  return request(`/cleaning-areas/${areaId}/members`, z.object({ data: z.object({ userId: guidSchema }) }), {
    method: 'POST',
    headers: { 'If-Match': etag },
    body: JSON.stringify({ userId }),
  })
}

export function unassignUserFromArea(areaId: string, userId: string, etag: string) {
  return request(`/cleaning-areas/${areaId}/members/${userId}`, noneSchema(), {
    method: 'DELETE',
    headers: { 'If-Match': etag },
  })
}

export function scheduleWeekRule(areaId: string, etag: string, values: z.infer<typeof weekRuleFormSchema>) {
  const body = weekRuleFormSchema.parse(values)
  return request(`/cleaning-areas/${areaId}/pending-week-rule`, apiEnvelopeSchema(cleaningAreaDetailSchema), {
    method: 'PUT',
    headers: { 'If-Match': etag },
    body: JSON.stringify(body),
  }).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export async function listWeeklyDutyPlans(search: PlanSearch) {
  return request(`/weekly-duty-plans${toQueryString(search)}`, cursorPageSchema(weeklyDutyPlanSummarySchema)).then((response) => response.data)
}

export function getWeeklyDutyPlan(planId: string) {
  return request(`/weekly-duty-plans/${planId}`, apiEnvelopeSchema(weeklyDutyPlanDetailSchema)).then((response) => ({
    ...response,
    data: response.data.data,
  }))
}

export function generateWeeklyDutyPlan(areaId: string, weekId: string, values: z.infer<typeof planGenerateSchema>) {
  const body = planGenerateSchema.parse(values)
  return request('/weekly-duty-plans', z.object({ data: z.object({ planId: guidSchema, weekId: z.string(), weekLabel: z.string().optional() }) }), {
    method: 'POST',
    body: JSON.stringify({
      areaId,
      weekId,
      policy: { fairnessWindowWeeks: body.fairnessWindowWeeks },
    }),
  })
}

export function publishWeeklyDutyPlan(planId: string, etag: string) {
  return request(`/weekly-duty-plans/${planId}/publication`, z.object({ data: z.object({ planId: guidSchema, status: z.string() }) }), {
    method: 'PUT',
    headers: { 'If-Match': etag },
  })
}

export function explainApiError(error: unknown) {
  const networkFallbackMessage = '通信に失敗しました。時間をおいて再試行してください。'

  if (error instanceof ApiError) {
    if (error.status === 409) {
      const knownConflictMessage = conflictMessageByCode[error.code]
      if (knownConflictMessage) {
        return knownConflictMessage
      }

      return error.details[0]?.message || error.message || networkFallbackMessage
    }

    return error.details[0]?.message ?? error.message
  }

  return networkFallbackMessage
}

export function resolveSpotName(area: CleaningAreaDetail, spotId: string) {
  return area.spots.find((spot) => spot.id === spotId)?.name ?? spotId
}

export function resolveWeekRuleDraft(rule: WeekRule) {
  return {
    startDay: rule.startDay,
    startTime: rule.startTime,
    timeZoneId: rule.timeZoneId,
    effectiveFromWeek: rule.effectiveFromWeek,
  }
}

export function resolvePlanStatusLabel(status: string) {
  return status === 'published'
    ? '公開済み'
    : status === 'closed'
      ? '完了'
      : '下書き'
}

export function resolveWeekLabel<T extends { weekId: string; weekLabel?: string | null }>(value: T) {
  return value.weekLabel || value.weekId
}

export function resolveEffectiveFromWeekLabel(rule: { effectiveFromWeek: string; effectiveFromWeekLabel?: string | null }) {
  return rule.effectiveFromWeekLabel || rule.effectiveFromWeek
}

export function resolveLifecycleTone(status: string) {
  switch (status) {
    case 'active':
    case 'published':
      return 'positive'
    case 'inactive':
    case 'archived':
    case 'closed':
      return 'muted'
    case 'suspended':
      return 'warning'
    default:
      return 'default'
  }
}
