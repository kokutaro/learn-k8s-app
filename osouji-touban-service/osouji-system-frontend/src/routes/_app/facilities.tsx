import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { z } from 'zod'
import {
  changeFacilityActivation,
  createFacility,
  explainApiError,
  getFacility,
  listFacilities,
  queryKeys,
  resolveLifecycleTone,
  updateFacility,
} from '../../lib/api'
import { currentIsoWeekId } from '../../lib/date'
import { facilityEditSchema, facilityFormSchema } from '../../lib/contracts'
import {
  Banner,
  Button,
  DataTable,
  EmptyState,
  Field,
  GlassPanel,
  Modal,
  PageHeader,
  SelectInput,
  StatusBadge,
  StackedFieldRow,
  TextArea,
  TextInput,
} from '../../components/ui'
import { preserveScrollNavigateOptions } from '../../lib/navigation'

const searchSchema = z.object({
  query: z.string().optional(),
  status: z.enum(['active', 'inactive']).optional(),
  sort: z.enum(['name', '-name', 'facilityCode', '-facilityCode']).optional().default('name'),
  cursor: z.string().optional(),
  limit: z.coerce.number().optional().default(20),
})

type Feedback = { kind: 'success' | 'error'; message: string } | null

type FacilityFormState = {
  facilityCode: string
  name: string
  description: string
  timeZoneId: string
}

type FacilityEditState = Omit<FacilityFormState, 'facilityCode'>

const defaultCreateForm: FacilityFormState = {
  facilityCode: 'FAC-' + currentIsoWeekId().replace('-', ''),
  name: '',
  description: '',
  timeZoneId: 'Asia/Tokyo',
}

const defaultEditForm: FacilityEditState = {
  name: '',
  description: '',
  timeZoneId: 'Asia/Tokyo',
}

export const Route = createFileRoute('/_app/facilities')({
  validateSearch: (search) => searchSchema.parse(search),
  component: FacilitiesPage,
})

function FacilitiesPage() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const queryClient = useQueryClient()

  const [feedback, setFeedback] = useState<Feedback>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [editFacilityId, setEditFacilityId] = useState<string | null>(null)
  const [deactivateFacilityId, setDeactivateFacilityId] = useState<string | null>(null)
  const [createForm, setCreateForm] = useState<FacilityFormState>(defaultCreateForm)
  const [editForm, setEditForm] = useState<FacilityEditState>(defaultEditForm)

  const facilitiesQuery = useQuery({
    queryKey: queryKeys.facilities(search),
    queryFn: () => listFacilities(search),
  })

  const selectedFacilityId = editFacilityId ?? deactivateFacilityId
  const facilityDetailQuery = useQuery({
    queryKey: selectedFacilityId ? queryKeys.facility(selectedFacilityId) : ['facility', 'idle'],
    queryFn: () => getFacility(selectedFacilityId!),
    enabled: Boolean(selectedFacilityId),
  })

  useEffect(() => {
    if (createOpen) {
      setCreateForm(defaultCreateForm)
    }
  }, [createOpen])

  useEffect(() => {
    if (editFacilityId && facilityDetailQuery.data?.data) {
      setEditForm({
        name: facilityDetailQuery.data.data.name,
        description: facilityDetailQuery.data.data.description ?? '',
        timeZoneId: facilityDetailQuery.data.data.timeZoneId,
      })
    }
  }, [editFacilityId, facilityDetailQuery.data])

  const createMutation = useMutation({
    mutationFn: () => createFacility(createForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '施設を追加しました。' })
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['facilities'] })
    },
    onError: (error) => {
      setFeedback({ kind: 'error', message: explainApiError(error) })
    },
  })

  const updateMutation = useMutation({
    mutationFn: () => updateFacility(editFacilityId!, facilityDetailQuery.data!.etag!, editForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '施設情報を更新しました。' })
      setEditFacilityId(null)
      await queryClient.invalidateQueries({ queryKey: ['facilities'] })
      if (selectedFacilityId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.facility(selectedFacilityId) })
      }
    },
    onError: async (error) => {
      setFeedback({ kind: 'error', message: explainApiError(error) })
      if (selectedFacilityId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.facility(selectedFacilityId) })
      }
    },
  })

  const deactivateMutation = useMutation({
    mutationFn: () => changeFacilityActivation(deactivateFacilityId!, facilityDetailQuery.data!.etag!, 'inactive'),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: '施設を無効化しました。' })
      setDeactivateFacilityId(null)
      await queryClient.invalidateQueries({ queryKey: ['facilities'] })
    },
    onError: async (error) => {
      setFeedback({ kind: 'error', message: explainApiError(error) })
      if (selectedFacilityId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.facility(selectedFacilityId) })
      }
    },
  })

  const page = facilitiesQuery.data

  return (
    <div className="space-y-6">
      <PageHeader
        title="施設管理"
        description="施設コード、名称、タイムゾーンを管理します。削除は行わず、inactive への切り替えで運用停止します。"
        action={<Button onClick={() => setCreateOpen(true)}>施設を追加</Button>}
      />

      {feedback ? <Banner kind={feedback.kind} message={feedback.message} /> : null}
      {facilitiesQuery.isError ? <Banner kind="error" message={explainApiError(facilitiesQuery.error)} /> : null}

      <GlassPanel className="space-y-4">
        <StackedFieldRow>
          <Field label="検索">
            <TextInput
              value={search.query ?? ''}
              placeholder="施設コードまたは施設名"
              onChange={(event) => {
                void navigate(preserveScrollNavigateOptions({
                  search: (previous) => ({ ...previous, query: event.target.value || undefined, cursor: undefined }),
                }))
              }}
            />
          </Field>
          <Field label="状態">
            <SelectInput
              value={search.status ?? ''}
              onChange={(event) => {
                void navigate(preserveScrollNavigateOptions({
                  search: (previous) => ({
                    ...previous,
                    status: event.target.value ? (event.target.value as 'active' | 'inactive') : undefined,
                    cursor: undefined,
                  }),
                }))
              }}
            >
              <option value="">すべて</option>
              <option value="active">active</option>
              <option value="inactive">inactive</option>
            </SelectInput>
          </Field>
        </StackedFieldRow>
      </GlassPanel>

      <GlassPanel className="space-y-4">
        {facilitiesQuery.isLoading ? <p className="text-sm text-slate-500">読み込み中...</p> : null}
        {page && page.data.length > 0 ? (
          <>
            <DataTable
              headers={['施設名', '施設コード', 'タイムゾーン', '状態', '操作']}
              columnClassNames={['min-w-[12rem]', 'min-w-[10rem]', 'min-w-[10rem]', 'min-w-[8rem]', 'min-w-[11rem]']}
            >
              {page.data.map((facility) => (
                <tr key={facility.id}>
                  <td className="px-4 py-4">
                    <div className="font-semibold text-slate-900">{facility.name}</div>
                  </td>
                  <td className="px-4 py-4 text-sm text-slate-600">{facility.facilityCode}</td>
                  <td className="px-4 py-4 text-sm text-slate-600">{facility.timeZoneId}</td>
                  <td className="px-4 py-4">
                    <StatusBadge label={facility.lifecycleStatus} tone={resolveLifecycleTone(facility.lifecycleStatus)} />
                  </td>
                  <td className="px-4 py-4">
                    <div className="flex flex-wrap gap-2">
                      <Button tone="secondary" onClick={() => setEditFacilityId(facility.id)}>編集</Button>
                      {facility.lifecycleStatus === 'active' ? (
                        <Button tone="danger" onClick={() => setDeactivateFacilityId(facility.id)}>無効化</Button>
                      ) : null}
                    </div>
                  </td>
                </tr>
              ))}
            </DataTable>
            <div className="flex justify-end gap-3">
              {search.cursor ? (
                <Button
                  tone="ghost"
                  onClick={() => {
                    void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: undefined }) }))
                  }}
                >
                  先頭へ戻る
                </Button>
              ) : null}
              {page.meta.hasNext && page.meta.nextCursor ? (
                <Button
                  tone="secondary"
                  onClick={() => {
                    void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: page.meta.nextCursor ?? undefined }) }))
                  }}
                >
                  次のページ
                </Button>
              ) : null}
            </div>
          </>
        ) : null}

        {page && page.data.length === 0 ? (
          <EmptyState title="施設がまだありません" message="最初の施設を追加すると、掃除エリアの作成が可能になります。" />
        ) : null}
      </GlassPanel>

      <Modal
        open={createOpen}
        title="施設を追加"
        description="施設コードは一意、タイムゾーンは IANA ID で入力します。"
        onClose={() => setCreateOpen(false)}
      >
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            const parsed = facilityFormSchema.safeParse(createForm)
            if (!parsed.success) {
              setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
              return
            }

            createMutation.mutate()
          }}
        >
          <StackedFieldRow>
            <Field label="施設コード">
              <TextInput value={createForm.facilityCode} onChange={(event) => setCreateForm((previous) => ({ ...previous, facilityCode: event.target.value }))} />
            </Field>
            <Field label="施設名">
              <TextInput value={createForm.name} onChange={(event) => setCreateForm((previous) => ({ ...previous, name: event.target.value }))} />
            </Field>
          </StackedFieldRow>
          <Field label="タイムゾーン">
            <TextInput value={createForm.timeZoneId} onChange={(event) => setCreateForm((previous) => ({ ...previous, timeZoneId: event.target.value }))} />
          </Field>
          <Field label="説明">
            <TextArea value={createForm.description} onChange={(event) => setCreateForm((previous) => ({ ...previous, description: event.target.value }))} />
          </Field>
          <div className="flex justify-end gap-3">
            <Button tone="ghost" onClick={() => setCreateOpen(false)}>キャンセル</Button>
            <Button type="submit" disabled={createMutation.isPending}>保存</Button>
          </div>
        </form>
      </Modal>

      <Modal
        open={Boolean(editFacilityId)}
        title="施設を編集"
        description="更新は ETag を使って楽観排他で送信されます。"
        onClose={() => setEditFacilityId(null)}
      >
        {facilityDetailQuery.data?.data ? (
          <form
            className="space-y-4"
            onSubmit={(event) => {
              event.preventDefault()
              const parsed = facilityEditSchema.safeParse(editForm)
              if (!parsed.success) {
                setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
                return
              }

              updateMutation.mutate()
            }}
          >
            <Field label="施設コード">
              <TextInput value={facilityDetailQuery.data.data.facilityCode} disabled />
            </Field>
            <StackedFieldRow>
              <Field label="施設名">
                <TextInput value={editForm.name} onChange={(event) => setEditForm((previous) => ({ ...previous, name: event.target.value }))} />
              </Field>
              <Field label="タイムゾーン">
                <TextInput value={editForm.timeZoneId} onChange={(event) => setEditForm((previous) => ({ ...previous, timeZoneId: event.target.value }))} />
              </Field>
            </StackedFieldRow>
            <Field label="説明">
              <TextArea value={editForm.description} onChange={(event) => setEditForm((previous) => ({ ...previous, description: event.target.value }))} />
            </Field>
            <div className="flex justify-end gap-3">
              <Button tone="ghost" onClick={() => setEditFacilityId(null)}>キャンセル</Button>
              <Button type="submit" disabled={updateMutation.isPending}>更新</Button>
            </div>
          </form>
        ) : (
          <p className="text-sm text-slate-500">読み込み中...</p>
        )}
      </Modal>

      <Modal
        open={Boolean(deactivateFacilityId)}
        title="施設を無効化"
        description="inactive に切り替えると、新しい掃除エリア作成の対象外になります。"
        onClose={() => setDeactivateFacilityId(null)}
      >
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            {facilityDetailQuery.data?.data.name ?? 'この施設'} を無効化します。既存データは削除されません。
          </p>
          <div className="flex justify-end gap-3">
            <Button tone="ghost" onClick={() => setDeactivateFacilityId(null)}>キャンセル</Button>
            <Button tone="danger" onClick={() => deactivateMutation.mutate()} disabled={deactivateMutation.isPending || facilityDetailQuery.isLoading}>
              無効化する
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
