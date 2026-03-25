import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { z } from 'zod'
import {
  addCleaningSpot,
  assignUserToArea,
  createCleaningArea,
  explainApiError,
  getCleaningArea,
  listCleaningAreas,
  listFacilities,
  listUsers,
  queryKeys,
  resolveEffectiveFromWeekLabel,
  removeCleaningSpot,
  resolveWeekRuleDraft,
  scheduleWeekRule,
  unassignUserFromArea,
} from '../../lib/api'
import {
  cleaningAreaFormSchema,
  guidSchema,
  spotFormSchema,
  weekRuleFormSchema,
} from '../../lib/contracts'
import { currentIsoWeekId } from '../../lib/date'
import {
  Banner,
  Button,
  DataTable,
  EmptyState,
  Field,
  GlassPanel,
  MetricChip,
  Modal,
  PageHeader,
  SectionCard,
  SelectInput,
  StackedFieldRow,
  StatusBadge,
  TextInput,
} from '../../components/ui'
import { preserveScrollNavigateOptions } from '../../lib/navigation'

const searchSchema = z.object({
  facilityId: guidSchema.optional(),
  userId: guidSchema.optional(),
  areaId: guidSchema.optional(),
  sort: z.enum(['name', '-name']).optional().default('name'),
  cursor: z.string().optional(),
  limit: z.coerce.number().optional().default(20),
})

type Feedback = { kind: 'success' | 'error'; message: string } | null

type AreaFormState = {
  facilityId: string
  name: string
  initialWeekRule: {
    startDay: 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday'
    startTime: string
    timeZoneId: string
    effectiveFromWeek: string
  }
  initialSpots: Array<{ name: string; sortOrder: number }>
}

type SpotFormState = {
  name: string
  sortOrder: number
}

type WeekRuleFormState = {
  startDay: 'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday'
  startTime: string
  timeZoneId: string
  effectiveFromWeek: string
}

const defaultAreaForm = (): AreaFormState => ({
  facilityId: '',
  name: '',
  initialWeekRule: {
    startDay: 'monday',
    startTime: '09:00:00',
    timeZoneId: 'Asia/Tokyo',
    effectiveFromWeek: currentIsoWeekId(),
  },
  initialSpots: [{ name: '', sortOrder: 10 }],
})

const defaultSpotForm: SpotFormState = {
  name: '',
  sortOrder: 10,
}

export const Route = createFileRoute('/_app/cleaning-areas')({
  validateSearch: (search) => searchSchema.parse(search),
  component: CleaningAreasPage,
})

function CleaningAreasPage() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const queryClient = useQueryClient()

  const [feedback, setFeedback] = useState<Feedback>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [createForm, setCreateForm] = useState<AreaFormState>(defaultAreaForm)
  const [spotForm, setSpotForm] = useState<SpotFormState>(defaultSpotForm)
  const [assignUserId, setAssignUserId] = useState('')
  const [pendingRuleForm, setPendingRuleForm] = useState<WeekRuleFormState>({
    startDay: 'monday',
    startTime: '09:00:00',
    timeZoneId: 'Asia/Tokyo',
    effectiveFromWeek: currentIsoWeekId(),
  })

  const areasQuery = useQuery({
    queryKey: queryKeys.cleaningAreas(search),
    queryFn: () => listCleaningAreas(search),
  })

  const facilitiesQuery = useQuery({
    queryKey: queryKeys.facilities({ status: 'active', sort: 'name', limit: 100 }),
    queryFn: () => listFacilities({ status: 'active', sort: 'name', limit: 100 }),
  })

  const usersQuery = useQuery({
    queryKey: queryKeys.users({ status: 'active', sort: 'displayName', limit: 100 }),
    queryFn: () => listUsers({ status: 'active', sort: 'displayName', limit: 100 }),
  })

  const areaDetailQuery = useQuery({
    queryKey: search.areaId ? queryKeys.cleaningArea(search.areaId) : ['cleaningArea', 'idle'],
    queryFn: () => getCleaningArea(search.areaId!),
    enabled: Boolean(search.areaId),
  })

  useEffect(() => {
    if (createOpen) {
      const firstFacilityId = facilitiesQuery.data?.data[0]?.id ?? ''
      setCreateForm({
        ...defaultAreaForm(),
        facilityId: firstFacilityId,
      })
    }
  }, [createOpen, facilitiesQuery.data])

  useEffect(() => {
    if (areaDetailQuery.data?.data) {
      setPendingRuleForm(resolveWeekRuleDraft(areaDetailQuery.data.data.pendingWeekRule ?? areaDetailQuery.data.data.currentWeekRule) as WeekRuleFormState)
    }
  }, [areaDetailQuery.data])

  const createMutation = useMutation({
    mutationFn: () => createCleaningArea(createForm),
    onSuccess: async (response) => {
      setFeedback({ kind: 'success', message: '掃除エリアを追加しました。' })
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['cleaningAreas'] })
      void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, areaId: response.data.data.areaId, cursor: undefined }) }))
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const addSpotMutation = useMutation({
    mutationFn: () => addCleaningSpot(search.areaId!, areaDetailQuery.data!.etag!, spotForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '掃除箇所を追加しました。' })
      setSpotForm(defaultSpotForm)
      await invalidateArea()
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const assignUserMutation = useMutation({
    mutationFn: () => assignUserToArea(search.areaId!, areaDetailQuery.data!.etag!, assignUserId),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: 'ユーザーをアサインしました。' })
      setAssignUserId('')
      await invalidateArea()
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const pendingRuleMutation = useMutation({
    mutationFn: () => scheduleWeekRule(search.areaId!, areaDetailQuery.data!.etag!, pendingRuleForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '週ルール変更を予約しました。' })
      await invalidateArea()
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const page = areasQuery.data
  const area = areaDetailQuery.data?.data

  async function invalidateArea() {
    await queryClient.invalidateQueries({ queryKey: ['cleaningAreas'] })
    if (search.areaId) {
      await queryClient.invalidateQueries({ queryKey: queryKeys.cleaningArea(search.areaId) })
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="掃除エリア管理"
        description="施設配下に掃除エリアを作成し、掃除箇所と担当ユーザーを管理します。"
        action={<Button onClick={() => setCreateOpen(true)}>掃除エリアを追加</Button>}
      />

      {feedback ? <Banner kind={feedback.kind} message={feedback.message} /> : null}

      <div className="grid gap-6 xl:grid-cols-[440px_1fr]">
        <GlassPanel className="space-y-4">
          <StackedFieldRow>
            <Field label="施設">
              <SelectInput
                value={search.facilityId ?? ''}
                onChange={(event) => {
                  void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, facilityId: event.target.value || undefined, cursor: undefined }) }))
                }}
              >
                <option value="">すべて</option>
                {facilitiesQuery.data?.data.map((facility) => (
                  <option key={facility.id} value={facility.id}>{facility.name}</option>
                ))}
              </SelectInput>
            </Field>
            <Field label="ユーザー所属">
              <SelectInput
                value={search.userId ?? ''}
                onChange={(event) => {
                  void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, userId: event.target.value || undefined, cursor: undefined }) }))
                }}
              >
                <option value="">すべて</option>
                {usersQuery.data?.data.map((user) => (
                  <option key={user.userId} value={user.userId}>{user.displayName}</option>
                ))}
              </SelectInput>
            </Field>
          </StackedFieldRow>

          {page && page.data.length > 0 ? (
            <div className="space-y-3">
              {page.data.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={`w-full rounded-[1.5rem] border px-4 py-4 text-left transition ${search.areaId === item.id ? 'border-teal-300 bg-white/85 shadow-lg' : 'border-white/60 bg-white/45 hover:bg-white/70'}`}
                  onClick={() => {
                    void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, areaId: item.id }) }))
                  }}
                >
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <div className="text-lg font-bold text-slate-900">{item.name}</div>
                      <div className="mt-2 text-xs uppercase tracking-[0.18em] text-slate-500">
                        {item.currentWeekRule.timeZoneId}
                      </div>
                    </div>
                    <StatusBadge label={`${item.memberCount}名`} />
                  </div>
                  <div className="mt-3 flex gap-3 text-sm text-slate-600">
                    <span>{item.spotCount} 箇所</span>
                    <span>{item.currentWeekRule.startDay} / {item.currentWeekRule.startTime}</span>
                  </div>
                </button>
              ))}
            </div>
          ) : null}

          {page && page.data.length === 0 ? (
            <EmptyState title="掃除エリアがありません" message="施設を作成した後、最初の掃除エリアを登録してください。" />
          ) : null}
        </GlassPanel>

        <div className="space-y-6">
          {area ? (
            <>
              <GlassPanel className="space-y-4">
                <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
                  <div>
                    <h2 className="text-3xl font-bold text-slate-900">{area.name}</h2>
                    <p className="mt-2 text-sm text-slate-600">施設 ID: {area.facilityId}</p>
                  </div>
                  <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                    <MetricChip label="掃除箇所" value={area.spots.length} />
                    <MetricChip label="担当者" value={area.members.length} />
                    <MetricChip label="Rotation" value={area.rotationCursor} />
                    <MetricChip label="Version" value={area.version} />
                  </div>
                </div>
              </GlassPanel>

              <SectionCard title="概要">
                <div className="grid gap-4 md:grid-cols-2">
                  <div className="rounded-[1.5rem] bg-white/60 p-4">
                    <div className="text-xs uppercase tracking-[0.18em] text-slate-500">Current Week Rule</div>
                    <div className="mt-3 text-sm text-slate-700">
                      {area.currentWeekRule.startDay} / {area.currentWeekRule.startTime} / {area.currentWeekRule.timeZoneId} / {resolveEffectiveFromWeekLabel(area.currentWeekRule)}
                    </div>
                  </div>
                  <div className="rounded-[1.5rem] bg-white/60 p-4">
                    <div className="text-xs uppercase tracking-[0.18em] text-slate-500">Pending Week Rule</div>
                    <div className="mt-3 text-sm text-slate-700">
                      {area.pendingWeekRule
                        ? `${area.pendingWeekRule.startDay} / ${area.pendingWeekRule.startTime} / ${area.pendingWeekRule.timeZoneId} / ${resolveEffectiveFromWeekLabel(area.pendingWeekRule)}`
                        : '予約なし'}
                    </div>
                  </div>
                </div>
              </SectionCard>

              <SectionCard title="掃除箇所" action={<StatusBadge label={`${area.spots.length} 箇所`} />}>
                <form
                  className="grid gap-4 md:grid-cols-[1fr_160px_auto]"
                  onSubmit={(event) => {
                    event.preventDefault()
                    const parsed = spotFormSchema.safeParse(spotForm)
                    if (!parsed.success) {
                      setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
                      return
                    }

                    addSpotMutation.mutate()
                  }}
                >
                  <Field label="掃除箇所名">
                    <TextInput value={spotForm.name} onChange={(event) => setSpotForm((previous) => ({ ...previous, name: event.target.value }))} />
                  </Field>
                  <Field label="並び順">
                    <TextInput type="number" value={spotForm.sortOrder} onChange={(event) => setSpotForm((previous) => ({ ...previous, sortOrder: Number(event.target.value) }))} />
                  </Field>
                  <div className="flex items-end">
                    <Button type="submit" disabled={addSpotMutation.isPending}>追加</Button>
                  </div>
                </form>

                <DataTable
                  headers={['掃除箇所', '並び順', '操作']}
                  columnClassNames={['min-w-[12rem]', 'min-w-[8rem]', 'min-w-[8rem]']}
                >
                  {area.spots.map((spot) => (
                    <tr key={spot.id}>
                      <td className="px-4 py-4 font-semibold text-slate-900">{spot.name}</td>
                      <td className="px-4 py-4 text-sm text-slate-600">{spot.sortOrder}</td>
                      <td className="px-4 py-4">
                        <Button
                          tone="danger"
                          onClick={() => {
                            void removeCleaningSpot(search.areaId!, spot.id, areaDetailQuery.data!.etag!)
                              .then(async () => {
                                setFeedback({ kind: 'success', message: '掃除箇所を削除しました。' })
                                await invalidateArea()
                              })
                              .catch((error) => setFeedback({ kind: 'error', message: explainApiError(error) }))
                          }}
                        >
                          削除
                        </Button>
                      </td>
                    </tr>
                  ))}
                </DataTable>
              </SectionCard>

              <SectionCard title="メンバー">
                <form
                  className="grid gap-4 md:grid-cols-[1fr_auto]"
                  onSubmit={(event) => {
                    event.preventDefault()
                    if (!assignUserId) {
                      setFeedback({ kind: 'error', message: 'ユーザーを選択してください。' })
                      return
                    }

                    assignUserMutation.mutate()
                  }}
                >
                  <Field label="アサインするユーザー">
                    <SelectInput value={assignUserId} onChange={(event) => setAssignUserId(event.target.value)}>
                      <option value="">選択してください</option>
                      {usersQuery.data?.data.map((user) => (
                        <option key={user.userId} value={user.userId}>{user.displayName} ({user.employeeNumber})</option>
                      ))}
                    </SelectInput>
                  </Field>
                  <div className="flex items-end">
                    <Button type="submit" disabled={assignUserMutation.isPending}>アサイン</Button>
                  </div>
                </form>

                <DataTable
                  headers={['社員名', '社員番号', '操作']}
                  columnClassNames={['min-w-[12rem]', 'min-w-[8rem]', 'min-w-[8rem]']}
                >
                  {area.members.map((member) => (
                    <tr key={member.id}>
                      <td className="px-4 py-4 font-semibold text-slate-900">{member.displayName || member.employeeNumber}</td>
                      <td className="px-4 py-4 text-sm text-slate-600">{member.employeeNumber}</td>
                      <td className="px-4 py-4">
                        <Button
                          tone="danger"
                          onClick={() => {
                            void unassignUserFromArea(search.areaId!, member.userId, areaDetailQuery.data!.etag!)
                              .then(async () => {
                                setFeedback({ kind: 'success', message: 'メンバー割当を解除しました。' })
                                await invalidateArea()
                              })
                              .catch((error) => setFeedback({ kind: 'error', message: explainApiError(error) }))
                          }}
                        >
                          解除
                        </Button>
                      </td>
                    </tr>
                  ))}
                </DataTable>
              </SectionCard>

              <SectionCard title="週ルール変更">
                <form
                  className="space-y-4"
                  onSubmit={(event) => {
                    event.preventDefault()
                    const parsed = weekRuleFormSchema.safeParse(pendingRuleForm)
                    if (!parsed.success) {
                      setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
                      return
                    }

                    pendingRuleMutation.mutate()
                  }}
                >
                  <StackedFieldRow>
                    <Field label="開始曜日">
                      <SelectInput value={pendingRuleForm.startDay} onChange={(event) => setPendingRuleForm((previous) => ({ ...previous, startDay: event.target.value as typeof previous.startDay }))}>
                        {['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'].map((value) => (
                          <option key={value} value={value}>{value}</option>
                        ))}
                      </SelectInput>
                    </Field>
                    <Field label="開始時刻">
                      <TextInput value={pendingRuleForm.startTime} onChange={(event) => setPendingRuleForm((previous) => ({ ...previous, startTime: event.target.value }))} />
                    </Field>
                  </StackedFieldRow>
                  <StackedFieldRow>
                    <Field label="タイムゾーン">
                      <TextInput value={pendingRuleForm.timeZoneId} onChange={(event) => setPendingRuleForm((previous) => ({ ...previous, timeZoneId: event.target.value }))} />
                    </Field>
                    <Field label="適用開始週">
                      <TextInput value={pendingRuleForm.effectiveFromWeek} onChange={(event) => setPendingRuleForm((previous) => ({ ...previous, effectiveFromWeek: event.target.value }))} />
                    </Field>
                  </StackedFieldRow>
                  <div className="flex justify-end">
                    <Button type="submit" disabled={pendingRuleMutation.isPending}>予約を保存</Button>
                  </div>
                </form>
              </SectionCard>
            </>
          ) : (
            <GlassPanel>
              <EmptyState title="掃除エリアを選択してください" message="左の一覧からエリアを選ぶと、掃除箇所・メンバー・週ルールの編集ができます。" />
            </GlassPanel>
          )}
        </div>
      </div>

      <Modal open={createOpen} title="掃除エリアを追加" onClose={() => setCreateOpen(false)}>
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            const parsed = cleaningAreaFormSchema.safeParse(createForm)
            if (!parsed.success) {
              setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
              return
            }

            createMutation.mutate()
          }}
        >
          <StackedFieldRow>
            <Field label="施設">
              <SelectInput value={createForm.facilityId} onChange={(event) => setCreateForm((previous) => ({ ...previous, facilityId: event.target.value }))}>
                <option value="">選択してください</option>
                {facilitiesQuery.data?.data.map((facility) => (
                  <option key={facility.id} value={facility.id}>{facility.name}</option>
                ))}
              </SelectInput>
            </Field>
            <Field label="エリア名">
              <TextInput value={createForm.name} onChange={(event) => setCreateForm((previous) => ({ ...previous, name: event.target.value }))} />
            </Field>
          </StackedFieldRow>
          <StackedFieldRow>
            <Field label="開始曜日">
              <SelectInput value={createForm.initialWeekRule.startDay} onChange={(event) => setCreateForm((previous) => ({ ...previous, initialWeekRule: { ...previous.initialWeekRule, startDay: event.target.value as typeof previous.initialWeekRule.startDay } }))}>
                {['monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday', 'sunday'].map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </SelectInput>
            </Field>
            <Field label="開始時刻">
              <TextInput value={createForm.initialWeekRule.startTime} onChange={(event) => setCreateForm((previous) => ({ ...previous, initialWeekRule: { ...previous.initialWeekRule, startTime: event.target.value } }))} />
            </Field>
          </StackedFieldRow>
          <StackedFieldRow>
            <Field label="タイムゾーン">
              <TextInput value={createForm.initialWeekRule.timeZoneId} onChange={(event) => setCreateForm((previous) => ({ ...previous, initialWeekRule: { ...previous.initialWeekRule, timeZoneId: event.target.value } }))} />
            </Field>
            <Field label="適用開始週">
              <TextInput value={createForm.initialWeekRule.effectiveFromWeek} onChange={(event) => setCreateForm((previous) => ({ ...previous, initialWeekRule: { ...previous.initialWeekRule, effectiveFromWeek: event.target.value } }))} />
            </Field>
          </StackedFieldRow>
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <h3 className="text-lg font-bold text-slate-900">初期掃除箇所</h3>
              <Button
                tone="secondary"
                onClick={() => {
                  setCreateForm((previous) => ({
                    ...previous,
                    initialSpots: [...previous.initialSpots, { name: '', sortOrder: previous.initialSpots.length * 10 + 10 }],
                  }))
                }}
              >
                箇所を追加
              </Button>
            </div>
            {createForm.initialSpots.map((spot, index) => (
              <StackedFieldRow key={`${index}-${spot.sortOrder}`}>
                <Field label={`掃除箇所 ${index + 1}`}>
                  <TextInput
                    value={spot.name}
                    onChange={(event) => {
                      setCreateForm((previous) => ({
                        ...previous,
                        initialSpots: previous.initialSpots.map((item, itemIndex) => itemIndex === index ? { ...item, name: event.target.value } : item),
                      }))
                    }}
                  />
                </Field>
                <Field label="並び順">
                  <TextInput
                    type="number"
                    value={spot.sortOrder}
                    onChange={(event) => {
                      setCreateForm((previous) => ({
                        ...previous,
                        initialSpots: previous.initialSpots.map((item, itemIndex) => itemIndex === index ? { ...item, sortOrder: Number(event.target.value) } : item),
                      }))
                    }}
                  />
                </Field>
              </StackedFieldRow>
            ))}
          </div>
          <div className="flex justify-end gap-3">
            <Button tone="ghost" onClick={() => setCreateOpen(false)}>キャンセル</Button>
            <Button type="submit" disabled={createMutation.isPending}>保存</Button>
          </div>
        </form>
      </Modal>
    </div>
  )
}
