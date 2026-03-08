# お掃除当番システム コア領域設計書（v1）

## 1. 目的
- 目的: お掃除当番のコア業務を DDD で実装可能な設計に落とし込む。
- 対象: コア領域（掃除担当の自動割り当て、再配分、公平性維持）。
- 非対象（初期リリース外）: 休暇・欠勤・スキップ運用。

## 2. 確定した業務ルール

| 項目 | 確定内容 |
|---|---|
| 週の境界 | 既定は `月曜 00:00 JST`。運用設定で変更可能。 |
| ユーザー追加/削除/異動 | 再配分は即時実行（当週計画へ反映）。 |
| ユーザー数 > 掃除箇所数 | `ユーザー数 - 掃除箇所数` 人を毎週 OffDuty とし、週をまたいで均等化する。 |
| 同率判定 | 1. 過去4週の掃除担当回数が多い順 2. それでも同率なら `UserId` 昇順。 |
| 週途中の掃除箇所増減 | 当週計画を再計算する。 |
| 休暇運用 | 前日実施で吸収。ドメイン要件としては現時点で扱わない。 |

補足（公平性）
- OffDuty の偏りを減らすため、同一ユーザーが連続 OffDuty にならないよう優先制御する。
- 理想状態のイメージ: `o x o x o x`（`o`=担当あり、`x`=担当なし）。

## 3. Bounded Context

### 3.1 Core BC: Duty Assignment
- 責務: 掃除担当の決定、週次計画作成、即時再配分、公平性の維持。

### 3.2 Supporting BC: User Registry
- 責務: ユーザー在籍と基本属性。
- 連携: Core は `UserId` と所属可否のみ参照。

### 3.3 Supporting BC: Facility Structure
- 責務: エリアの物理構造管理。
- 連携: Core は `CleaningAreaId` の有効性を参照。

## 4. 集約設計

## 4.1 Aggregate: `CleaningArea`
- 役割: エリア内の掃除箇所・所属ユーザー・ローテーション状態を管理。
- 集約不変条件:
  - 掃除箇所は 1 件以上。
  - 所属ユーザーは同一エリア内で重複不可。
  - エリア内ソート順（Spot, User）が固定され、割り当て結果の決定論を担保する。

### ドメインモデル
- Entity
  - `CleaningArea` (AR)
  - `CleaningSpot`
  - `AreaMember`
- Value Object
  - `CleaningAreaId`, `CleaningSpotId`, `AreaMemberId`, `UserId`
  - `WeekRule`（開始曜日、開始時刻、タイムゾーン）
  - `RotationCursor`（次回開始インデックス）

### コマンド
- `RegisterCleaningArea`
- `ConfigureWeekRule`
- `AddCleaningSpot`
- `RemoveCleaningSpot`
- `AssignUserToArea`
- `UnassignUserFromArea`
- `TransferUserToAnotherArea`

### 発行ドメインイベント
- `CleaningAreaRegistered`
- `WeekRuleConfigured`
- `CleaningSpotAdded`
- `CleaningSpotRemoved`
- `UserAssignedToArea`
- `UserUnassignedFromArea`
- `UserTransferredFromArea`

### 主な DomainError
- `CleaningAreaHasNoSpotError`
- `DuplicateCleaningSpotError`
- `DuplicateAreaMemberError`
- `UserAlreadyAssignedToAnotherAreaError`
- `InvalidWeekRuleError`

## 4.2 Aggregate: `WeeklyDutyPlan`
- 役割: 特定エリア・特定週の担当割り当てを保持し、再配分を即時反映する。
- キー: `(CleaningAreaId, WeekId)`
- 集約不変条件:
  - 同一 `CleaningSpotId` に担当は常に 1 人。
  - 同一週・同一エリアで spot-assignment は一意。
  - `PlanVersion` は再計算のたびに単調増加。

### ドメインモデル
- Entity
  - `WeeklyDutyPlan` (AR)
  - `DutyAssignment`（`SpotId -> UserId`）
  - `OffDutyEntry`（当週非割当ユーザー）
- Value Object
  - `WeekId`（週識別）
  - `PlanVersion`
  - `AssignmentPolicy`（rotation, fairnessWindow=4）
  - `UserWorkload`（当週割当数）

### コマンド
- `GenerateWeeklyPlan`
- `RecalculatePlanForSpotChange`
- `RebalancePlanForUserAssigned`
- `RebalancePlanForUserUnassigned`
- `PublishWeeklyPlan`

### 発行ドメインイベント
- `WeeklyPlanGenerated`
- `WeeklyPlanRecalculated`
- `DutyAssigned`
- `DutyReassigned`
- `UserMarkedOffDuty`
- `WeeklyPlanPublished`

### 主な DomainError
- `WeeklyPlanAlreadyPublishedError`
- `AssignmentConflictError`
- `NoAvailableUserForSpotError`
- `InvalidRebalanceRequestError`

## 5. ドメインサービス

## 5.1 `DutyAssignmentEngine`
- 役割: `CleaningArea` と履歴情報から `WeeklyDutyPlan` を計算する純粋ドメインサービス。
- 入力:
  - エリア内 `Spot` 一覧（固定順）
  - エリア内 `User` 一覧（固定順）
  - `RotationCursor`
  - 過去4週の担当実績（Userごとの回数、OffDuty連続情報）
- 出力:
  - `DutyAssignment[]`
  - `OffDutyEntry[]`
  - 更新後 `RotationCursor`

## 5.2 `FairnessPolicy`
- 役割: `ユーザー数 > 掃除箇所数` のとき OffDuty を均等化。
- ルール:
  - 当週 OffDuty 必要人数 = `users - spots`
  - 連続 OffDuty 回避を優先。
  - 候補同率時は「過去4週担当回数の多い順」→「UserId昇順」。

## 6. コマンド -> イベント発行・購読

| コマンド | 発行イベント | 主な購読先 | 用途 |
|---|---|---|---|
| `GenerateWeeklyPlan` | `WeeklyPlanGenerated`, `DutyAssigned`, `UserMarkedOffDuty` | 通知BC, 実績BC | 週初計画の確定通知 |
| `AssignUserToArea` | `UserAssignedToArea` | Duty Assignment AppService | 当週計画の即時再配分開始 |
| `RebalancePlanForUserAssigned` | `WeeklyPlanRecalculated`, `DutyReassigned` | 通知BC | 新規所属ユーザーの当週反映 |
| `UnassignUserFromArea` | `UserUnassignedFromArea` | Duty Assignment AppService | 離脱時の即時補充開始 |
| `RebalancePlanForUserUnassigned` | `WeeklyPlanRecalculated`, `DutyReassigned`, `UserMarkedOffDuty` | 通知BC | 欠員補充と公平性維持 |
| `AddCleaningSpot` / `RemoveCleaningSpot` | `CleaningSpotAdded` / `CleaningSpotRemoved` | Duty Assignment AppService | 週途中再計算の起点 |
| `RecalculatePlanForSpotChange` | `WeeklyPlanRecalculated`, `DutyAssigned`/`DutyReassigned` | 通知BC | spot増減反映 |

## 7. 割り当てアルゴリズム（実装仕様）

1. 対象エリアの Spot, User を固定順で取得する。
2. `spots >= users`:
- ローテーション順でユーザーを巡回しながら Spot へ順次割当。
- 余剰 Spot は同一巡回を継続し、複数担当を許可。
3. `users > spots`:
- 先に `spots` 人へ担当を割当。
- 残り `users - spots` 人を OffDuty として選定。
- OffDuty 選定は「連続 OffDuty 回避」優先、次に同率判定ルールを適用。
4. 追加/削除/異動/spot増減が発生したら当週計画を即時再計算。
5. 再計算後に `PlanVersion` をインクリメントし、差分イベントを発行。

## 8. ユースケース別ルール詳細

## 8.1 ユーザー追加（エリア割当）
- 前提: 新規作成時は未割当。
- エリア割当時:
  - `spots > users(割当前)` なら、当週の最多担当ユーザーから 1 件移譲。
  - 同率時は過去4週担当回数の多い順、それでも同率なら `UserId` 昇順。
  - それ以外は当週担当なし（翌再計算で公平に吸収）。

## 8.2 ユーザー削除・異動（離脱側エリア）
- 離脱ユーザーに担当なし: 変更なし。
- 離脱ユーザーに担当あり:
  - まず当週 OffDuty ユーザーへ優先割当。
  - 充足しない場合は通常ローテーションで補完。

## 8.3 掃除箇所増減（週途中）
- 増加: 追加 spot を含め当週計画を再計算。
- 減少: 削除 spot の担当を解除し、残り spot を再計算で再配置。

## 9. 実装マッピング（C# / .NET）

- ID は `readonly record struct`（StronglyTypedId）。
- コマンドは `record` + `required` プロパティで不完全入力を防止。
- ドメインイベントは `record` で不変表現。
- 集約更新メソッドは `Result<T, DomainError>` を返却。
- 時刻は `DateTimeOffset` + `TimeZoneInfo`（`WeekRule` で週境界を算出）。

## 10. 非技術者向け運用説明（短縮版）
- 毎週の開始時刻に、各エリアの担当を自動で作成します。
- 人の増減や掃除箇所の変更があれば、その場で当週分を再調整します。
- 担当が偏らないよう、直近4週の実績を使って公平に回します。
- 担当がない週が続かないように、可能な限り交互に近い形へ寄せます。

## 11. 未確定（次回ヒアリング）
- `WeekRule` の設定変更を即時反映するか、翌週反映するか。
- `UserId` 昇順の定義（GUID の byte 順か、業務上の社員番号順か）。
- 「過去4週担当回数」のデータ欠損時（新規導入直後）の初期値扱い。
