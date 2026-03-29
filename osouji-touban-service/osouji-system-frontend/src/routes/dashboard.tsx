import { useQueries, useQuery } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useState } from 'react'
import {
  Button,
  EmptyState,
  Field,
  GlassPanel,
  SelectInput,
  StatusBadge,
} from '../components/ui'
import {
  getCleaningArea,
  getCleaningAreaCurrentWeek,
  getWeeklyDutyPlan,
  listCleaningAreas,
  listWeeklyDutyPlans,
  queryKeys,
  resolvePlanStatusLabel,
  resolveSpotName,
  resolveWeekLabel,
} from '../lib/api'
import { loadDashboardSettings, saveDashboardSettings } from '../lib/dashboard-settings'
import { formatTimestamp } from '../lib/date'

export const Route = createFileRoute('/dashboard')({
  component: DashboardPage,
})

function DashboardPage() {
  const [settings, setSettings] = useState(loadDashboardSettings)
  const [settingsOpen, setSettingsOpen] = useState(false)

  const areasQuery = useQuery({
    queryKey: queryKeys.cleaningAreas({ sort: 'name', limit: 100 }),
    queryFn: () => listCleaningAreas({ sort: 'name', limit: 100 }),
    refetchInterval: 60_000,
  })

  const selectedAreaIds = settings.layout === 'single'
    ? settings.areaIds.slice(0, 1)
    : settings.areaIds.slice(0, 2)

  const areaDetailQueries = useQueries({
    queries: selectedAreaIds.map((areaId) => ({
      queryKey: queryKeys.cleaningArea(areaId),
      queryFn: () => getCleaningArea(areaId),
      refetchInterval: 60_000,
    })),
  })

  const currentWeekQueries = useQueries({
    queries: selectedAreaIds.map((areaId) => ({
      queryKey: queryKeys.cleaningAreaCurrentWeek(areaId),
      queryFn: () => getCleaningAreaCurrentWeek(areaId),
      refetchInterval: 60_000,
    })),
  })

  const planListQueries = useQueries({
    queries: selectedAreaIds.map((areaId, index) => ({
      queryKey: queryKeys.weeklyDutyPlans({
        areaId,
        weekId: currentWeekQueries[index]?.data?.data.weekId,
      }),
      queryFn: () => listWeeklyDutyPlans({
        areaId,
        weekId: currentWeekQueries[index]?.data?.data.weekId,
        limit: 1,
        sort: '-weekId',
      }),
      enabled: Boolean(currentWeekQueries[index]?.data?.data.weekId),
      refetchInterval: 60_000,
    })),
  })

  const planDetailQueries = useQueries({
    queries: selectedAreaIds.map((_, index) => ({
      queryKey: planListQueries[index]?.data?.data[0]?.id ? queryKeys.weeklyDutyPlan(planListQueries[index].data!.data[0]!.id) : ['weeklyDutyPlan', `idle-${index}`],
      queryFn: () => getWeeklyDutyPlan(planListQueries[index].data!.data[0]!.id),
      enabled: Boolean(planListQueries[index]?.data?.data[0]?.id),
      refetchInterval: 60_000,
    })),
  })

  const updatedAt = Math.max(
    areasQuery.dataUpdatedAt,
    ...areaDetailQueries.map((query) => query.dataUpdatedAt),
    ...currentWeekQueries.map((query) => query.dataUpdatedAt),
    ...planListQueries.map((query) => query.dataUpdatedAt),
    ...planDetailQueries.map((query) => query.dataUpdatedAt),
  )

  return (
    <div className="min-h-screen px-4 py-4 lg:px-6">
      <div className="mx-auto max-w-430 space-y-5">
        <div className="flex items-center justify-between">
          <div>
            <p className="text-xs uppercase tracking-[0.28em] text-teal-700/70">Always On Dashboard</p>
            <h1 className="mt-2 text-3xl font-bold text-slate-900">今週の担当</h1>
          </div>
          <Button tone="secondary" onClick={() => setSettingsOpen((previous) => !previous)}>
            設定
          </Button>
        </div>

        {settingsOpen ? (
          <GlassPanel className="space-y-4">
            <div className="grid gap-4 lg:grid-cols-3">
              <Field label="レイアウト">
                <SelectInput
                  value={settings.layout}
                  onChange={(event) => {
                    const next = {
                      ...settings,
                      layout: event.target.value as 'single' | 'double',
                    }
                    setSettings(next)
                    saveDashboardSettings(next)
                  }}
                >
                  <option value="single">1 面表示</option>
                  <option value="double">2 面表示</option>
                </SelectInput>
              </Field>
              <Field label="エリア 1">
                <SelectInput
                  value={settings.areaIds[0] ?? ''}
                  onChange={(event) => {
                    const next = {
                      ...settings,
                      areaIds: [event.target.value, settings.areaIds[1]].filter(Boolean),
                    }
                    setSettings(next)
                    saveDashboardSettings(next)
                  }}
                >
                  <option value="">未選択</option>
                  {areasQuery.data?.data.map((area) => (
                    <option key={area.id} value={area.id}>{area.name}</option>
                  ))}
                </SelectInput>
              </Field>
              {settings.layout === 'double' ? (
                <Field label="エリア 2">
                  <SelectInput
                    value={settings.areaIds[1] ?? ''}
                    onChange={(event) => {
                      const next = {
                        ...settings,
                        areaIds: [settings.areaIds[0], event.target.value].filter(Boolean),
                      }
                      setSettings(next)
                      saveDashboardSettings(next)
                    }}
                  >
                    <option value="">未選択</option>
                    {areasQuery.data?.data.map((area) => (
                      <option key={area.id} value={area.id}>{area.name}</option>
                    ))}
                  </SelectInput>
                </Field>
              ) : null}
            </div>
          </GlassPanel>
        ) : null}

        {selectedAreaIds.length === 0 ? (
          <GlassPanel>
            <EmptyState title="表示エリアが未設定です" message="設定から 1 つまたは 2 つの掃除エリアを選択してください。" />
          </GlassPanel>
        ) : (
          <div className={`grid gap-5 ${settings.layout === 'double' ? 'md:grid-cols-2' : ''}`}>
            {selectedAreaIds.map((areaId, index) => {
              const area = areaDetailQueries[index]?.data?.data
              const currentWeek = currentWeekQueries[index]?.data?.data
              const plan = planDetailQueries[index]?.data?.data

              return (
                <GlassPanel key={areaId} className="min-h-[70vh] space-y-5 rounded-[2.5rem] p-6 lg:p-7">
                  {area ? (
                    <>
                      <div className="flex flex-wrap items-start justify-between gap-4">
                        <div>
                          <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{currentWeek ? resolveWeekLabel(currentWeek) : 'week -'}</p>
                          <h2 className="mt-2 text-3xl font-bold text-slate-900">{area.name}</h2>
                        </div>
                        <div className="text-right">
                          <StatusBadge label={plan ? resolvePlanStatusLabel(plan.status) : '未作成'} />
                          <p className="mt-2 text-xs text-slate-500">
                            更新: {updatedAt > 0 ? formatTimestamp(updatedAt) : '-'}
                          </p>
                        </div>
                      </div>

                      {plan ? (
                        <>
                          <div className="grid gap-3 md:grid-cols-2">
                            {plan.assignments.map((assignment) => (
                              <div key={assignment.spotId} className="rounded-[1.75rem] border border-white/70 bg-white/70 p-4">
                                <div className="mt-1.5 text-xl font-bold text-slate-900">{resolveSpotName(area, assignment.spotId)}</div>
                                <div className="mt-1.5 text-xl font-bold text-teal-800">{assignment.user?.displayName ?? assignment.userId}</div>
                              </div>
                            ))}
                          </div>

                          <div className="rounded-[1.75rem] border border-dashed border-white/70 bg-white/50 p-4">
                            <div className="text-xs uppercase tracking-[0.18em] text-slate-500">担当なし</div>
                            {plan.offDutyEntries.length > 0 ? (
                              <div className="mt-3 flex flex-wrap gap-2.5">
                                {plan.offDutyEntries.map((entry) => (
                                  <StatusBadge key={entry.userId} label={entry.user?.displayName ?? entry.userId} />
                                ))}
                              </div>
                            ) : (
                              <div className="mt-2 text-base font-semibold text-slate-700">担当なしはありません</div>
                            )}
                          </div>
                        </>
                      ) : (
                        <EmptyState title="今週の計画がありません" message="管理画面から今週の清掃計画を作成してください。" />
                      )}
                    </>
                  ) : (
                    <EmptyState title="読み込み中" message="エリア詳細を取得しています。" />
                  )}
                </GlassPanel>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
