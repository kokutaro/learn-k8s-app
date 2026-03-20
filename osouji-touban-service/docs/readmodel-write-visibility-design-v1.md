# お掃除当番システム 更新系処理時の ReadModel 可視化待機設計書（v1）

- Status: Draft
- Date: 2026-03-09
- Related:
  - `docs/application-usecase-design-v1.md`
  - `docs/api-endpoint-design-v1.md`
  - `docs/readmodel-cqrs-design-v1.md`
  - `docs/infrastructure-architecture-adr-v5.md`
  - `docs/infrastructure-implementation-plan-v1.md`

## 1. 目的 / スコープ

本書は、公開更新 API の完了応答を返す前に、対応する ReadModel(Projection) が UI から安全に読める状態まで待機するための設計を定義する。

対象:

- 公開 API の更新系 endpoint (`POST` / `PUT` / `DELETE`)
- 更新完了後の Projection 追従待機
- ReadModel cache を含む「読める状態」の定義
- `202 Accepted` へのフォールバック契約
- Application / Infrastructure / WebApi の責務分担

非対象:

- Domain ルール 変更
- Projector 自体の同期実行化
- WebSocket / SSE による push 通知
- 内部 batch endpoint (`/api/v1/internal/*`) の待機保証

## 2. 背景

現状の write path は以下である。

1. WebApi が Application UseCase を実行する
2. Event Store に event が commit される
3. API は `200` / `201` / `204` を返す
4. 非同期 `MainProjector` が Projection を更新する
5. Projector が ReadModel cache の pointer / namespace を更新する

この構成では、更新成功直後に UI が通常の `GET` を行うと、Projection 未反映または cache 未更新により古い状態を読むことがある。

既存 ADR v5 はこの挙動をトレードオフとして許容しているが、対話的 UI では以下の問題になる。

1. 成功 toast の直後に一覧・詳細を再取得すると、作成・更新結果が見えない
2. 作成直後の詳細取得で一時 `404` になることがある
3. 更新完了と見なした時点の UX と、ReadModel の可視化時点がずれている

## 3. 要求

### 3.1. 機能要求

1. 公開更新 API は、ReadModel が可視化済みなら従来どおり `200` / `201` / `204` を返す
2. 可視化待機が timeout した場合、コマンド自体は再実行せず `202 Accepted` を返す
3. `202 Accepted` の応答だけで、クライアントは対象 resource と再取得先を把握できる
4. 複数集約更新でも、当該 request で commit された最後の event までを待機対象にできる

### 3.2. 非機能要求

1. CQRS の境界を崩さない。GET は引き続き Projection / ReadRepository を使う
2. Projector の非同期性は維持する
3. 待機は上限時間付きで、障害時に API request を無限待機させない
4. Redis 障害や cache invalidation 失敗時に false success を返さない
5. 既存の optimistic concurrency (`ETag` / `If-Match`) は維持する

## 4. 候補案

### 4.1. 案A: write request 内で projector を同期実行する

概要:

- command commit 後に API thread が projector を直接回し、必要な Projection 更新を同期で終わらせる

却下理由:

- write path が projector の batch 制御と強結合になる
- request ごとに projector を起動すると worker と責務競合する
- 複数 stream / batch / cache invalidation の責務が WebApi 側へ漏れる
- 背景 worker を前提にした現行運用と整合しない

### 4.2. 案B: `projection_checkpoints.last_global_position` だけを待つ

概要:

- request が commit した最後の `global_position` を求め、API は `projection_checkpoints` がそこへ到達するまで poll する

利点:

- 実装が比較的軽い
- Projector の順序保証をそのまま使える

不足:

- 現行実装では cache invalidation が checkpoint 更新後に行われる
- checkpoint 到達直後でも stale cache を読む狭い窓が残る
- Redis 失敗時に checkpoint だけ進み、API が false success を返す危険がある

### 4.3. 案C: Projection + ReadModel cache を含む「可視化 checkpoint」を待つ

概要:

- request が commit した最後の `global_position` を求める
- Projector は Projection 反映後、ReadModel cache invalidation の成功状況を踏まえて `readmodel_visibility_checkpoints` を進める
- API はこの visibility checkpoint が target position に到達するまで待機する

利点:

- 「通常の GET で見えること」を成功条件にできる
- cache invalidation 失敗時に `202 Accepted` へ安全に落とせる
- 非同期 projector と CQRS を維持したまま UI の read-after-write を強化できる

採用:

- 本書では案Cを正式採用する

## 5. 採用設計の全体像

### 5.1. 用語

- `CommittedGlobalPosition`:
  - ある write request が commit した event のうち最大の `event_store_events.global_position`
- `ProjectionCheckpoint`:
  - `projection_checkpoints.last_global_position`
- `ReadModelVisibilityCheckpoint`:
  - ReadModel cache も含めて通常 GET で可視化済みと判断できる最大 global position

保証したい条件:

`ReadModelVisibilityCheckpoint >= CommittedGlobalPosition`

この条件が満たされた request だけが最終成功 (`200` / `201` / `204`) を返す。

### 5.2. 対象 endpoint

対象:

- `/api/v1/facilities/*` の更新系
- `/api/v1/cleaning-areas/*` の更新系
- `/api/v1/area-member-transfers`
- `/api/v1/weekly-duty-plans/*` の更新系
- `/api/v1/users/*` の更新系

非対象:

- `/api/v1/internal/*`
- projector / worker / integration consumer が起こす内部書き込み

理由:

- 本機能は UI の read-after-write 体験改善が主目的であり、内部 batch に同じ待機を強制すると処理時間と失敗面が不必要に増えるため

## 6. API 契約

### 6.1. 成功時

ReadModel が可視化済みであれば、各 endpoint は現行どおりの成功コードを返す。

- `POST` create: `201 Created`
- `PUT` / 状態変更: `200 OK`
- `DELETE`: `204 No Content`

追加ヘッダー:

- `ETag`: 現行どおり。新しい aggregate version が分かる endpoint は維持する
- `Location`: create 系は現行どおり
- `X-ReadModel-Visibility: ready`

### 6.2. timeout 時

command commit は成功したが可視化待機が timeout した場合、最終成功ではなく `202 Accepted` を返す。

ヘッダー:

- `Retry-After: 1`
- `Location`: 作成/更新対象 resource の取得先
- `ETag`: 新 aggregate version が分かる endpoint は付与する
- `X-ReadModel-Visibility: pending`

body の共通形:

```json
{
  "data": {
    "resourceId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "location": "/api/v1/cleaning-areas/8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "readModelStatus": "pending"
  }
}
```

補足:

- `202` は「command 未完了」ではなく、「command は commit 済みだが、通常 GET による観測保証はまだない」を意味する
- クライアントは再送ではなく `Location` の `GET` 再試行へ進む

### 6.3. OpenAPI / UI 影響

各公開 mutation endpoint は `202 Accepted` を追加定義する。

UI 方針:

1. `200` / `201` / `204` の場合は従来どおり成功扱い
2. `202` の場合は「反映待ち」と表示し、`Retry-After` 後に `Location` を `GET` する
3. `202` に対して同じ command を再送しない

## 7. Application 層設計

### 7.1. 新規 abstraction

`OsoujiSystem.Application` に以下を追加する。

```csharp
public readonly record struct ReadModelConsistencyToken(long RequiredGlobalPosition);

public interface IReadModelConsistencyContextAccessor
{
    bool TryGet(out ReadModelConsistencyToken token);
    void Set(ReadModelConsistencyToken token);
    void Clear();
}

public interface IReadModelVisibilityWaiter
{
    Task<ReadModelVisibilityWaitResult> WaitUntilVisibleAsync(
        ReadModelConsistencyToken token,
        CancellationToken ct);
}

public readonly record struct ReadModelVisibilityWaitResult(
    bool IsVisible,
    bool TimedOut,
    TimeSpan Waited);
```

責務:

- UseCase は token の存在を意識しない
- WebApi は mutation 成功後に accessor から token を取得し、必要時のみ waiter を呼ぶ
- token がなければ待機は bypass する

### 7.2. WebApi での適用位置

公開 mutation endpoint 共通 helper を追加し、以下の順で処理する。

1. command 実行
2. `ApplicationResult` が失敗なら従来どおり error を返す
3. 成功時に `IReadModelConsistencyContextAccessor` から token を取得
4. token があれば `IReadModelVisibilityWaiter` で待機
5. visible なら従来の success result を返す
6. timeout なら `202 Accepted` result を返す

この責務は endpoint ごとに重複させず、`ApiHttpResults` か専用 helper に集約する。

## 8. Infrastructure 層設計

### 8.1. request が commit した `global_position` の確定

現状の `IEventWriteContextAccessor` は `eventId` / `streamVersion` までしか保持していない。
本設計では以下のどちらかで `CommittedGlobalPosition` を取得する。

推奨:

- `AppendEventsAsync` の insert を `RETURNING event_id, global_position` 付きに変更する
- `EventWriteMetadata` に `GlobalPosition` を追加する
- transaction 完了時に accessor から最大 `GlobalPosition` を取得する

代替:

- 既知の `eventId` 群を使って commit 後に `event_store_events` を再検索し、`MAX(global_position)` を求める

採用:

- 余計な再読込を避けるため `RETURNING` 方式を推奨する

### 8.2. transaction 完了後の token 発行

`NpgsqlApplicationTransaction` は outermost transaction の commit 成功後に、当該 request の最大 `GlobalPosition` から `ReadModelConsistencyToken` を作成し、`IReadModelConsistencyContextAccessor` へ保存する。

ネストされた transaction の扱い:

- 既存どおり outer transaction に合流する
- token は outer transaction 全体で 1 つ、`Max(GlobalPosition)` を採用する

これにより、`AssignUserToArea -> RebalanceForUserAssigned` のようなイベント連鎖で複数 stream が更新されても、単一 token で待機できる。

### 8.3. ReadModel 可視化 checkpoint

新テーブルを追加する。

```sql
CREATE TABLE readmodel_visibility_checkpoints (
    projector_name TEXT PRIMARY KEY,
    last_visible_global_position BIGINT NOT NULL CHECK (last_visible_global_position >= 0),
    updated_at TIMESTAMPTZ NOT NULL
);
```

意味:

- `last_visible_global_position` 以下の event は、Projection 反映と ReadModel cache invalidation の両方が完了し、通常 GET で観測可能である

### 8.4. ReadModel cache invalidation 失敗の追跡

既存の `cache_invalidation_tasks` は aggregate cache 用に使われており、`reason_global_position` に stream version を入れている箇所がある。
このまま ReadModel 可視化判定へ再利用すると意味が衝突するため、ReadModel 用に別テーブルを持つ。

```sql
CREATE TABLE readmodel_cache_invalidation_tasks (
    task_id UUID PRIMARY KEY,
    projector_name TEXT NOT NULL,
    cache_key TEXT NOT NULL,
    reason_global_position BIGINT NOT NULL CHECK (reason_global_position >= 0),
    retry_count INT NOT NULL CHECK (retry_count >= 0),
    next_retry_at TIMESTAMPTZ NOT NULL,
    last_error TEXT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    resolved_at TIMESTAMPTZ NULL
);
```

index:

- `(projector_name, next_retry_at)` for due scan
- `(projector_name, reason_global_position)` for visibility advance
- unique `(cache_key, reason_global_position)`

### 8.5. Projector の処理順

`MainProjector.RunBatchAsync` の batch 完了順を以下へ変更する。

1. event batch を global position 昇順で読む
2. Projection table を更新する
3. `projection_checkpoints.last_global_position = batchLastPosition` を transaction 内で commit する
4. batch で影響した ReadModel cache key を即時 invalidation する
5. 失敗した key は `readmodel_cache_invalidation_tasks` に enqueue する
6. `TryAdvanceReadModelVisibilityCheckpointAsync(projectorName)` を実行する

### 8.6. visibility checkpoint の進め方

advance 規則:

1. `projection_checkpoints.last_global_position` を `P` とする
2. 未解決 `readmodel_cache_invalidation_tasks` のうち最小 `reason_global_position` を `F` とする
3. 未解決 task がなければ `last_visible_global_position = P`
4. 未解決 task があれば `last_visible_global_position = min(P, F - 1)`

意味:

- unresolved な invalidation task がある最初の位置までは「通常 GET の可視性」を保証しない
- それ以前は連続的に可視化済みとみなせる

この advance は以下のタイミングで試行する。

- projector が batch を処理した直後
- readmodel cache invalidation recovery worker が task を解消した直後

### 8.7. 復旧 worker

`CacheInvalidationRecoveryWorker` とは別に、`ReadModelCacheInvalidationRecoveryWorker` を追加する。

責務:

- `readmodel_cache_invalidation_tasks` を再試行する
- 成功時に task を resolve する
- resolve 後に visibility checkpoint advance を再試行する

使用 cache:

- `IReadModelCache`

理由:

- aggregate cache recovery と ReadModel 可視化責務を分離するため

### 8.8. Waiter 実装

`PostgresReadModelVisibilityWaiter` を実装する。

処理:

1. `readmodel_visibility_checkpoints` を読む
2. `last_visible_global_position >= token.RequiredGlobalPosition` なら成功
3. そうでなければ短い interval で poll する
4. timeout 到達で `TimedOut = true`

設定値:

- `Infrastructure:ProjectionVisibility:WaitTimeoutMs` 例: 3000
- `Infrastructure:ProjectionVisibility:PollIntervalMs` 例: 50
- `Infrastructure:ProjectionVisibility:Enabled` 例: true

## 9. WebApi 応答設計

### 9.1. create 系

`201 Created` 条件:

- token なし、または visibility wait 成功

`202 Accepted` 条件:

- create command は commit 済み
- `Location` は新 resource URL
- body には `resourceId` と `location` を含める

### 9.2. update / activation / publication 系

`200 OK` 条件:

- visibility wait 成功

`202 Accepted` 条件:

- command は commit 済み
- body には `resourceId`, `location`, 必要に応じて `version` を含める

### 9.3. delete 系

`204 No Content` 条件:

- visibility wait 成功

`202 Accepted` 条件:

- delete は commit 済みだが一覧/詳細 GET の可視化待機が timeout
- body には削除対象 id と関連一覧 URL を返す

補足:

- delete timeout 時に `204` を返すと UI が古い一覧を再表示するため、本設計では `202` を返す

## 10. 監視 / メトリクス

追加メトリクス:

1. `osouji_readmodel_visibility_wait_duration_seconds{endpoint,result}`
   - `visible` / `timeout` / `bypass`

2. `osouji_readmodel_visibility_wait_requests_total{endpoint,result}`

3. `osouji_readmodel_visibility_checkpoint_gap{projector}`
   - `projection_checkpoint - visibility_checkpoint`

4. `osouji_readmodel_cache_invalidation_tasks_pending{projector}`

運用アラート例:

- `timeout` 比率が閾値超過
- visibility checkpoint gap が継続的に拡大
- readmodel invalidation task が長時間未解消

## 11. テスト計画

### 11.1. Application / Infrastructure

1. 1 request で複数 stream を更新したとき、最大 `GlobalPosition` が token になる
2. visibility checkpoint advance が未解決 task の直前で止まる
3. task 解消後に visibility checkpoint が再前進する
4. waiter が visible / timeout を正しく返す

### 11.2. WebApi 統合

1. projector が即追従できる場合、更新 endpoint は従来どおり `200` / `201` / `204`
2. projector を止めた状態では mutation endpoint が `202 Accepted` を返す
3. `202` 後に `DrainProjectionAsync` と recovery 実行後、`GET` が最新状態を返す
4. create 直後に stale `404` を返さない
5. delete timeout 時に `204` ではなく `202` になる

テスト fixture 追加:

- `DrainReadModelVisibilityAsync`
- readmodel cache invalidation 失敗を疑似的に起こす fake cache

## 12. 段階導入

### 12.1. Phase 1

- token 発行
- visibility checkpoint table
- projector wait / `202 Accepted`
- create / update 系 endpoint へ適用

### 12.2. Phase 2

- delete 系 endpoint へ適用
- readmodel cache invalidation recovery worker
- 監視メトリクス追加

### 12.3. Phase 3

- UI 側で `202` の反映待ち表示と `Location` poll を標準化
- timeout 閾値の実測調整

## 13. トレードオフ

利点:

1. 成功応答と「通常 GET で読める時点」を揃えられる
2. 非同期 projector と CQRS を維持できる
3. Redis / cache 障害時も false success ではなく `202` へ落とせる

コスト:

1. 公開 mutation API の p95 / p99 latency は増える
2. visibility checkpoint と recovery worker の追加管理が必要
3. `202` handling を UI と OpenAPI に追加する必要がある

## 14. 採用判断

推奨実装は案Cである。

理由:

1. UI 問題の本質は Projection だけでなく ReadModel cache 可視化ギャップも含むため
2. command 成功と read success を分離した `202 Accepted` fallback が最も安全だから
3. 既存 ADR v5 の非同期 projector 前提を壊さずに、対話的 read-after-write を強化できるから

## 15. 実装タスク分解

本章は、設計を実装へ落とすための最小実行単位を定義する。

### 15.1. 実装順序

1. 永続化トークン基盤
2. visibility checkpoint 永続化
3. waiter と option
4. projector / recovery worker
5. WebApi mutation 共通化
6. endpoint 適用
7. テスト拡張
8. メトリクスと運用調整

### 15.2. チケット一覧

#### 15.2.1. RMV-01: Projection visibility option 追加

対象:

- `src/OsoujiSystem.Infrastructure/Options/InfrastructureOptions.cs`
- `src/OsoujiSystem.WebApi/appsettings*.json`

作業:

- `Infrastructure:ProjectionVisibility` セクション追加
- `Enabled`, `WaitTimeoutMs`, `PollIntervalMs` を定義
- options validation を追加

完了条件:

- option が DI で bind / validate される
- 既定値は feature off でも安全に起動できる

依存:

- なし

#### 15.2.2. RMV-02: Event write metadata に `GlobalPosition` を追加

対象:

- `src/OsoujiSystem.Infrastructure/Persistence/Postgres/PostgresRepositoryBase.cs`
- `src/OsoujiSystem.Infrastructure/Persistence/Postgres/IEventWriteContextAccessor.cs`
- `src/OsoujiSystem.Infrastructure/Persistence/Postgres/AsyncLocalEventWriteContextAccessor.cs`

作業:

- event insert を `RETURNING event_id, global_position` で取得する形へ変更
- `EventWriteMetadata` に `GlobalPosition` を追加
- accessor が request 内の最大 `GlobalPosition` を返せるようにする

完了条件:

- 単一 aggregate 更新で最大 `GlobalPosition` を取得できる
- 複数 event append でも最大値が欠落しない

依存:

- RMV-01

#### 15.2.3. RMV-03: ReadModel consistency context accessor 追加

対象:

- `src/OsoujiSystem.Application`
- `src/OsoujiSystem.Infrastructure`

作業:

- `ReadModelConsistencyToken`
- `IReadModelConsistencyContextAccessor`
- AsyncLocal 実装

完了条件:

- request スコープ内で token を set / get / clear できる

依存:

- RMV-02

#### 15.2.4. RMV-04: transaction commit 後 token 発行

対象:

- `src/OsoujiSystem.Infrastructure/Persistence/Postgres/NpgsqlApplicationTransaction.cs`

作業:

- outermost transaction commit 成功後に最大 `GlobalPosition` から token を生成
- rollback 時は token を残さない
- nested transaction 時は outer transaction のみ publish

完了条件:

- 1 request 1 token で管理される
- event 未発生 request では token が生成されない

依存:

- RMV-03

#### 15.2.5. RMV-05: visibility checkpoint 用 migration 追加

対象:

- `src/OsoujiSystem.Infrastructure/Migrations/*`

作業:

- `readmodel_visibility_checkpoints`
- `readmodel_cache_invalidation_tasks`
- 初期 seed (`main_projector`, 0)

完了条件:

- migration 適用後に新規テーブル・index が存在する
- projector 名の seed が入る

依存:

- RMV-01

#### 15.2.6. RMV-06: visibility checkpoint repository 実装

対象:

- `src/OsoujiSystem.Infrastructure/Projection` または近傍 namespace

作業:

- checkpoint 読み取り / upsert 抽象を追加
- unresolved invalidation task 最小位置を取得する query を追加
- visibility advance ロジックをサービス化

完了条件:

- `ProjectionCheckpoint`, `VisibilityCheckpoint`, `MinPendingTaskPosition` から次の visible position を計算できる

依存:

- RMV-05

#### 15.2.7. RMV-07: ReadModel cache invalidation task repository / worker 実装

対象:

- `src/OsoujiSystem.Infrastructure/Cache`

作業:

- `IReadModelCacheInvalidationTaskRepository`
- `ReadModelCacheInvalidationTaskRepository`
- `ReadModelCacheInvalidationRecoveryWorker`

完了条件:

- task enqueue / resolve / retry ができる
- resolve 後に visibility checkpoint advance が再実行される

依存:

- RMV-05
- RMV-06

#### 15.2.8. RMV-08: projector に visibility advance を組み込む

対象:

- `src/OsoujiSystem.Infrastructure/Projection/MainProjectionWorker.cs`
- ReadModel cache key factory / cache abstraction 周辺

作業:

- batch ごとの ReadModel cache invalidation 失敗を新 task table に enqueue
- invalidation 後に visibility checkpoint advance
- cache invalidation 完全成功時のみ `last_visible_global_position` を batch last まで進める

完了条件:

- projector batch 成功後に visibility checkpoint が前進する
- invalidation failure があるときは該当前で止まる

依存:

- RMV-06
- RMV-07

#### 15.2.9. RMV-09: waiter abstraction と Postgres 実装追加

対象:

- `src/OsoujiSystem.Application`
- `src/OsoujiSystem.Infrastructure`

作業:

- `IReadModelVisibilityWaiter`
- `ReadModelVisibilityWaitResult`
- `PostgresReadModelVisibilityWaiter`

完了条件:

- timeout / visible / bypass を判定できる
- cancellation を尊重する

依存:

- RMV-05
- RMV-06

#### 15.2.10. RMV-10: mutation endpoint 共通 helper 追加

対象:

- `src/OsoujiSystem.WebApi/Endpoints/Support`

作業:

- ApplicationResult 成功後に token を取り wait する helper を追加
- `202 Accepted` の共通 response body / header builder を追加

完了条件:

- endpoint ごとの待機処理重複がない
- `Location`, `Retry-After`, `X-ReadModel-Visibility` を一元生成できる

依存:

- RMV-04
- RMV-09

#### 15.2.11. RMV-11: create / update 系 endpoint へ適用

対象:

- `src/OsoujiSystem.WebApi/Endpoints/Facilities/FacilityEndpoints.cs`
- `src/OsoujiSystem.WebApi/Endpoints/CleaningAreas/CleaningAreaEndpoints.cs`
- `src/OsoujiSystem.WebApi/Endpoints/WeeklyDutyPlans/WeeklyDutyPlanEndpoints.cs`
- `src/OsoujiSystem.WebApi/Endpoints/Users/UserManagementEndpoints.cs`

作業:

- `POST`, `PUT`, status change 系 endpoint を共通 helper 利用へ寄せる
- 既存 `ETag` / `Location` を維持しながら `202` fallback を追加

完了条件:

- create / update / publish / close / activation / lifecycle 系で `202` を返せる
- `If-Match` / `ETag` 契約を壊さない

依存:

- RMV-10

#### 15.2.12. RMV-12: delete 系 endpoint へ適用

対象:

- `RemoveCleaningSpot`
- `UnassignUserFromArea`
- 将来的な delete endpoint 全般

作業:

- `204 No Content` / `202 Accepted` の切替を導入
- delete timeout 時の body / location 方針を統一

完了条件:

- delete timeout 時に stale UI を誘発する `204` を返さない

依存:

- RMV-10

#### 15.2.13. RMV-13: telemetry / metrics 追加

対象:

- `src/OsoujiSystem.Infrastructure/Observability`

作業:

- wait duration / requests total
- visibility checkpoint gap
- readmodel invalidation tasks pending

完了条件:

- 主要可観測値が Prometheus に出る

依存:

- RMV-08
- RMV-09

#### 15.2.14. RMV-14: Infrastructure テスト追加

対象:

- `tests/OsoujiSystem.Infrastructure.Tests`

作業:

- token 発行テスト
- visibility advance 計算テスト
- invalidation retry / resolve テスト
- waiter timeout / success テスト

完了条件:

- visibility barrier のコアロジックが unit / integration で保証される

依存:

- RMV-04
- RMV-08
- RMV-09

#### 15.2.15. RMV-15: WebApi 統合テスト追加

対象:

- `tests/OsoujiSystem.WebApi.Tests`

作業:

- projector 停止時 `202`
- projector drain 後の `GET` 成功
- create 直後 stale `404` 防止
- delete timeout `202`

完了条件:

- 公開 API 契約が統合テストで固定される

依存:

- RMV-11
- RMV-12

#### 15.2.16. RMV-16: OpenAPI / HTTP サンプル更新

対象:

- WebApi OpenAPI schema
- `src/OsoujiSystem.WebApi/OsoujiSystem.WebApi.http`
- 関連 docs

作業:

- mutation endpoint に `202 Accepted` を追加
- `Retry-After`, `X-ReadModel-Visibility` サンプルを追記

完了条件:

- 手動検証とクライアント実装が迷わない

依存:

- RMV-11
- RMV-12

### 15.3. 推奨スプリント分割

#### 15.3.1. Sprint 1: 基盤

- RMV-01
- RMV-02
- RMV-03
- RMV-04
- RMV-05
- RMV-06

成果:

- token 発行と visibility checkpoint 永続化が入る

#### 15.3.2. Sprint 2: 実行経路

- RMV-07
- RMV-08
- RMV-09
- RMV-10
- RMV-11

成果:

- create / update 系 endpoint が `202` fallback 付きで動く

#### 15.3.3. Sprint 3: 完成

- RMV-12
- RMV-13
- RMV-14
- RMV-15
- RMV-16

成果:

- delete、監視、OpenAPI、テストまで含めて完結する

### 15.4. 先行実装の最小スコープ

最短でユーザー体験改善を出すなら、先に以下だけ実装してもよい。

1. RMV-01
2. RMV-02
3. RMV-03
4. RMV-04
5. RMV-05
6. RMV-06
7. RMV-08
8. RMV-09
9. RMV-10
10. RMV-11

この最小スコープでは delete と監視は後回しだが、主訴である「POST/PUT 成功直後の GET 漏れ」は解消できる。

### 15.5. Definition of Done

1. 公開 create / update endpoint が visibility wait 成功時のみ `200/201` を返す
2. wait timeout 時は `202 Accepted` と `Location` / `Retry-After` を返す
3. token は request 全体の最大 `GlobalPosition` に基づく
4. ReadModel cache invalidation failure 中は visibility checkpoint が該当位置を越えない
5. WebApi 統合テストで stale read 再現ケースが固定される
6. full `.NET` pipeline (`dotnet restore`, `dotnet build`, `dotnet test`) が通る
