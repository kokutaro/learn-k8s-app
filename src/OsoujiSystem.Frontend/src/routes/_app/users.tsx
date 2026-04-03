import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { createFileRoute } from '@tanstack/react-router'
import { useEffect, useState } from 'react'
import { z } from 'zod'
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
    StackedFieldRow,
    StatusBadge,
    TextInput,
} from '../../components/ui'
import {
    changeUserLifecycle,
    createUser,
    explainApiError,
    getUser,
    listUsers,
    queryKeys,
    resolveLifecycleTone,
    updateUser,
} from '../../lib/api'
import { userCreateSchema, userEditSchema } from '../../lib/contracts'
import { preserveScrollNavigateOptions } from '../../lib/navigation'

const searchSchema = z.object({
  query: z.string().optional(),
  status: z.enum(['pendingActivation', 'active', 'suspended', 'archived']).optional(),
  sort: z.enum(['displayName', '-displayName', 'employeeNumber', '-employeeNumber']).optional().default('displayName'),
  cursor: z.string().optional(),
  limit: z.coerce.number().optional().default(20),
})

type Feedback = { kind: 'success' | 'error'; message: string } | null

type UserCreateState = {
  employeeNumber: string
  displayName: string
  emailAddress: string
  departmentCode: string
}

type UserEditState = Omit<UserCreateState, 'employeeNumber'>

const defaultCreateForm: UserCreateState = {
  employeeNumber: '',
  displayName: '',
  emailAddress: '',
  departmentCode: '',
}

const defaultEditForm: UserEditState = {
  displayName: '',
  emailAddress: '',
  departmentCode: '',
}

export const Route = createFileRoute('/_app/users')({
  validateSearch: (search) => searchSchema.parse(search),
  component: UsersPage,
})

function UsersPage() {
  const navigate = Route.useNavigate()
  const search = Route.useSearch()
  const queryClient = useQueryClient()

  const [feedback, setFeedback] = useState<Feedback>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [editUserId, setEditUserId] = useState<string | null>(null)
  const [archiveUserId, setArchiveUserId] = useState<string | null>(null)
  const [createForm, setCreateForm] = useState<UserCreateState>(defaultCreateForm)
  const [editForm, setEditForm] = useState<UserEditState>(defaultEditForm)

  const usersQuery = useQuery({
    queryKey: queryKeys.users(search),
    queryFn: () => listUsers(search),
  })

  const selectedUserId = editUserId ?? archiveUserId
  const userDetailQuery = useQuery({
    queryKey: selectedUserId ? queryKeys.user(selectedUserId) : ['user', 'idle'],
    queryFn: () => getUser(selectedUserId!),
    enabled: Boolean(selectedUserId),
  })

  useEffect(() => {
    if (createOpen) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reset form each time the create modal is opened.
      setCreateForm(defaultCreateForm)
    }
  }, [createOpen])

  useEffect(() => {
    if (editUserId && userDetailQuery.data?.data) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- hydrate edit form from freshly loaded detail data.
      setEditForm({
        displayName: userDetailQuery.data.data.displayName,
        emailAddress: userDetailQuery.data.data.emailAddress ?? '',
        departmentCode: userDetailQuery.data.data.departmentCode ?? '',
      })
    }
  }, [editUserId, userDetailQuery.data])

  const createMutation = useMutation({
    mutationFn: () => createUser(createForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: 'ユーザーを追加しました。' })
      setCreateOpen(false)
      await queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: (error) => setFeedback({ kind: 'error', message: explainApiError(error) }),
  })

  const updateMutation = useMutation({
    mutationFn: () => updateUser(editUserId!, userDetailQuery.data!.etag!, editForm),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: 'ユーザー情報を更新しました。' })
      setEditUserId(null)
      await queryClient.invalidateQueries({ queryKey: ['users'] })
      if (selectedUserId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.user(selectedUserId) })
      }
    },
    onError: async (error) => {
      setFeedback({ kind: 'error', message: explainApiError(error) })
      if (selectedUserId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.user(selectedUserId) })
      }
    },
  })

  const archiveMutation = useMutation({
    mutationFn: () => changeUserLifecycle(archiveUserId!, userDetailQuery.data!.etag!, 'archived'),
    onSuccess: async () => {
      setFeedback({ kind: 'success', message: 'ユーザーを無効化しました。' })
      setArchiveUserId(null)
      await queryClient.invalidateQueries({ queryKey: ['users'] })
    },
    onError: async (error) => {
      setFeedback({ kind: 'error', message: explainApiError(error) })
      if (selectedUserId) {
        await queryClient.invalidateQueries({ queryKey: queryKeys.user(selectedUserId) })
      }
    },
  })

  const page = usersQuery.data

  return (
    <div className="flex min-h-0 flex-col gap-6 lg:h-full">
      <PageHeader
        title="ユーザー管理"
        description="従業員番号、表示名、メール、部署を管理します。削除は archived への変更で扱います。"
        action={<Button onClick={() => setCreateOpen(true)}>ユーザーを追加</Button>}
      />

      {feedback ? <Banner kind={feedback.kind} message={feedback.message} /> : null}

      <GlassPanel className="space-y-4">
        <StackedFieldRow>
          <Field label="検索">
            <TextInput
              value={search.query ?? ''}
              placeholder="社員番号、表示名、部署コード"
              onChange={(event) => {
                void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, query: event.target.value || undefined, cursor: undefined }) }))
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
                    status: event.target.value
                      ? (event.target.value as 'pendingActivation' | 'active' | 'suspended' | 'archived')
                      : undefined,
                    cursor: undefined,
                  }),
                }))
              }}
            >
              <option value="">すべて</option>
              <option value="active">active</option>
              <option value="pendingActivation">pendingActivation</option>
              <option value="suspended">suspended</option>
              <option value="archived">archived</option>
            </SelectInput>
          </Field>
        </StackedFieldRow>
      </GlassPanel>

      <GlassPanel className="flex min-h-0 flex-col gap-4 lg:flex-1">
        {usersQuery.isLoading ? <p className="text-sm text-[var(--color-text-secondary)]">読み込み中...</p> : null}
        {page && page.data.length > 0 ? (
          <>
            <DataTable
              headers={['表示名', '社員番号', '部署', '状態', '操作']}
              columnClassNames={['min-w-[12rem]', 'min-w-[8rem]', 'min-w-[8rem]', 'min-w-[10rem]', 'min-w-[11rem]']}
              stickyHeader
              testId="users-results-scroll"
              containerClassName="min-h-0 lg:flex-1 lg:overflow-y-auto"
            >
              {page.data.map((user) => (
                <tr key={user.userId}>
                  <td className="px-4 py-4 font-semibold text-[var(--color-text)]">{user.displayName}</td>
                  <td className="px-4 py-4 text-sm text-[var(--color-text)]">{user.employeeNumber}</td>
                  <td className="px-4 py-4 text-sm text-[var(--color-text)]">{user.departmentCode ?? '未設定'}</td>
                  <td className="px-4 py-4">
                    <StatusBadge label={user.lifecycleStatus} tone={resolveLifecycleTone(user.lifecycleStatus)} />
                  </td>
                  <td className="px-4 py-4">
                    <div className="flex flex-wrap gap-2">
                      <Button tone="secondary" onClick={() => setEditUserId(user.userId)}>編集</Button>
                      {user.lifecycleStatus !== 'archived' ? (
                        <Button tone="danger" onClick={() => setArchiveUserId(user.userId)}>無効化</Button>
                      ) : null}
                    </div>
                  </td>
                </tr>
              ))}
            </DataTable>
            <div className="flex justify-end gap-3">
              {search.cursor ? <Button tone="ghost" onClick={() => void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: undefined }) }))}>先頭へ戻る</Button> : null}
              {page.meta.hasNext && page.meta.nextCursor ? (
                <Button tone="secondary" onClick={() => void navigate(preserveScrollNavigateOptions({ search: (previous) => ({ ...previous, cursor: page.meta.nextCursor ?? undefined }) }))}>次のページ</Button>
              ) : null}
            </div>
          </>
        ) : null}

        {page && page.data.length === 0 ? (
          <EmptyState title="ユーザーがまだありません" message="作成したユーザーを掃除エリアへ割り当てます。" />
        ) : null}
      </GlassPanel>

      <Modal open={createOpen} title="ユーザーを追加" onClose={() => setCreateOpen(false)}>
        <form
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault()
            const parsed = userCreateSchema.safeParse(createForm)
            if (!parsed.success) {
              setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
              return
            }

            createMutation.mutate()
          }}
        >
          <StackedFieldRow>
            <Field label="社員番号">
              <TextInput value={createForm.employeeNumber} onChange={(event) => setCreateForm((previous) => ({ ...previous, employeeNumber: event.target.value }))} />
            </Field>
            <Field label="表示名">
              <TextInput value={createForm.displayName} onChange={(event) => setCreateForm((previous) => ({ ...previous, displayName: event.target.value }))} />
            </Field>
          </StackedFieldRow>
          <StackedFieldRow>
            <Field label="メールアドレス">
              <TextInput value={createForm.emailAddress} onChange={(event) => setCreateForm((previous) => ({ ...previous, emailAddress: event.target.value }))} />
            </Field>
            <Field label="部署コード">
              <TextInput value={createForm.departmentCode} onChange={(event) => setCreateForm((previous) => ({ ...previous, departmentCode: event.target.value }))} />
            </Field>
          </StackedFieldRow>
          <div className="flex justify-end gap-3">
            <Button tone="ghost" onClick={() => setCreateOpen(false)}>キャンセル</Button>
            <Button type="submit" disabled={createMutation.isPending}>保存</Button>
          </div>
        </form>
      </Modal>

      <Modal open={Boolean(editUserId)} title="ユーザーを編集" onClose={() => setEditUserId(null)}>
        {userDetailQuery.data?.data ? (
          <form
            className="space-y-4"
            onSubmit={(event) => {
              event.preventDefault()
              const parsed = userEditSchema.safeParse(editForm)
              if (!parsed.success) {
                setFeedback({ kind: 'error', message: parsed.error.issues[0]?.message ?? '入力内容を確認してください。' })
                return
              }

              updateMutation.mutate()
            }}
          >
            <Field label="社員番号">
              <TextInput value={userDetailQuery.data.data.employeeNumber} disabled />
            </Field>
            <StackedFieldRow>
              <Field label="表示名">
                <TextInput value={editForm.displayName} onChange={(event) => setEditForm((previous) => ({ ...previous, displayName: event.target.value }))} />
              </Field>
              <Field label="部署コード">
                <TextInput value={editForm.departmentCode} onChange={(event) => setEditForm((previous) => ({ ...previous, departmentCode: event.target.value }))} />
              </Field>
            </StackedFieldRow>
            <Field label="メールアドレス">
              <TextInput value={editForm.emailAddress} onChange={(event) => setEditForm((previous) => ({ ...previous, emailAddress: event.target.value }))} />
            </Field>
            <div className="flex justify-end gap-3">
              <Button tone="ghost" onClick={() => setEditUserId(null)}>キャンセル</Button>
              <Button type="submit" disabled={updateMutation.isPending}>更新</Button>
            </div>
          </form>
        ) : (
          <p className="text-sm text-[var(--color-text-secondary)]">読み込み中...</p>
        )}
      </Modal>

      <Modal open={Boolean(archiveUserId)} title="ユーザーを無効化" onClose={() => setArchiveUserId(null)}>
        <div className="space-y-4">
          <p className="text-sm text-[var(--color-text-secondary)]">
            {userDetailQuery.data?.data.displayName ?? 'このユーザー'} を archived に変更します。履歴参照は保持されます。
          </p>
          <div className="flex justify-end gap-3">
            <Button tone="ghost" onClick={() => setArchiveUserId(null)}>キャンセル</Button>
            <Button tone="danger" onClick={() => archiveMutation.mutate()} disabled={archiveMutation.isPending || userDetailQuery.isLoading}>
              無効化する
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  )
}
