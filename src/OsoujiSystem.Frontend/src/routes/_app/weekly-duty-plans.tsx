import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'
import { z } from 'zod'
import {
    Banner,
    Button,
    DataTable,
    EmptyState,
    Field,
    GlassPanel,
    MetricChip,
    PageHeader,
    SectionCard,
    SelectInput,
    StackedFieldRow,
    StatusBadge,
    TextInput,
} from '../../components/ui'
import {
    explainApiError,
    generateWeeklyDutyPlan,
    getCleaningArea,
    getCleaningAreaCurrentWeek,
    getWeeklyDutyPlan,
    listCleaningAreas,
    listWeeklyDutyPlans,
    publishWeeklyDutyPlan,
    queryKeys,
    resolveLifecycleTone,
    resolvePlanStatusLabel,
    resolveSpotName,
    resolveWeekLabel,
} from '../../lib/api'
import { guidSchema, planGenerateSchema } from '../../lib/contracts'
import { preserveScrollNavigateOptions } from '../../lib/navigation'

const searchSchema = z.object({
  areaId: guidSchema.optional(),
  weekId: z.string().optional(),
  status: z.enum(['draft', 'published', 'closed']).optional(),
  planId: guidSchema.optional(),
  sort: z.enum(['weekId', '-weekId', 'createdAt', '-createdAt']).optional().default('-weekId'),
  cursor: z.string().optional(),
  limit: z.coerce.number().optional().default(20),
})

type Feedback = { kind: 'success' | 'error'; message: string } | null

export const Route = createFileRoute('/_app/weekly-duty-plans')({
  validateSearch: (search) => searchSchema.parse(search),
  component: WeeklyDutyPlansPage,
})

function WeeklyDutyPlansPage() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const queryClient = useQueryClient()

  const [feedback, setFeedback] = useState<Feedback>(null)
  const [fairnessWindowWeeks, setFairnessWindowWeeks] = useState(4)

  const areasQuery = useQuery({
    queryKey: queryKeys.cleaningAreas({ sort: 'name', limit: 100 }),
    queryFn: () => listCleaningAreas({ sort: 'name', limit: 100 }),
  })

  const currentWeekQuery = useQuery({
    queryKey: search.areaId ? queryKeys.cleaningAreaCurrentWeek(search.areaId) : ['cleaningAreaCurrentWeek', 'idle'],
    queryFn: () => getCleaningAreaCurrentWeek(search.areaId!),
    enabled: Boolean(search.areaId),
  })

  const plansQuery = useQuery({
    queryKey: queryKeys.weeklyDutyPlans(search),
    queryFn: () => listWeeklyDutyPlans(search),
  })

  const selectedPlanId = search.planId ?? plansQuery.data?.data[0]?.id
  const planDetailQuery = useQuery({
    queryKey: selectedPlanId ? queryKeys.weeklyDutyPlan(selectedPlanId) : ['weeklyDutyPlan', 'idle'],
    queryFn: () => getWeeklyDutyPlan(selectedPlanId!),
    enabled: Boolean(selectedPlanId),
  })

  const areaDetailQuery = useQuery({
    queryKey: search.areaId ? queryKeys.cleaningArea(search.areaId) : ['cleaningArea', 'idle'],
    queryFn: () => getCleaningArea(search.areaId!),
    enabled: Boolean(search.areaId),
  })

  const generateMutation = useMutation({
    mutationFn: () => generateWeeklyDutyPlan(search.areaId!, currentWeekQuery.data!.data.weekId, { fairnessWindowWeeks }),
    onSuccess: async (response) => {
      setFeedback({ kind: 'success', message: '今週の清掃計画を作成しました。' })
      await queryClient.invalidateQueries({ queryKey: ['weeklyDutyPlans'] })
      await queryClient.invalidateQueries({ queryKey: ['weeklyDutyPlan'] })
      void navigate(preserveScrollNavigateOptions({
        search: (previous) => ({
          ...previous,
          weekId: response.data.data.weekId,
          planId: response.data.data.planId,
        }),
      }))
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const publishMutation = useMutation({
    mutationFn: () => publishWeeklyDutyPlan(selectedPlanId!, planDetailQuery.data!.etag!),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '清掃計画を発行しました。' })
      await queryClient.invalidateQueries({ queryKey: ['weeklyDutyPlans'] })
      await queryClient.invalidateQueries({ queryKey: ['weeklyDutyPlan'] })
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const plan = planDetailQuery.data?.data
  const area = areaDetailQuery.data?.data
  const page = plansQuery.data

  return (
    <div className="space-y-6">
      <PageHeader
        title="清掃計画"
        description="エリア単位で今週の計画を作成し、確認後に発行します。"
      />

      {feedback ? <Banner kind={feedback.kind} message={feedback.message} /> : null}

      <GlassPanel className="space-y-4">
        <StackedFieldRow>
          <Field label="掃除エリア">
            <SelectInput
              value={search.areaId ?? ''}
              onChange={(event) => {
                void navigate(preserveScrollNavigateOptions({
                  search: (previous) => ({
                    ...previous,
                    areaId: event.target.value || undefined,
                    weekId: undefined,
                    planId: undefined,
                    cursor: undefined,
                  }),
                }))
              }}
            >
              <option value="">選択してください</option>
              {areasQuery.data?.data.map((area) => (
                <option key={area.id} value={area.id}>{area.name}</option>
              ))}
            </SelectInput>
          </Field>
          <Field label="状態">
            <SelectInput
              value={search.status ?? ''}
              onChange={(event) => {
                void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, status: event.target.value ? (event.target.value as 'draft' | 'published' | 'closed') : undefined, cursor: undefined }) }))
              }}
            >
              <option value="">すべて</option>
              <option value="draft">draft</option>
              <option value="published">published</option>
              <option value="closed">closed</option>
            </SelectInput>
          </Field>
        </StackedFieldRow>

        <div className="grid gap-4 md:grid-cols-[1fr_180px_auto]">
          <Field label="今週">
            <TextInput value={currentWeekQuery.data ? resolveWeekLabel(currentWeekQuery.data.data) : 'エリアを選択してください'} disabled />
          </Field>
          <Field label="公平性ウィンドウ">
            <TextInput type="number" value={fairnessWindowWeeks} onChange={(event) => setFairnessWindowWeeks(Number(event.target.value))} />
          </Field>
          <div className="flex items-end">
            <Button
              onClick={() => {
                const parsed = planGenerateSchema.safeParse({ fairnessWindowWeeks })
                if (!parsed.success) {
                  setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '公平性ウィンドウを確認してください。' })
                  return
                }

                generateMutation.mutate()
              }}
              disabled={!search.areaId || !currentWeekQuery.data?.data.weekId || generateMutation.isPending}
            >
              今週の計画を作成
            </Button>
          </div>
        </div>
      </GlassPanel>

      <div className="grid min-w-0 gap-6 xl:grid-cols-[480px_1fr] *:min-w-0">
        <GlassPanel className="space-y-4">
          {page && page.data.length > 0 ? (
            <>
              <DataTable
                headers={['週', '状態', '改訂', '操作']}
                columnClassNames={['w-[30%]', 'w-[26%]', 'w-[14%]', 'w-[30%] text-right']}
                minTableWidthClassName="min-w-full table-fixed"
              >
                {page.data.map((item) => (
                  <tr key={item.id}>
                    <td className="px-4 py-4 font-semibold text-slate-900">{resolveWeekLabel(item)}</td>
                    <td className="px-4 py-4">
                      <StatusBadge label={resolvePlanStatusLabel(item.status)} tone={resolveLifecycleTone(item.status)} />
                    </td>
                    <td className="px-4 py-4 text-sm text-slate-600">r{item.revision}</td>
                    <td className="px-4 py-4 text-right whitespace-nowrap">
                      <Button tone="secondary" onClick={() => void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, planId: item.id, areaId: item.areaId, weekId: undefined }) }))}>
                        詳細
                      </Button>
                    </td>
                  </tr>
                ))}
              </DataTable>
              <div className="flex justify-end gap-3">
                {search.cursor ? <Button tone="ghost" onClick={() => void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: undefined }) }))}>先頭へ戻る</Button> : null}
                {page.meta.hasNext && page.meta.nextCursor ? <Button tone="secondary" onClick={() => void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: page.meta.nextCursor ?? undefined }) }))}>次のページ</Button> : null}
              </div>
            </>
          ) : null}

          {page && page.data.length === 0 ? (
            <EmptyState title="計画がありません" message="エリアを選択し、今週の計画を作成してください。" />
          ) : null}
        </GlassPanel>

        {plan && area ? (
          <div className="min-w-0 space-y-6">
            <GlassPanel className="space-y-4">
              <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <h2 className="text-3xl font-bold text-slate-900">{area.name}</h2>
                  <p className="mt-2 text-sm text-slate-600">{resolveWeekLabel(plan)} / revision {plan.revision}</p>
                </div>
                <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                  <MetricChip label="状態" value={resolvePlanStatusLabel(plan.status)} />
                  <MetricChip label="割当数" value={plan.assignments.length} />
                  <MetricChip label="担当なし" value={plan.offDutyEntries.length} />
                  <MetricChip label="公平性" value={plan.assignmentPolicy.fairnessWindowWeeks} />
                </div>
              </div>
              <div className="flex justify-end">
                <Button
                  onClick={() => publishMutation.mutate()}
                  disabled={plan.status !== 'draft' || publishMutation.isPending}
                >
                  発行する
                </Button>
              </div>
            </GlassPanel>

            <SectionCard title="担当一覧">
              <DataTable
                headers={['掃除箇所', '担当者', '社員番号']}
                columnClassNames={['min-w-[12rem]', 'min-w-[10rem]', 'min-w-[8rem]']}
              >
                {plan.assignments.map((assignment) => (
                  <tr key={assignment.spotId}>
                    <td className="px-4 py-4 font-semibold text-slate-900">{resolveSpotName(area, assignment.spotId)}</td>
                    <td className="px-4 py-4 text-sm text-slate-700">{assignment.user?.displayName ?? assignment.userId}</td>
                    <td className="px-4 py-4 text-sm text-slate-600">{assignment.user?.employeeNumber ?? '-'}</td>
                  </tr>
                ))}
              </DataTable>
            </SectionCard>

            <SectionCard title="担当なし">
              {plan.offDutyEntries.length > 0 ? (
                <div className="flex flex-wrap gap-2">
                  {plan.offDutyEntries.map((entry) => (
                    <StatusBadge
                      key={entry.userId}
                      label={`${entry.user?.displayName ?? entry.userId} ${entry.user?.employeeNumber ? `(${entry.user.employeeNumber})` : ''}`}
                    />
                  ))}
                </div>
              ) : (
                <EmptyState title="担当なしはありません" message="全メンバーに今週の担当が割り当てられています。" />
              )}
            </SectionCard>
          </div>
        ) : (
          <GlassPanel>
            <EmptyState title="計画を選択してください" message="左の一覧から計画を選ぶと、割当と担当なしを確認できます。" />
          </GlassPanel>
        )}
      </div>
    </div>
  )
}
