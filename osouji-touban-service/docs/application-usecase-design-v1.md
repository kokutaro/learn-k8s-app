# お掃除当番システム Application層 / ユースケース設計書（v1）

## 1. 目的 / スコープ

本書は `core-domain-design-v3.md` と `core-domain-repository-abstraction-v1.md` を前提に、Application 層の責務・ユースケース・イベント連携・エラーマッピングを定義する。

対象:

- ユースケース入力/出力とハンドラ責務
- MediatR を使った同期 In-Process 連携
- DomainError/Repository例外の Application/HTTP マッピング
- Domain `Commands` から Application Request への移行

非対象:

- ReadModel/参照クエリ最適化
- Outbox 実装
- 認可ポリシー詳細

## 2. 層責務と依存ルール

依存方向:

- `OsoujiSystem.WebApi -> OsoujiSystem.Application -> OsoujiSystem.Domain`

責務:

- Domain: 集約・ValueObject・DomainService・DomainEvent
- Application: UseCase orchestration、トランザクション境界、Repository/Dispatcher 呼び出し
- WebApi: 入出力変換、HTTP 応答、認証認可

補足:

- Domain 配下の `Commands/*.cs` は廃止し、Application の `ICommand<TResponse>` に一本化する。

## 3. UseCase一覧（入力 / 出力 / 副作用 / エラー）

### 3.1. CleaningArea系

1. `RegisterCleaningAreaUseCase`
   - Input: `FacilityId`, `AreaId`, `Name`, `InitialWeekRule`, `InitialSpots[]`
   - Output: `AreaId`
   - SideEffect: Active な `FacilityDirectoryProjection` 検証、`CleaningArea` 追加、`CleaningAreaRegistered` 配送
   - Error: `NotFound(FacilityDirectory)`, `FacilityInactive`, `InvalidWeekRuleError`, `CleaningAreaHasNoSpotError`, `RepositoryDuplicate`

2. `ScheduleWeekRuleChangeUseCase`
   - Input: `AreaId`, `NextWeekRule`
   - Output: `Unit`
   - SideEffect: `CleaningArea` 更新、`WeekRuleChangeScheduled` 配送
   - Error: `NotFound`, `InvalidWeekRuleError`, `RepositoryConcurrency`

3. `ApplyDueWeekRuleChangesUseCase`
   - Input: `CurrentWeek?`
   - Output: `AppliedCount`
   - SideEffect: `ListWeekRuleDueAsync` 対象へ `ApplyPendingWeekRule` を適用
   - Error: `RepositoryConcurrency`

4. `AddCleaningSpotUseCase`
   - Input: `AreaId`, `SpotId`, `SpotName`, `SortOrder`
   - Output: `Unit`
   - SideEffect: `CleaningArea` 更新、`CleaningSpotAdded` 配送
   - Error: `NotFound`, `DuplicateCleaningSpotError`, `RepositoryConcurrency`

5. `RemoveCleaningSpotUseCase`
   - Input: `AreaId`, `SpotId`
   - Output: `Unit`
   - SideEffect: `CleaningArea` 更新、`CleaningSpotRemoved` 配送（未存在 Spot は no-op）
   - Error: `NotFound`, `CleaningAreaHasNoSpotError`, `RepositoryConcurrency`

6. `AssignUserToAreaUseCase`
   - Input: `AreaId`, `UserId`, `EmployeeNumber`, `AreaMemberId?`
   - Output: `Unit`
   - SideEffect: 重複所属チェック -> `CleaningArea` 更新 -> `UserAssignedToArea` 配送
   - Error: `NotFound`, `UserAlreadyAssignedToAnotherAreaError`, `DuplicateAreaMemberError`, `RepositoryConcurrency`

7. `UnassignUserFromAreaUseCase`
   - Input: `AreaId`, `UserId`
   - Output: `Unit`
   - SideEffect: `CleaningArea` 更新、`UserUnassignedFromArea` 配送（未所属は no-op）
   - Error: `NotFound`, `RepositoryConcurrency`

8. `TransferUserToAreaUseCase`
   - Input: `FromAreaId`, `ToAreaId`, `UserId`, `ToAreaMemberId`, `EmployeeNumber`
   - Output: `Unit`
   - SideEffect: 同一トランザクションで From/To の2集約更新、イベント配送
   - Error: `NotFound`, `InvalidTransferRequest`, `DuplicateAreaMemberError`, `RepositoryConcurrency`

### 3.2. WeeklyDutyPlan系

1. `GenerateWeeklyPlanUseCase`
   - Input: `AreaId`, `WeekId`, `Policy`
   - Output: `PlanId`, `WeekId`, `Revision`, `Status`
   - SideEffect: `(AreaId,WeekId)` 重複チェック、履歴取得、計算、Plan追加、Area cursor更新、イベント配送
   - Error: `NotFound`, `WeeklyPlanAlreadyExists`, `NoAvailableUserForSpotError`, `RepositoryDuplicate`, `RepositoryConcurrency`

2. `RebalanceForUserAssignedUseCase`
   - Input: `PlanId`, `AddedUserId`
   - Output: `Unit`
   - SideEffect: 再配分計算、Plan再計算、Area cursor更新、イベント配送
   - Error: `NotFound`, `InvalidRebalanceRequestError`, `WeekAlreadyClosedError`, `RepositoryConcurrency`

3. `RebalanceForUserUnassignedUseCase`
   - Input: `PlanId`, `RemovedUserId`
   - Output: `Unit`
   - SideEffect: 再配分計算、Plan再計算、Area cursor更新、イベント配送
   - Error: `NotFound`, `InvalidRebalanceRequestError`, `WeekAlreadyClosedError`, `RepositoryConcurrency`

4. `RecalculateForSpotChangedUseCase`
   - Input: `PlanId`
   - Output: `Unit`
   - SideEffect: 再計算、Plan revision 増加、Area cursor更新、イベント配送
   - Error: `NotFound`, `InvalidRebalanceRequestError`, `WeekAlreadyClosedError`, `RepositoryConcurrency`

5. `PublishWeeklyPlanUseCase`
   - Input: `PlanId`
   - Output: `Unit`
   - SideEffect: 状態遷移 `Draft|Published -> Published`、イベント配送
   - Error: `NotFound`, `WeekAlreadyClosedError`, `RepositoryConcurrency`

6. `CloseWeeklyPlanUseCase`
   - Input: `PlanId`
   - Output: `Unit`
   - SideEffect: 状態遷移 `Draft|Published -> Closed`、イベント配送（既に Closed は no-op）
   - Error: `NotFound`, `RepositoryConcurrency`

### 3.3. バッチ系

1. `GenerateCurrentWeekPlansBatchUseCase`
   - Input: `Policy`
   - Output: `GeneratedCount`, `SkippedCount`, `FailedCount`
   - SideEffect: 全エリア対象に週次生成 UseCase を順次実行
   - Error: 各エリア失敗は `FailedCount` に集約

### 3.4. Facility系

1. `RegisterFacilityUseCase`
   - Input: `FacilityCode`, `FacilityName`, `Description?`, `TimeZoneId`
   - Output: `FacilityId`, `LifecycleStatus`
   - SideEffect: `Facility` 追加、`FacilityRegistered` 配送
   - Error: `InvalidFacilityCodeError`, `InvalidFacilityNameError`, `InvalidFacilityDescriptionError`, `InvalidFacilityTimeZoneError`, `DuplicateFacilityCodeError`, `RepositoryDuplicate`

2. `UpdateFacilityUseCase`
   - Input: `FacilityId`, `FacilityName`, `Description?`, `TimeZoneId`
   - Output: `FacilityId`, `Version`
   - SideEffect: `Facility` 更新、`FacilityUpdated` 配送
   - Error: `NotFound`, `InvalidFacilityNameError`, `InvalidFacilityDescriptionError`, `InvalidFacilityTimeZoneError`, `RepositoryConcurrency`

3. `ChangeFacilityActivationUseCase`
   - Input: `FacilityId`, `TargetStatus`
   - Output: `FacilityId`, `LifecycleStatus`, `Version`
   - SideEffect: `FacilityUpdated(ChangeType=LifecycleChanged)` 配送
   - Error: `NotFound`, `RepositoryConcurrency`

## 4. MediatR 構成とパイプライン責務

採用:

- `ICommand<TResponse>` + `ICommandHandler<TRequest,TResponse>`
- `ApplicationResult<T>` を全ユースケースで返却

責務:

- ハンドラ内で `Find -> Execute Domain -> Save -> Dispatch -> ClearEvents`
- リポジトリ例外は `UseCaseExecution.InTransaction` で `ApplicationResult` に正規化

## 5. DomainEvent 同期配送設計

採用方式:

- 同期 In-Process
- `IDomainEventDispatcher` 実装は `MediatRDomainEventDispatcher`
- 1イベントずつ `DomainEventNotification` として Publish

購読（実装済み）:

- `UserAssignedToArea` -> `RebalanceForUserAssignedUseCase`
- `UserUnassignedFromArea` -> `RebalanceForUserUnassignedUseCase`
- `CleaningSpotAdded` / `CleaningSpotRemoved` -> `RecalculateForSpotChangedUseCase`

運用方針:

- `WeekRuleChangeScheduled` は即時再配分しない
- 反映は `ApplyDueWeekRuleChangesUseCase` と `GenerateCurrentWeekPlansBatchUseCase` に委譲

## 6. トランザクション・排他・整合性

- 単一集約更新は 1 トランザクション
- `TransferUserToArea` は 2集約同時更新を 1 トランザクションで実施
- イベント連鎖で起動される再配分は別トランザクションとして扱う
- `SaveAsync(expectedVersion)` による楽観排他を必須化

## 7. エラーマッピング（Domain -> Application -> HTTP）

| 種別                       | ApplicationError.Code                             | HTTP |
| -------------------------- | ------------------------------------------------- | ---- |
| 未存在                     | `NotFound`                                        | 404  |
| 入力不正 / VO不正          | DomainError.Code（`Invalid*`）                    | 400  |
| 業務競合（週クローズ済等） | DomainError.Code（`WeekAlreadyClosedError` 等）   | 409  |
| 保存競合                   | `RepositoryConcurrency`                           | 409  |
| 重複作成                   | `RepositoryDuplicate` / `WeeklyPlanAlreadyExists` | 409  |
| 予期しない障害             | `Unexpected`                                      | 500  |

## 8. Domain Commands からの移行計画

実施内容:

- 削除: `src/OsoujiSystem.Domain/Commands/CleaningAreaCommands.cs`
- 削除: `src/OsoujiSystem.Domain/Commands/WeeklyDutyPlanCommands.cs`

移行後:

- WebApi / Job / EventHandler からは Application Request を直接送信
- Domain は Command DTO を保持しない

## 9. テスト戦略

優先テスト:

1. 各 UseCase の正常系/異常系/no-op 系
2. `AssignUserToArea` 後に同期で再配分 UseCase が起動すること
3. `RemoveCleaningSpot` 後に `WeeklyDutyPlan.Revision` が増加すること
4. 古い `expectedVersion` で `RepositoryConcurrency` が返ること
5. Domain Command 参照が残っていないこと（ビルドで保証）
6. `ApplyDueWeekRuleChanges` が `EffectiveFromWeek <= currentWeek` のみ適用すること
7. `GenerateCurrentWeekPlansBatch` が `(AreaId,WeekId)` 重複を `SkippedCount` で処理すること
8. `RegisterFacility` / `UpdateFacility` / `ChangeFacilityActivation` の正常系 / 重複 / 楽観排他 / no-op を確認すること
9. `RegisterCleaningArea` が `FacilityDirectoryProjection` 未同期時に `404`、非アクティブ時に `409` を返すこと

## 10. 将来拡張（Outbox / ReadModel / 認可）

- `IDomainEventDispatcher` を Outbox 実装へ差し替え可能に維持
- ReadModel 用の参照専用 Repository 抽象を別文書で定義
- ユースケースごとの認可ポリシー（ロール/テナント境界）を追加

## 11. 付録: 現状ギャップへの対応結果

- Domain Command の混在解消: Application Request へ統一
- `RegisterCleaningArea` 初期 Spot 欠落: `InitialSpots[]` 追加
- `TransferUserToArea` 入力欠落: `ToAreaMemberId` / `EmployeeNumber` 追加
- `ApplyPendingWeekRule` ユースケース不足: `ApplyDueWeekRuleChangesUseCase` 追加
- 重複所属チェック不足: `AssignUserToAreaUseCase` で `FindByUserIdAsync` 検証
- 複数集約更新境界未定義: `IApplicationTransaction` で明示
- エラーマッピング未定義: Domain/Application/HTTP 対応表を定義
