# お掃除当番システム ユーザージャーニー / API統合テスト計画（v2）

## 1. 目的

- `application-usecase-design-v1.md`
- `core-domain-design-v3.md`
- `api-endpoint-design-v1.md`
- `user-management-bc-design-v1.md`

上記文書で定義された業務フローを、公開 API と内部 API の両方で end-to-end に検証できるよう、ユーザージャーニーと API 統合テストケースへ落とし込む。

## 2. 前提

- 対象 API は `/api/v1` 配下の Users / CleaningArea / WeeklyDutyPlan / internal endpoints。
- 認証認可は本計画の対象外とし、HTTP 契約と業務整合性を優先して確認する。
- `employeeNumber` は User Management BC が正本として管理し、`^\d{6}$` を正とする。
- `AssignUserToArea` / `TransferUserToArea` は `projection_user_directory` を優先参照し、`employeeNumber` 明示入力は移行期間のフォールバックとする。
- User Management BC の更新は `user-registry.user-registered` / `user-registry.user-updated` を通じて別 BC の user directory projection へ反映される。
- Projection は非同期反映のため、write 後の read assert は projection drain 後に行う。
- current week はテスト固定時計により `2026-W10` として扱う。

## 3. ユーザージャーニー

| ID  | Actor           | Trigger                  | Main Flow                                                            | Observable Outcome                                                                                      | Covered Endpoints                                                                                         | Negative Cases                                                                |
| --- | --------------- | ------------------------ | -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| J01 | 管理者          | 新規ユーザー登録         | user 登録 -> profile 更新 or identity link 追加                      | `Location` / body が返り、以後の BC 連携元となる `UserId` が確定する                                    | `POST /users`, `PATCH /users/{userId}`, `POST /users/{userId}/identity-links`                             | invalid displayName, duplicate employeeNumber, duplicate auth identity        |
| J02 | 管理者          | 新規エリア立ち上げ       | user 登録 -> area 登録 -> member 所属 -> weekly plan 生成 -> publish | area detail / plan detail / ETag / Location が整合し、member 所属は user directory 経由で成立する       | `POST /users`, `POST/GET /cleaning-areas`, `POST /members`, `POST /weekly-duty-plans`, `PUT /publication` | area 入力不正, `If-Match` 欠落, duplicate plan, stale publish                 |
| J03 | 管理者          | 当週メンバー追加         | current week plan 生成済み area に member 追加                       | plan `revision` が増加し、追加 user が assignment か off-duty に反映される                              | `POST /members`, `GET /weekly-duty-plans/{id}`                                                            | plan 未作成時は area のみ更新, 未登録 user は拒否, suspended user は拒否      |
| J04 | 管理者          | 当週メンバー削除         | current week plan 生成済み area から member 削除                     | 削除 user が plan から消え、残 user で再配分される                                                      | `DELETE /members/{userId}`, `GET /weekly-duty-plans/{id}`                                                 | 未所属 user 削除は `204` no-op                                                |
| J05 | 管理者          | 掃除箇所変更             | current week plan 生成済み area へ spot 追加 / 削除                  | plan `revision` が増加し、assignment 数が current spots と一致する                                      | `POST/DELETE /spots`, `GET /weekly-duty-plans/{id}`                                                       | duplicate spot, 最後の 1 件削除, 未登録 spot 削除 no-op                       |
| J06 | 管理者          | エリア間異動             | source/target area 用意 -> member transfer 実行                      | area membership と両 area の当週 plan が整合し、target 側 `employeeNumber` は user directory を参照する | `POST /area-member-transfers`, `GET /cleaning-areas/{id}`, `GET /weekly-duty-plans/{id}`                  | same area transfer, stale body version, unknown area, suspended user transfer |
| J07 | 管理者/運用     | 翌週ルール変更           | pending week rule 予約 -> 内部 API で due 判定適用                   | 未到達週は未反映、到達週で current rule へ昇格する                                                      | `PUT /pending-week-rule`, `POST /internal/week-rule-applications`, `GET /cleaning-areas/{id}`             | invalid week rule, stale `If-Match`                                           |
| J08 | 運用ジョブ      | current week 一括生成    | 全 area 走査で plan 生成                                             | `generated/skipped/failed` が area 状態に応じて集計される                                               | `POST /internal/current-week-plan-generations`, `GET /weekly-duty-plans`                                  | メンバー不足 area の失敗、既存 plan の skip                                   |
| J09 | 管理者          | 計画クローズ後の運用継続 | close 実行後に publish 再試行、spot/member 変更                      | plan は `closed` を維持し、再公開不可、関連 area 変更後も plan は不変                                   | `PUT /closure`, `PUT /publication`, `POST /spots`, `POST /members`                                        | closed 後 publish は `409 WeekAlreadyClosedError`                             |
| J10 | クライアント/UI | 一覧再取得               | list API へ filter / sort / cursor 付きでアクセス                    | detail と list の read model が追従し、次ページ導線が機能する                                           | `GET /cleaning-areas`, `GET /weekly-duty-plans`                                                           | invalid sort / invalid filter は別系統で validation                           |

## 4. テストスイート構成

### 4.1 `UserManagementApiTests`

- J01 の user 登録 / profile 更新
- user directory projection を使った member 所属
- suspended user の所属拒否

### 4.2 `CleaningAreaApiTests`

- J02 の area 作成・member 所属の基本契約
- J05 の spot 競合 / last spot / no-op delete
- J07 の pending week rule 登録
- J10 の cleaning area list filter / sort / cursor

### 4.3 `WeeklyDutyPlanApiTests`

- J02 の generate / publish happy path
- duplicate generate と stale publish
- J09 の close idempotency と republish 拒否
- J10 の weekly plan list filter / sort / cursor

### 4.4 `TransferAndRebalanceApiTests`

- J03 の member add rebalance
- J04 の member remove rebalance
- J05 の current week plan 再計算
- J06 の area transfer
- J09 の closed plan 不変

### 4.5 `InternalOperationsApiTests`

- J07 の due week rule application
- J08 の current week batch generation 集計

## 5. ケース設計方針

- 各ケースは `Given / When / Then` を固定する。
- user update / identity link / area write API は `ETag` / `If-Match`、transfer は body version を必ず通す。
- 業務結果の主観測点は `status`, `revision`, `assignments`, `offDutyEntries`, `pendingWeekRule`, `lifecycleStatus`, list `meta`。
- `User Management` と `Duty Assignment` の境界は、可能な限り `projection_user_directory` 経由の観測で確認する。
- 再配分アルゴリズムの細部順序は Domain テストに委譲し、API 統合テストでは externally visible な結果に絞る。

## 6. 完了条件

- 設計資料で定義された公開 / 内部 UseCase が、API 呼び出しまたは API 副作用として少なくとも 1 回は統合テストから到達される。
- `User Management` で確定した `UserId` が `Duty Assignment` の member 所属へ接続されるケースを少なくとも 1 本持つ。
- 各ジャーニーに happy path 1 本以上と、そのジャーニー固有の境界 / 失敗ケース 2 本以上を持つ。
