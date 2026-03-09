# お掃除当番システム ReadModel / CQRS 設計書（v1）

- Status: Draft
- Date: 2026-03-08
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/core-domain-repository-abstraction-v1.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/api-endpoint-design-v1.md`
  - `docs/readmodel-write-visibility-design-v1.md`
  - `docs/facility-management-bc-design-v1.md`
  - `docs/infrastructure-architecture-adr-v2.md`

## 1. 目的 / スコープ

本書は、GET 系エンドポイントを「集約再構築」ではなく「Projection を参照する ReadModel」から返すための設計を定義する。

対象:
- `GET /api/v1/facilities`
- `GET /api/v1/facilities/{facilityId}`
- `GET /api/v1/cleaning-areas`
- `GET /api/v1/cleaning-areas/{areaId}`
- `GET /api/v1/weekly-duty-plans`
- `GET /api/v1/weekly-duty-plans/{planId}`
- Query 側の Application 抽象、ReadModel 型、Infrastructure 実装方針
- 既存 Projection に対する不足項目の補完

非対象:
- コマンド側 Aggregate / Repository の再設計
- Projector のジョブ制御詳細
- Redis キャッシュ詳細
- 認可ポリシー詳細

## 2. 背景と現状ギャップ

現状の GET エンドポイントは、コマンド側 Repository から `LoadedAggregate<T>` を取得し、API レスポンスへ直接整形している。

その結果、以下の問題がある。

1. Query のために Aggregate を再構築しており、CQRS の責務分離が崩れている。
2. `ICleaningAreaRepository.ListAllAsync` と `IWeeklyDutyPlanRepository.ListAsync` が Query 需要を抱え込み、書き込み用抽象が肥大化している。
3. `docs/infrastructure-architecture-adr-v2.md` では Projection 優先を採用しているが、公開 GET API が未反映である。
4. 既存 Projection は参照の土台にはなるが、現行 API 契約をそのまま満たすには不足がある。

不足している主な点:
- `Facility` 詳細 / 一覧を返す Projection / ReadRepository がない
- `CleaningArea` の `facilityId` と Facility 名を返す ReadModel がない
- `CleaningArea` 詳細の `spots[]` を保持する Projection がない
- `WeeklyDutyPlan` の `ETag` / `version` 用 `aggregate_version` が Projection にない
- `GET /api/v1/weekly-duty-plans` の `createdAt` ソートを支える列がない
- カーソル実装が現在は実質 offset ベースで、設計書の opaque cursor 方針とずれている

## 3. 設計方針

### 3.1 CQRS の境界

- Command 側:
  - 既存どおり EventSourcing + Aggregate + 書き込み用 Repository を使う
  - `If-Match` による楽観排他は継続する
- Query 側:
  - Projection テーブルのみを参照する
  - Aggregate の復元は行わない
  - Application 層に Query Handler と ReadRepository 抽象を新設する

### 3.2 ReadModel と Projection の区別

- Projection:
  - PostgreSQL 上の read store
  - Projector が更新する永続表
- ReadModel:
  - Application 層が返す参照専用 DTO
  - API レスポンス形に近いが、HTTP 実装詳細には依存しない

Projection は永続化都合で分割してよい。ReadModel は API 利用単位で組み立てる。

### 3.3 レイヤ配置

ReadModel 関連の責務は以下へ配置する。

- `OsoujiSystem.Application`
  - Query Request / Handler
  - ReadRepository 抽象
  - ReadModel 型
- `OsoujiSystem.Infrastructure`
  - Projection を読む `Postgres*ReadRepository`
- `OsoujiSystem.WebApi`
  - クエリ文字列の parse
  - Query Request の送信
  - ReadModel から HTTP レスポンスへの最終整形

ReadRepository 抽象は Domain ではなく Application に置く。Read は業務不変条件の保護ではなく、ユースケース最適化の責務だからである。

## 4. Query ユースケース

GET 系は既存 Command UseCase と分離し、以下の Query Request を追加する。

### 4.1 Facility 系

1. `ListFacilitiesQuery`
- Input:
  - `string? Query`
  - `FacilityLifecycleStatus? Status`
  - `string? Cursor`
  - `int Limit`
  - `FacilitySortOrder Sort`
- Output:
  - `CursorPage<FacilityListItemReadModel>`

2. `GetFacilityQuery`
- Input:
  - `Guid FacilityId`
- Output:
  - `FacilityDetailReadModel?`

### 4.2 CleaningArea 系

3. `ListCleaningAreasQuery`
- Input:
  - `Guid? FacilityId`
  - `Guid? UserId`
  - `string? Cursor`
  - `int Limit`
  - `CleaningAreaSortOrder Sort`
- Output:
  - `CursorPage<CleaningAreaListItemReadModel>`

4. `GetCleaningAreaQuery`
- Input:
  - `Guid AreaId`
- Output:
  - `CleaningAreaDetailReadModel?`

### 4.3 WeeklyDutyPlan 系

5. `ListWeeklyDutyPlansQuery`
- Input:
  - `Guid? AreaId`
  - `string? WeekId`
  - `WeeklyPlanStatus? Status`
  - `string? Cursor`
  - `int Limit`
  - `WeeklyDutyPlanSortOrder Sort`
- Output:
  - `CursorPage<WeeklyDutyPlanListItemReadModel>`

6. `GetWeeklyDutyPlanQuery`
- Input:
  - `Guid PlanId`
- Output:
  - `WeeklyDutyPlanDetailReadModel?`

補足:
- Query の parse/validation は現行どおり WebApi で行う
- `404` 判定は Query 結果 `null` を API 層で NotFound へ変換する

## 5. ReadModel 定義

### 5.1 共通

```csharp
public sealed record CursorPage<T>(
    IReadOnlyList<T> Items,
    int Limit,
    bool HasNext,
    string? NextCursor);
```

```csharp
public sealed record WeekRuleReadModel(
    string StartDay,
    string StartTime,
    string TimeZoneId,
    string EffectiveFromWeek);
```

`WeekRuleReadModel` は API 契約で使う文字列表現をそのまま保持する。

### 5.2 Facility ReadModel

```csharp
public sealed record FacilityListItemReadModel(
    Guid Id,
    string FacilityCode,
    string Name,
    string TimeZoneId,
    string LifecycleStatus,
    long Version);

public sealed record FacilityDetailReadModel(
    Guid Id,
    string FacilityCode,
    string Name,
    string? Description,
    string TimeZoneId,
    string LifecycleStatus,
    long Version);
```

### 5.3 CleaningArea ReadModel

```csharp
public sealed record CleaningAreaListItemReadModel(
    Guid Id,
    Guid FacilityId,
    string Name,
    WeekRuleReadModel CurrentWeekRule,
    int MemberCount,
    int SpotCount,
    long Version);

public sealed record CleaningSpotReadModel(
    Guid Id,
    string Name,
    int SortOrder);

public sealed record AreaMemberReadModel(
    Guid Id,
    Guid UserId,
    string EmployeeNumber);

public sealed record CleaningAreaDetailReadModel(
    Guid Id,
    Guid FacilityId,
    string Name,
    WeekRuleReadModel CurrentWeekRule,
    WeekRuleReadModel? PendingWeekRule,
    int RotationCursor,
    IReadOnlyList<CleaningSpotReadModel> Spots,
    IReadOnlyList<AreaMemberReadModel> Members,
    long Version);
```

### 5.4 WeeklyDutyPlan ReadModel

```csharp
public sealed record WeeklyDutyPlanListItemReadModel(
    Guid Id,
    Guid AreaId,
    string WeekId,
    int Revision,
    string Status,
    long Version,
    DateTimeOffset CreatedAt);

public sealed record DutyAssignmentReadModel(
    Guid SpotId,
    Guid UserId);

public sealed record OffDutyEntryReadModel(
    Guid UserId);

public sealed record AssignmentPolicyReadModel(
    int FairnessWindowWeeks);

public sealed record WeeklyDutyPlanDetailReadModel(
    Guid Id,
    Guid AreaId,
    string WeekId,
    int Revision,
    string Status,
    AssignmentPolicyReadModel AssignmentPolicy,
    IReadOnlyList<DutyAssignmentReadModel> Assignments,
    IReadOnlyList<OffDutyEntryReadModel> OffDutyEntries,
    long Version);
```

設計意図:
- `Version` は `ETag` と書き込み時 `If-Match` の橋渡しに使う
- `Revision` はドメイン上の計画改訂番号であり、`Version` と分離する
- `CreatedAt` は一覧ソート専用の read 属性であり、詳細レスポンスには含めない

## 6. ReadRepository 抽象

Application 層に以下の抽象を追加する。

```csharp
public interface ICleaningAreaReadRepository
{
    Task<CursorPage<CleaningAreaListItemReadModel>> ListAsync(
        ListCleaningAreasQuery query,
        CancellationToken ct);

    Task<CleaningAreaDetailReadModel?> FindByIdAsync(
        Guid areaId,
        CancellationToken ct);
}

public interface IWeeklyDutyPlanReadRepository
{
    Task<CursorPage<WeeklyDutyPlanListItemReadModel>> ListAsync(
        ListWeeklyDutyPlansQuery query,
        CancellationToken ct);

    Task<WeeklyDutyPlanDetailReadModel?> FindByIdAsync(
        Guid planId,
        CancellationToken ct);
}
```

方針:
- ReadRepository は Query Request をそのまま受け取ってよい
- `LoadedAggregate<T>` は返さない
- 例外契約は既存 Repository と同様にインフラ障害時例外送出でよい

## 7. Projection スキーマ方針

## 7.1 継続利用する既存 Projection

- `projection_cleaning_areas`
- `projection_area_members`
- `projection_weekly_plans`
- `projection_weekly_plan_assignments`
- `projection_weekly_plan_offduty`
- `projection_user_weekly_workloads`

`projection_user_weekly_workloads` は履歴集計用のため、本書の GET エンドポイントでは直接使わない。

## 7.2 追加する Projection

`CleaningArea` 詳細 ReadModel のため、以下を追加する。

### `projection_cleaning_area_spots`

- `area_id UUID`
- `spot_id UUID`
- `spot_name TEXT`
- `sort_order INT`
- `updated_at TIMESTAMPTZ`
- `PRIMARY KEY (area_id, spot_id)`
- `INDEX (area_id, sort_order, spot_name, spot_id)`

用途:
- `GET /api/v1/cleaning-areas/{areaId}` の `spots[]`
- `GET /api/v1/cleaning-areas` の `spotCount`
- `GET /api/v1/weekly-duty-plans/{planId}` の assignment 並び順補助

## 7.3 既存 Projection への列追加

### `projection_weekly_plans`

以下を追加する。

- `aggregate_version BIGINT NOT NULL`
- `created_at TIMESTAMPTZ NOT NULL`

用途:
- `aggregate_version`: GET 詳細/一覧の `version` と `ETag`
- `created_at`: `sort=createdAt|-createdAt`

更新ルール:
- `aggregate_version` は stream version を投影する
- `created_at` は初回 insert 時に設定し、更新時は保持する

### `projection_cleaning_areas`

既存の `aggregate_version` を GET の `version` として正式利用する。

追加列は必須ではない。`memberCount` と `spotCount` は関連表から導出する。

## 8. Projector 更新方針

現在の `MainProjector` は stream ごとに最新 snapshot を再読込して Projection を全面更新している。本設計ではこの方針を維持し、Query 側だけを差し替える。

### 8.1 `ProjectCleaningAreaAsync`

更新対象:
- `projection_cleaning_areas`
- `projection_area_members`
- `projection_cleaning_area_spots` を追加

更新ルール:
- `projection_cleaning_areas` は現行どおり `aggregate_version` で条件付き upsert
- `projection_area_members` は area 全体の active 状態を snapshot 基準で再同期
- `projection_cleaning_area_spots` は対象 `area_id` の既存行を delete 後、snapshot の spot 一覧を insert する

理由:
- `CleaningArea` の spot 数は少数前提であり、全件入れ替えの方が projector 実装が単純
- 更新は 1 transaction 内で完結するため、ReadModel の部分更新は発生しない

### 8.2 `ProjectWeeklyPlanAsync`

更新対象:
- `projection_weekly_plans`
- `projection_weekly_plan_assignments`
- `projection_weekly_plan_offduty`
- `projection_user_weekly_workloads`

追加ルール:
- `projection_weekly_plans.aggregate_version` へ stream version を保存する
- `projection_weekly_plans.created_at` は insert 時のみ設定する

## 9. Query 実装方針

## 9.1 `GET /api/v1/cleaning-areas`

参照表:
- `projection_cleaning_areas a`
- `projection_area_members m`
- `projection_cleaning_area_spots s`

実装方針:
- 一覧の主テーブルは `projection_cleaning_areas`
- `userId` フィルタは `EXISTS` で `projection_area_members` を参照する
- `memberCount` / `spotCount` は page 対象 `area_id` に対する集計で補完する
- `sort=name|-name` は keyset cursor を採用する

cursor payload 例:

```json
{ "sort": "name", "name": "3F East", "id": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6" }
```

## 9.2 `GET /api/v1/cleaning-areas/{areaId}`

参照表:
- `projection_cleaning_areas`
- `projection_cleaning_area_spots`
- `projection_area_members`

取得順:
1. header を 1 件取得
2. `spots` を `sort_order ASC, spot_name ASC, spot_id ASC` で取得
3. `members` を `employee_number ASC, user_id ASC` で取得

`ETag` は `projection_cleaning_areas.aggregate_version` から生成する。

## 9.3 `GET /api/v1/weekly-duty-plans`

参照表:
- `projection_weekly_plans`

フィルタ:
- `areaId`
- `weekId` -> `week_year`, `week_number`
- `status`

ソート:
- `weekId`, `-weekId`
- `createdAt`, `-createdAt`

cursor payload 例:

```json
{ "sort": "-weekId", "weekYear": 2026, "weekNumber": 10, "id": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0" }
```

```json
{ "sort": "-createdAt", "createdAt": "2026-03-06T01:23:45.0000000Z", "id": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0" }
```

`areaId + weekId` は高選択なため、単一候補でも list 契約のまま返す。

## 9.4 `GET /api/v1/weekly-duty-plans/{planId}`

参照表:
- `projection_weekly_plans`
- `projection_weekly_plan_assignments`
- `projection_weekly_plan_offduty`
- 必要に応じて `projection_cleaning_area_spots`

取得順:
1. plan header を取得
2. assignments を取得
3. off-duty を取得

assignment の並び順:
- 既定は `spot_id ASC`
- UI 表示安定性を優先する場合、`projection_cleaning_area_spots` と join して `sort_order ASC, spot_id ASC` を採用する

`ETag` は `projection_weekly_plans.aggregate_version` から生成する。

## 10. WebApi 差し替え方針

各 GET エンドポイントは書き込み用 Repository を直接受け取らず、Mediator 経由で Query Handler を呼ぶ。

### 10.1 差し替え対象

- `CleaningAreaEndpoints.ListCleaningAreasAsync`
- `CleaningAreaEndpoints.GetCleaningAreaAsync`
- `WeeklyDutyPlanEndpoints.ListWeeklyDutyPlansAsync`
- `WeeklyDutyPlanEndpoints.GetWeeklyDutyPlanAsync`

### 10.2 差し替え後の責務

- WebApi:
  - parse / validation
  - Query Request 生成
  - `ETag` ヘッダー設定
  - `ReadModel -> JSON` 変換
- Application:
  - query orchestration
- Infrastructure:
  - projection SQL

Write エンドポイントの `If-Match` と再取得処理は当面そのままでよい。対象は GET 系のみとする。

## 11. 整合性モデル

本設計は CQRS のため、Command 完了直後の GET には projection lag があり得る。

運用上の扱い:
- Command 応答本文は即時結果として信頼してよい
- `Location` 先 GET は eventual consistency とする
- クライアントが直後整合を必要とする場合、Command 応答の `ETag` または `version` を保持し、GET の `ETag` が追いつくまで retry する

この方針は `docs/infrastructure-architecture-adr-v2.md` の `projection_checkpoints` / projection lag 監視と整合する。

## 12. テスト方針

### 12.1 Application / Infrastructure

1. `ICleaningAreaReadRepository.ListAsync`
- `userId` filter
- `sort=name|-name`
- cursor ページング
- `memberCount` / `spotCount` 集計

2. `ICleaningAreaReadRepository.FindByIdAsync`
- `spots` / `members` の順序
- `pendingWeekRule = null` ケース
- `version` 取得

3. `IWeeklyDutyPlanReadRepository.ListAsync`
- `areaId`, `weekId`, `status` filter
- `sort=weekId|-weekId|createdAt|-createdAt`
- cursor ページング

4. `IWeeklyDutyPlanReadRepository.FindByIdAsync`
- `assignments` / `offDutyEntries`
- `version` と `revision` の分離

### 12.2 WebApi

1. GET 詳細が Projection ベースでも既存 JSON 契約を維持する
2. GET 詳細の `ETag` が ReadModel `version` と一致する
3. GET 一覧が Aggregate を再構築せずとも既存 filter/sort 契約を満たす
4. Query 失敗時の `404` / validation が現行と同じになる

### 12.3 Projector

1. `projection_cleaning_area_spots` が area snapshot と一致する
2. `projection_weekly_plans.aggregate_version` が event stream version と一致する
3. 同一イベント再適用時も Projection が不整合にならない

## 13. 段階的移行手順

1. Migration を追加する
- `projection_cleaning_area_spots` 新設
- `projection_weekly_plans.aggregate_version` 追加
- `projection_weekly_plans.created_at` 追加

2. Projector を更新する
- `ProjectCleaningAreaAsync` に spots 反映を追加
- `ProjectWeeklyPlanAsync` に `aggregate_version` / `created_at` を追加

3. Application に Query Handler / ReadRepository 抽象 / ReadModel を追加する

4. Infrastructure に `PostgresCleaningAreaReadRepository` / `PostgresWeeklyDutyPlanReadRepository` を追加する

5. WebApi の GET エンドポイントを Query 経由へ切り替える

6. Integration Test を GET 중심に更新する

7. 安定化後、書き込み用 Repository から一覧系メソッドを削除する
- `ICleaningAreaRepository.ListAllAsync`
- `IWeeklyDutyPlanRepository.ListAsync`

`FindByIdAsync` は Command 側で必要なため維持する。

## 14. この設計で解消されること

1. 公開 GET API が Projection 優先という既存 ADR と一致する
2. Query が Aggregate 復元から切り離され、CQRS の責務が明確になる
3. `ETag` と `If-Match` を read/write 間で同じ `aggregate_version` に揃えられる
4. ReadModel の追加が Domain 抽象を汚さずに行える

## 15. 将来拡張

- `CleaningArea` / `WeeklyDutyPlan` 以外の参照 API も同じ Query パターンへ揃える
- ReadModel に対する Redis cache を `Query Handler` の背後へ追加する
- Projection を snapshot 再投影方式からイベント種別差分更新へ最適化する
- ReadModel 専用の監視指標として query latency / page scan 件数を追加する
