# お掃除当番システム Infrastructure Architecture ADR（v2 / Detailed Data & Reliability）

- Status: Accepted
- Date: 2026-03-05
- Supersedes: `docs/infrastructure-architecture-adr-v1.md`
- Decision Makers: Core/Platform Team
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/core-domain-repository-abstraction-v1.md`
  - `docs/application-usecase-design-v1.md`

## 1. 目的

v1 で決定した採用サービス（PostgreSQL + EventSourcing、Redis、Outbox + RabbitMQ）を前提に、以下の未確定事項を設計として確定する。

1. `event_store` / `snapshot` / `outbox` / `projection` の論理スキーマ
2. Projection 更新順序と再構築（リプレイ）手順
3. キャッシュ無効化失敗時の回復戦略

## 2. スコープ

対象:

- 論理データモデル（テーブル責務、主キー/一意制約、主要インデックス）
- Repository 契約とのマッピング
- イベント処理パイプライン（整合性・冪等性）

非対象:

- クラウドベンダー固有のマネージド設定値
- 物理チューニング（CPU/IOPS/パラメータ詳細）
- 監査ログ保持年数の最終決定（別 ADR）

## 3. 決定サマリ

| 領域            | 決定                                                                                 |
| --------------- | ------------------------------------------------------------------------------------ |
| Event Store     | PostgreSQL の append-only `event_store_events` を正本とする                          |
| Snapshot        | `event_store_snapshots` に Aggregate 単位の最新スナップショットを保持する            |
| Outbox          | `outbox_messages` を同一トランザクションで書き込み、後段で RabbitMQ に配信する       |
| Projection      | 用途別 Projection テーブルを持ち、API/Repository の参照は Projection を優先する      |
| Projection 順序 | `global_position` 昇順で単調処理し、`projection_checkpoints` で進捗管理する          |
| Cache           | Redis Cache-Aside + バージョンキー方式を採用し、削除失敗に依存しない整合性を確保する |

## 4. 論理スキーマ

## 4.1 Event Store

テーブル: `event_store_events`

主なカラム:

- `event_id UUID`（PK）
- `stream_id UUID`（Aggregate ID）
- `stream_type TEXT`（`cleaning_area` / `weekly_duty_plan`）
- `stream_version BIGINT`（1 始まり）
- `global_position BIGSERIAL`（全体順序）
- `event_type TEXT`（例: `WeeklyPlanGenerated`）
- `event_schema_version INT`（イベントスキーマ版）
- `payload JSONB`（イベント本体）
- `metadata JSONB`（`trace_id`, `causation_id`, `correlation_id`, `command_id`）
- `occurred_at TIMESTAMPTZ`
- `recorded_at TIMESTAMPTZ DEFAULT now()`

制約/インデックス:

- `PRIMARY KEY (event_id)`
- `UNIQUE (stream_id, stream_version)`（楽観排他の根拠）
- `UNIQUE (global_position)`
- `INDEX (stream_type, stream_id, stream_version DESC)`
- `INDEX (recorded_at)`

## 4.2 Snapshot

テーブル: `event_store_snapshots`

主なカラム:

- `stream_id UUID`（PK）
- `stream_type TEXT`
- `last_included_version BIGINT`
- `snapshot_payload JSONB`
- `updated_at TIMESTAMPTZ`

運用ルール:

- 1 Aggregate につき最新 1 レコードを保持（上書き）。
- 目安 50 イベントごとに再作成。
- Snapshot は最適化手段であり、正本は常に `event_store_events`。

## 4.3 Outbox

テーブル: `outbox_messages`

主なカラム:

- `message_id UUID`（PK）
- `source_event_id UUID`（`event_store_events.event_id`）
- `exchange_name TEXT`
- `routing_key TEXT`
- `payload JSONB`
- `headers JSONB`
- `available_at TIMESTAMPTZ`
- `published_at TIMESTAMPTZ NULL`
- `attempt_count INT DEFAULT 0`
- `last_error TEXT NULL`
- `created_at TIMESTAMPTZ DEFAULT now()`

制約/インデックス:

- `UNIQUE (source_event_id)`（同一イベント二重発行防止）
- `INDEX (published_at, available_at)`（未送信走査用）

## 4.4 Projection

用途別に分割し、更新責務を明確にする。

1. `projection_cleaning_areas`
   - `area_id UUID`（PK）
   - `area_name TEXT`
   - `current_week_rule JSONB`
   - `pending_week_rule JSONB NULL`
   - `rotation_cursor INT`
   - `aggregate_version BIGINT`
   - `updated_at TIMESTAMPTZ`

2. `projection_area_members`
   - `area_id UUID`
   - `user_id UUID`
   - `area_member_id UUID`
   - `employee_number CHAR(6)`
   - `is_active BOOLEAN`
   - `updated_at TIMESTAMPTZ`
   - `PRIMARY KEY (area_id, user_id)`
   - `UNIQUE (user_id) WHERE is_active = true`（重複所属防止の参照根拠）

3. `projection_weekly_plans`
   - `plan_id UUID`（PK）
   - `area_id UUID`
   - `week_year INT`
   - `week_number INT`
   - `revision INT`
   - `status SMALLINT`（Draft/Published/Closed）
   - `fairness_window_weeks INT`
   - `updated_at TIMESTAMPTZ`
   - `UNIQUE (area_id, week_year, week_number)`

4. `projection_weekly_plan_assignments`
   - `plan_id UUID`
   - `revision INT`
   - `spot_id UUID`
   - `user_id UUID`
   - `updated_at TIMESTAMPTZ`
   - `PRIMARY KEY (plan_id, spot_id)`

5. `projection_weekly_plan_offduty`
   - `plan_id UUID`
   - `revision INT`
   - `user_id UUID`
   - `updated_at TIMESTAMPTZ`
   - `PRIMARY KEY (plan_id, user_id)`

6. `projection_user_weekly_workloads`
   - `area_id UUID`
   - `user_id UUID`
   - `week_year INT`
   - `week_number INT`
   - `assigned_count INT`
   - `off_duty_count INT`（0 or 1）
   - `source_plan_id UUID`
   - `source_revision INT`
   - `updated_at TIMESTAMPTZ`
   - `PRIMARY KEY (area_id, user_id, week_year, week_number)`

7. `projection_checkpoints`
   - `projector_name TEXT`（PK）
   - `last_global_position BIGINT`
   - `updated_at TIMESTAMPTZ`

## 5. Repository 契約とのマッピング

1. `ICleaningAreaRepository.FindByIdAsync`
   - 読み出し: `event_store_snapshots` + `event_store_events`（`last_included_version` より後を再生）
   - 戻り値 `LoadedAggregate.Version` は `stream_version` の最新値

2. `ICleaningAreaRepository.FindByUserIdAsync`
   - 読み出し: `projection_area_members`（`is_active = true`）から `area_id` を特定し、該当 stream を復元

3. `ICleaningAreaRepository.ListAllAsync` / `ListWeekRuleDueAsync`
   - 読み出し: `projection_cleaning_areas`
   - due 判定は `pending_week_rule` の `effective_from_week <= currentWeek`

4. `IWeeklyDutyPlanRepository.FindByIdAsync` / `FindByAreaAndWeekAsync`
   - `FindByIdAsync`: stream 復元
   - `FindByAreaAndWeekAsync`: `projection_weekly_plans` の unique キーで `plan_id` を引き、stream 復元

5. `IAssignmentHistoryRepository.GetSnapshotsAsync`
   - `projection_user_weekly_workloads` から直近 `windowWeeks` を集計
   - 欠損ユーザーは `AssignedCountLast4Weeks = 0` / `ConsecutiveOffDutyWeeks = 0` 補完

## 6. 書き込みトランザクション規約

1. Aggregate を復元し、ドメイン操作で新イベント列を得る。
2. `event_store_events` へ `stream_version = expectedVersion + n` で append。
3. 必要に応じて `event_store_snapshots` を更新。
4. 外部公開対象イベントを `outbox_messages` へ書き込む。
5. 2-4 を同一 DB トランザクションで commit。

備考:

- `(stream_id, stream_version)` 制約違反は `RepositoryConcurrencyException` に変換する。
- `TransferUserToArea` は 2 stream への append を同一トランザクションで処理する。

## 7. Projection 更新順序と再構築

## 7.1 通常更新

1. Projector は `projection_checkpoints.last_global_position` 以降を `global_position ASC` で読む。
2. 1 バッチを 1 トランザクションで Projection に反映し、同トランザクション内で checkpoint を更新する。
3. 失敗時はロールバックし、同じ `global_position` から再開する（冪等）。

## 7.2 冪等更新ルール

- Update/Upsert は「`source_revision` が新しい場合のみ反映」の条件付き更新にする。
- 同一イベント再処理時に結果が変わらない SQL（`INSERT ... ON CONFLICT ... DO UPDATE WHERE ...`）を標準とする。

## 7.3 リプレイ（再構築）手順

1. 対象 projector を停止。
2. `projection_checkpoints` を初期化（`last_global_position = 0`）。
3. 対象 Projection テーブルを truncate。
4. `event_store_events` を先頭から順次適用。
5. 件数検証（計画件数、週次件数、ユーザー件数）を実施。
6. 問題なければ projector を通常モードで再開。

## 8. Redis キャッシュ戦略（失敗回復込み）

## 8.1 キー戦略

- 実体キー: `weekly-plan:{areaId}:{weekYear}-{weekNumber}:r{revision}`
- 参照キー: `weekly-plan:{areaId}:{weekYear}-{weekNumber}:latest`

API 読み出し:

1. `projection_weekly_plans` から最新 `revision` を取得
2. 実体キーを組み立てて Redis を参照
3. ミス時は Projection から再構築して格納（Cache-Aside）

## 8.2 無効化方針

- 原則は「削除」ではなく「新 revision キーの作成」で整合性を保つ。
- 旧 revision キー削除は best-effort（失敗しても整合性は崩れない）。

## 8.3 削除失敗時の回復

- `cache_invalidation_tasks` テーブルを持ち、削除失敗キーを記録して再試行する。
- 再試行は指数バックオフ（例: 1m, 5m, 15m, 1h, 6h）。
- それでも失敗したキーは TTL 失効に任せる（初期 TTL 5分）。

`cache_invalidation_tasks` 主なカラム:

- `task_id UUID`（PK）
- `cache_key TEXT`
- `reason_global_position BIGINT`
- `retry_count INT`
- `next_retry_at TIMESTAMPTZ`
- `last_error TEXT NULL`
- `resolved_at TIMESTAMPTZ NULL`

## 9. イベントスキーマ進化ルール

1. `event_schema_version` を必須化する。
2. 破壊的変更は新バージョンイベント型として追加し、既存型は維持する。
3. Projector/復元処理は upcaster を通して最新ドメイン形式に正規化する。
4. 互換性テスト（旧イベント再生テスト）を CI 必須にする。

## 10. この決定の影響

利点:

1. Repository 契約に直接対応する保存/参照設計が揃う。
2. 投影遅延・再構築・キャッシュ失敗を運用手順として扱える。
3. EventSourcing の監査性を維持しつつ API 性能を確保できる。

トレードオフ:

1. Projection とキャッシュの二段運用により運用コンポーネントが増える。
2. イベントスキーマ互換運用（upcaster/リプレイ検証）が必須になる。

## 11. 次 ADR への持ち越し

1. 監査ログ保持年数、PII 匿名化ポリシーの確定
2. RabbitMQ トポロジ（Exchange/Queue/Retry/DLQ）の詳細定義
3. SLI/SLO の目標値（p95, lag, エラー率）確定
