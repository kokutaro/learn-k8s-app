# お掃除当番システム Repository 抽象設計書（v1 / v3準拠）

## 1. 目的とスコープ

本書は、`core-domain-design-v3.md` で定義したドメインモデル操作を永続化するための最小レポジトリ抽象を定義する。

- 対象:
  - `CleaningArea` 集約の取得・保存
  - `WeeklyDutyPlan` 集約の取得・保存
  - 割り当て履歴（`AssignmentHistorySnapshot`）の取得
- 非対象:
  - 画面表示用の参照クエリ（ReadModel）
  - Outbox
  - UnitOfWork
  - 実DBスキーマ詳細

## 2. v3との対応方針（コマンド -> Repo操作）

| コマンド                                                                                                                          | 利用する抽象                                                                                                                  |
| --------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `RegisterCleaningArea`                                                                                                            | `ICleaningAreaRepository.AddAsync`                                                                                            |
| `ScheduleWeekRuleChange` / `AddCleaningSpot` / `RemoveCleaningSpot` / `AssignUserToArea` / `UnassignUserFromArea`                 | `ICleaningAreaRepository.FindByIdAsync` + `SaveAsync`                                                                         |
| `TransferUserToArea`                                                                                                              | `ICleaningAreaRepository.FindByIdAsync`（From/To）+ `SaveAsync`（両方）                                                       |
| `GenerateWeeklyPlan`                                                                                                              | `IWeeklyDutyPlanRepository.FindByAreaAndWeekAsync`（重複防止）+ `IAssignmentHistoryRepository.GetSnapshotsAsync` + `AddAsync` |
| `RebalanceForUserAssigned` / `RebalanceForUserUnassigned` / `RecalculateForSpotChanged` / `PublishWeeklyPlan` / `CloseWeeklyPlan` | `IWeeklyDutyPlanRepository.FindByIdAsync` + （必要時）`IAssignmentHistoryRepository.GetSnapshotsAsync` + `SaveAsync`          |

## 3. 抽象インターフェイス定義（C#シグネチャ）

`namespace OsoujiSystem.Domain.Repositories` を新設し、以下の型を配置する。

```csharp
using OsoujiSystem.Domain.DomainServices;
using OsoujiSystem.Domain.Entities.CleaningAreas;
using OsoujiSystem.Domain.Entities.WeeklyDutyPlans;
using OsoujiSystem.Domain.ValueObjects;

namespace OsoujiSystem.Domain.Repositories;

public readonly record struct AggregateVersion(long Value)
{
    public static AggregateVersion Initial => new(1);
    public AggregateVersion Next() => new(Value + 1);
}

public readonly record struct LoadedAggregate<TAggregate>(
    TAggregate Aggregate,
    AggregateVersion Version);

public interface ICleaningAreaRepository
{
    Task<LoadedAggregate<CleaningArea>?> FindByIdAsync(
        CleaningAreaId areaId,
        CancellationToken ct);

    Task<LoadedAggregate<CleaningArea>?> FindByUserIdAsync(
        UserId userId,
        CancellationToken ct);

    Task<IReadOnlyList<LoadedAggregate<CleaningArea>>> ListWeekRuleDueAsync(
        WeekId currentWeek,
        CancellationToken ct);

    Task AddAsync(
        CleaningArea aggregate,
        CancellationToken ct);

    Task SaveAsync(
        CleaningArea aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

public interface IWeeklyDutyPlanRepository
{
    Task<LoadedAggregate<WeeklyDutyPlan>?> FindByIdAsync(
        WeeklyDutyPlanId planId,
        CancellationToken ct);

    Task<LoadedAggregate<WeeklyDutyPlan>?> FindByAreaAndWeekAsync(
        CleaningAreaId areaId,
        WeekId weekId,
        CancellationToken ct);

    Task AddAsync(
        WeeklyDutyPlan aggregate,
        CancellationToken ct);

    Task SaveAsync(
        WeeklyDutyPlan aggregate,
        AggregateVersion expectedVersion,
        CancellationToken ct);
}

public interface IAssignmentHistoryRepository
{
    Task<IReadOnlyDictionary<UserId, AssignmentHistorySnapshot>> GetSnapshotsAsync(
        CleaningAreaId areaId,
        WeekId targetWeek,
        int windowWeeks,
        IReadOnlyCollection<UserId> userIds,
        CancellationToken ct);
}
```

## 4. 楽観排他ルール（`expectedVersion`）

- `SaveAsync` は必ず `expectedVersion` 比較を行う。
- 保存対象の永続化バージョンが `expectedVersion` と一致しない場合、保存は失敗とする。
- 失敗時は競合例外（例: `RepositoryConcurrencyException`）を送出する。
- 成功時は永続化バージョンを 1 進める。
- `AddAsync` は新規作成専用とし、同一識別子が既に存在する場合は重複例外（例: `RepositoryDuplicateException`）を送出する。

## 5. メソッド契約（事前条件・事後条件・エラー）

### 5.1 `Find*Async`

- 事前条件:
  - 引数IDは呼び出し側で妥当な値を渡す（`default`回避）。
- 事後条件:
  - 存在時は `LoadedAggregate<T>` を返す。
  - 未存在時は `null` を返す。
- エラー:
  - インフラ障害時は例外を送出（接続障害等）。

### 5.2 `AddAsync`

- 事前条件:
  - 追加対象は新規集約であること。
- 事後条件:
  - 集約状態が永続化される。
  - 次回 `FindByIdAsync` で取得可能になる。
- エラー:
  - 同一主キーが存在する場合 `RepositoryDuplicateException`。

### 5.3 `SaveAsync`

- 事前条件:
  - 呼び出し元は `Find*Async` で取得した `Version` を `expectedVersion` として渡す。
- 事後条件:
  - バージョン一致時のみ更新成功。
  - 成功後は永続化バージョンがインクリメントされる。
- エラー:
  - `expectedVersion` 不一致時 `RepositoryConcurrencyException`。

### 5.4 `IAssignmentHistoryRepository.GetSnapshotsAsync`

- 事前条件:
  - `windowWeeks` は正数。
  - `userIds` は対象メンバーのみを渡す。
- 事後条件:
  - 戻り値は `userIds` をキーに参照可能。
  - 履歴欠損ユーザーは `AssignedCountLast4Weeks = 0`、`ConsecutiveOffDutyWeeks = 0` で返す。
- エラー:
  - 集計不能なインフラ障害時は例外を送出。

## 6. 履歴取得契約（欠損時0補完）

`IAssignmentHistoryRepository` は、`targetWeek` の直前 `windowWeeks` 週を対象に `AssignmentHistorySnapshot` を返す。

- `AssignedCountLast4Weeks`:
  - 対象期間内で担当に選ばれた回数。
- `ConsecutiveOffDutyWeeks`:
  - `targetWeek` 直前から連続する OffDuty 週数。
- 欠損時初期値:
  - 対象ユーザーの履歴が存在しない場合でも辞書キーとして返し、両値を `0` とする。

## 7. 実装指針（RDB/NoSQL 共通）

- 契約優先:
  - DB種別に関わらず、`Find*`, `Add`, `Save` の戻り値・例外契約を維持する。
- 一意制約:
  - `WeeklyDutyPlan` は `(AreaId, WeekId)` 一意を必須にする。
- 競合制御:
  - RDB: `version` 列を `WHERE` 条件に含める。
  - NoSQL: ETag / 条件付き更新を利用する。
- 集約境界:
  - `CleaningArea` と `WeeklyDutyPlan` は独立して保存し、トランザクション結合は本書では扱わない。
- ドメインイベント:
  - 集約内 `DomainEvents` の配送方式は本書対象外（Outbox設計で扱う）。

## 8. 契約テスト観点（Repository Contract Test）

1. `FindByIdAsync` は未存在時 `null` を返す。  
2. `AddAsync` 後に `FindByIdAsync` で同一集約が復元できる。  
3. `SaveAsync` は正しい `expectedVersion` で成功し、Versionが進む。  
4. `SaveAsync` は古い `expectedVersion` で `RepositoryConcurrencyException` を返す。  
5. `FindByAreaAndWeekAsync` は同一 `(AreaId, WeekId)` を一意に返す。  
6. `GetSnapshotsAsync` は履歴欠損ユーザーを `AssignedCountLast4Weeks = 0` / `ConsecutiveOffDutyWeeks = 0` で返す。  
7. `FindByUserIdAsync` は所属中ユーザーのエリアを返し、未所属なら `null`。  
8. `ListWeekRuleDueAsync` は `PendingWeekRule.EffectiveFromWeek <= currentWeek` のみ返す。  

## 9. 非対象と将来拡張

### 非対象（本書）

- 画面表示用ReadModelのクエリ抽象
- UnitOfWork とトランザクション境界
- Outbox / イベント永続化・配送
- リトライ戦略、監査ログ詳細

### 将来拡張

- `RepositoryConcurrencyException` / `RepositoryDuplicateException` の正式型定義
- コマンドハンドラ層での横断的エラーマッピング（HTTP/メッセージング）
- ReadModel用 `IWeeklyDutyPlanReadRepository` など参照専用抽象

## 付録: 前提とデフォルト

- レポジトリ抽象はドメインモデル操作専用で、画面表示専用クエリは含めない。
- 楽観排他はすべての `SaveAsync` で必須。
- 履歴は `IAssignmentHistoryRepository` で取得し、欠損時0補完を固定契約とする。
- UnitOfWork / Outbox は別設計書で扱う。
