# お掃除当番システム コア領域設計書（v3 / 実装反映版）

## 1. スコープ

- 対象: 掃除担当の自動割り当て、再配分、公平性維持。
- 非対象（初期リリース外）: 休暇・欠勤・スキップ。

## 2. 確定ルール一覧

| 項目                   | 仕様                                                         |
| ---------------------- | ------------------------------------------------------------ |
| 週境界                 | 既定 `月曜 00:00 JST`。`WeekRule` で変更可能。               |
| WeekRule 変更反映      | 変更は翌週から適用（当週は据え置き）。                       |
| ユーザー追加/削除/異動 | 当週計画へ即時再配分。                                       |
| 掃除箇所増減           | 当週計画を即時再計算。                                       |
| `users > spots`        | `users - spots` 人を OffDuty 化し、連続 OffDuty 回避を優先。 |
| 同率時の優先順         | 1. 過去4週担当回数（降順） 2. 社員番号（昇順）。             |
| 履歴欠損時初期値       | 過去4週担当回数は全員 `0`、OffDuty連続回数も `0`。           |
| 社員番号形式           | `EmployeeNumber` は 6 桁数字（`^[0-9]{6}$`）のみ許可。       |

## 3. ユビキタス言語

- `CleaningArea`: 掃除運用の単位（フロア/区画）。
- `CleaningSpot`: エリア内の掃除対象。
- `AreaMember`: エリア所属ユーザー。
- `WeeklyDutyPlan`: 特定エリア・特定週の担当計画。
- `PlanRevision`: 同一週内の再計算版番号。
- `OffDuty`: その週に担当がない状態。
- `EmployeeNumber`: 同率判定に使う社員番号。
- `Facility`: 掃除エリアが所属する施設。Supporting BC `Facility Structure` が正本を持つ。

## 4. Bounded Context

- Core BC: `Duty Assignment`
- Supporting BC: `User Registry`（`UserId`, `EmployeeNumber`, 在籍）
- Supporting BC: `Facility Structure`（`FacilityId`, `FacilityLifecycleStatus`, `TimeZoneId`）

## 5. 集約設計

### 5.1. Aggregate: `CleaningArea`

#### 5.1.1. 責務

- 所属 `Facility` を参照しつつ、掃除箇所・所属ユーザー・週ルールを管理。
- エリア内の決定論的な順序（Spot順、Member順）を保持。

#### 5.1.2. モデル

- Entity: `CleaningArea`(AR), `CleaningSpot`, `AreaMember`
- ValueObject:
  - `FacilityId`（Supporting BC 参照）
  - `CleaningAreaId`, `CleaningSpotId`, `AreaMemberId`, `UserId`
  - `EmployeeNumber`
  - `WeekRule`（開始曜日、開始時刻、TimeZoneId、EffectiveFromWeek）
  - `RotationCursor`

#### 5.1.3. 不変条件

- 掃除箇所は常に 1 件以上。
- `CleaningArea` は 1 つの `FacilityId` に属する。
- 同一 `UserId` の重複所属なし。
- `AreaMember` は `EmployeeNumber` を必須保持。
- `RegisterCleaningArea` 時に初期 `CleaningSpot` を 1 件以上必須とする。
- `RegisterCleaningArea` 時に参照先 `FacilityId` の存在 / Active 判定は Application 層で行う。
- `ScheduleWeekRuleChange` は翌週以降（当週以前は不可）のみ受け付ける。

#### 5.1.4. コマンド

- `RegisterCleaningArea`
- `ScheduleWeekRuleChange`
- `AddCleaningSpot`
- `RemoveCleaningSpot`
- `AssignUserToArea`
- `UnassignUserFromArea`
- `TransferUserToArea`

#### 5.1.5. ドメインイベント

- `CleaningAreaRegistered`
- `WeekRuleChangeScheduled`
- `CleaningSpotAdded`
- `CleaningSpotRemoved`
- `UserAssignedToArea`
- `UserUnassignedFromArea`
- `UserTransferredFromArea`

#### 5.1.6. DomainError

- `CleaningAreaHasNoSpotError`
- `DuplicateCleaningSpotError`
- `DuplicateAreaMemberError`
- `UserAlreadyAssignedToAnotherAreaError`
- `InvalidWeekRuleError`

### 5.2. Aggregate: `WeeklyDutyPlan`

#### 5.2.1. 責務

- 週次計画の生成・即時再計算・版管理。
- `Spot -> User` 一意制約を維持。

#### 5.2.2. モデル

- Entity: `WeeklyDutyPlan`(AR), `DutyAssignment`, `OffDutyEntry`
- ValueObject:
  - `WeekId`
  - `PlanRevision`（1,2,3...）
  - `AssignmentPolicy`（`FairnessWindowWeeks=4` 固定）
  - `UserWorkload`

#### 5.2.3. 不変条件

- 同一週・同一エリアで `CleaningSpotId` は一意に 1 ユーザーへ割当。
- `PlanRevision` は単調増加。
- 週がクローズ済みなら再計算不可。
- 再配分 API は理由別に入力妥当性を検証する（追加ユーザーが結果に含まれる、離脱ユーザーが結果に含まれない等）。

#### 5.2.4. コマンド

- `GenerateWeeklyPlan`
- `RebalanceForUserAssigned`
- `RebalanceForUserUnassigned`
- `RecalculateForSpotChanged`
- `PublishWeeklyPlan`
- `CloseWeeklyPlan`

#### 5.2.5. ドメインイベント

- `WeeklyPlanGenerated`
- `WeeklyPlanRecalculated`
- `DutyAssigned`
- `DutyReassigned`
- `UserMarkedOffDuty`
- `WeeklyPlanPublished`
- `WeeklyPlanClosed`

#### 5.2.6. DomainError

- `WeekAlreadyClosedError`
- `AssignmentConflictError`
- `NoAvailableUserForSpotError`
- `InvalidRebalanceRequestError`

## 6. 状態遷移

### 6.1. `CleaningArea`

- `Active`（初期登録後）
- `ScheduleWeekRuleChange` 実行時: 状態は `Active` のまま、`PendingWeekRule` を保持。
- 週切替タイミングで `PendingWeekRule` を `CurrentWeekRule` に昇格。

### 6.2. `WeeklyDutyPlan`

- `Draft` -> `Published` -> `Closed`
- `Published` 中に再配分要因発生:
  - 同一 `WeekId` の `PlanRevision` を +1 して再計算。
  - 再計算結果を再発行（`WeeklyPlanRecalculated`）。
  - 状態は `Published` 維持。

## 7. 割り当てアルゴリズム（実装可能仕様）

1. 入力取得

   - `spots`（固定順）
   - `members`（社員番号昇順）
   - `rotationCursor`
   - `history[直近4週]`（担当回数、OffDuty連続回数）

2. `spots >= members` の場合

   - `rotationCursor` 起点の巡回で全 spot を順に割当。
   - 余剰 spot は巡回継続で同一ユーザー複数担当可。

3. `members > spots` の場合

   - まず `spots` 人を担当者として選定。
   - 残りを OffDuty 候補とする。
   - OffDuty 決定規則:
     - 連続 OffDuty 回避を最優先。
     - 同率なら担当回数降順。
     - それでも同率なら社員番号昇順。

4. ユーザー追加（要件5）

   - `spots > users_before_add` のとき、最多担当ユーザーから1件移譲。
     - 最多担当同率は「現在担当件数降順 -> 過去4週担当回数降順 -> 社員番号昇順」。
   - それ以外は当週は OffDuty（次回再計算で均衡）。

5. ユーザー離脱/異動（要件6）

   - 離脱ユーザー担当なし: 変更なし。
   - 担当あり:
     - 当週 OffDuty ユーザー優先で補充。
     - 不足時は通常ローテーションで補充。

6. Spot 増減

   - 変更イベント受信で同週 `PlanRevision` を上げて再計算。

7. 出力

   - `DutyAssignment[]`
   - `OffDutyEntry[]`
   - `NextRotationCursor`
   - `PlanRevision + 1`（再計算時）

## 8. コマンド -> イベント連携

| Trigger Command                          | 発行イベント                                               | 後続コマンド（購読側）                   |
| ---------------------------------------- | ---------------------------------------------------------- | ---------------------------------------- |
| `GenerateWeeklyPlan`                     | `WeeklyPlanGenerated`, `DutyAssigned`, `UserMarkedOffDuty` | 通知送信、実績作成                       |
| `AssignUserToArea`                       | `UserAssignedToArea`                                       | `RebalanceForUserAssigned`               |
| `UnassignUserFromArea`                   | `UserUnassignedFromArea`                                   | `RebalanceForUserUnassigned`             |
| `AddCleaningSpot` / `RemoveCleaningSpot` | `CleaningSpotAdded` / `CleaningSpotRemoved`                | `RecalculateForSpotChanged`              |
| `ScheduleWeekRuleChange`                 | `WeekRuleChangeScheduled`                                  | 次週 `GenerateWeeklyPlan` で新ルール適用 |

## 9. 実装マッピング（C#）

- ID/VO: `readonly record struct` を採用。
- Command/Event: `record` + `required` で不完全入力を抑止。
- 集約操作: `Result<T, DomainError>` を返却。
- 週計算: `DateTimeOffset` + `TimeZoneInfo` + `WeekRule`。
- ソートキー: `EmployeeNumber` を Core BC に保持（User Registry 由来）。
- `CleaningArea.Register(...)` は初期 Spot 一覧引数を受け取り、0件を拒否する。
- `CleaningArea` は `FacilityId` を保持し、Application 層は `IFacilityDirectoryProjectionRepository` で存在 / Active を検証する。
- 既存 `CleaningArea` 互換のため、移行時は `Legacy Facility (LEGACY-DEFAULT)` を seed して `facilityId` をバックフィルする。
- `WeeklyDutyPlan` は `RebalanceForUserAssigned(...)` / `RebalanceForUserUnassigned(...)` / `RecalculateForSpotChanged(...)` を公開し、内部で共通再計算処理を使う。
- 再配分計算入力は `UserAssignedRebalanceInput` / `UserUnassignedRebalanceInput` DTO で受ける。

サンプル（概念）

```csharp
public readonly record struct EmployeeNumber(string Value)
{
    public static Result<EmployeeNumber, DomainError> Create(string value) { ... }
}

public sealed record ScheduleWeekRuleChange(
    CleaningAreaId AreaId,
    DayOfWeek StartDay,
    TimeOnly StartTime,
    string TimeZoneId,
    WeekId EffectiveFromWeek);
```

## 10. 非技術者向け要約

- 週の最初に担当を自動作成し、人や掃除場所が変わったらその週分をすぐ調整します。
- 担当の偏りを減らすため、直近4週間の履歴を見て公平に回します。
- 週の開始ルール変更は混乱を防ぐため次の週から反映します。
- 社員番号は6桁数字だけ受け付けるため、入力ゆれを防げます。

## 11. 現時点の残課題（v3時点）

- `UserAlreadyAssignedToAnotherAreaError` に対応する「他エリア重複所属チェック」は未実装（今回スコープ外）。
- 当番通知（通知BC）の配信タイミングと再送戦略を確定する。
- 監査ログの保持期間（何年分保存するか）を確定する。
