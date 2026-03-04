# お掃除当番システム コア領域設計書（要件整理 v0）

## 1. このドキュメントの位置づけ
- 目的: 要望を DDD の設計要素（BC/集約/コマンド/ドメインイベント）へ落とすための初版。
- 方針: 一発確定はせず、未確定点は明示し、ヒアリングで埋めてから v1 に進める。

## 2. 業務要件の整理（現時点）
1. 掃除エリア
- フロアや区画単位で管理する。
- 基本的にエリア近傍のユーザーを配属する。
- ユーザーは同時に複数エリアへ所属できない。

2. 掃除箇所
- 掃除エリア内に 1 件以上存在する作業対象（トイレ、廊下、階段、ゴミ捨てなど）。

3. 掃除担当
- 週単位で、ユーザーが実施する掃除箇所を定義する。

4. 掃除担当割り当て
- 週初にエリアごとで自動割り当てする。
- 掃除箇所 1 件に対して担当ユーザーは常に 1 人。
- 掃除箇所数 > ユーザー数なら 1 ユーザーが複数箇所を担当し得る。
- 人数と箇所数が不変なら割り当てサイクルは常に同一（決定論的ローテーション）。
- ユーザー数 > 掃除箇所数なら担当なしユーザーが出る。
- 担当なし状態の連続発生は極力避ける。

5. ユーザー追加
- 追加時点では「エリア未割り当て」。
- エリア割り当て時の挙動:
  - 掃除箇所数 > ユーザー数: 当該エリアで最も担当数が多いユーザーの担当を 1 件、新規ユーザーへ移す。
  - それ以外: その週の担当は割り当てない。

6. ユーザー削除・エリア変更
- 削除または別エリアへ異動時、離脱エリアの取り扱いは 5 と同様。
- 離脱ユーザーが担当を持っていなければ何もしない。
- 離脱ユーザーが担当を持っていれば、担当なしユーザーを優先して引き継ぐ。

## 3. ユビキタス言語（草案）
- 掃除エリア (CleaningArea): 掃除運用の最小運営単位。
- 掃除箇所 (CleaningSpot): エリア内の具体的作業対象。
- エリア所属 (AreaMembership): ユーザーのエリア所属状態。
- 週次当番計画 (WeeklyDutyPlan): 特定エリア・特定週の担当割り当て結果。
- ローテーション状態 (RotationState): 次回割り当てでどこから開始するかを示す巡回位置。
- 担当なし (OffDuty): その週に割り当てが無い状態。

## 4. Bounded Context（初期案）

### 4.1 Core BC: 当番割り当て管理 (Duty Assignment)
- 役割: 週次の掃除担当を決定し、公平性と決定論を担保する。
- コア理由: システム価値の中心が「自動割り当ての業務ロジック」にあるため。

### 4.2 Supporting BC: 利用者管理 (User Registry)
- 役割: ユーザーの在籍・基本属性管理。
- Core BC との関係: Core では `UserId` と所属可否のみ参照。

### 4.3 Supporting BC: 組織/場所管理 (Facility Structure)
- 役割: エリアの物理情報（建屋、階、区画）を管理。
- Core BC との関係: Core では `CleaningAreaId` の有効性を参照。

## 5. 集約設計（初期案）

## 5.1 Aggregate: CleaningArea
- 集約ルート: `CleaningArea`
- 主責務:
  - エリア配下の掃除箇所と所属ユーザーの整合性維持。
  - 「ユーザーは 1 エリアのみ所属」を守るための所属ルール適用（他 BC との整合はアプリ層連携）。
  - 現在週への最小再配分（追加/削除時）を指示。

### ドメインモデル候補
- Entity
  - `CleaningArea`
  - `CleaningSpot`
  - `AreaMember`
- Value Object
  - `CleaningAreaId`, `CleaningSpotId`, `AreaMemberId`, `UserId`
  - `WeekId` (ISO 週を想定)
  - `RotationCursor` (次回ローテーション開始位置)
  - `AssignmentLoad` (週内担当件数)

### コマンド
- `RegisterCleaningArea`
- `AddCleaningSpot`
- `RemoveCleaningSpot`
- `EnrollUserToArea`
- `UnassignUserFromArea`
- `TransferUserOutOfArea`

### ドメインイベント
- `CleaningAreaRegistered`
- `CleaningSpotAdded`
- `CleaningSpotRemoved`
- `UserEnrolledToArea`
- `UserUnassignedFromArea`
- `AreaMembershipChanged`

## 5.2 Aggregate: WeeklyDutyPlan
- 集約ルート: `WeeklyDutyPlan`
- 主責務:
  - 週初の自動割り当てを決定論的に生成。
  - 同人数・同箇所条件で同一サイクル維持。
  - 担当なし連続を抑制するための公平性ルールを適用。
  - 週中のメンバー増減時の再配分を最小変更で反映。

### ドメインモデル候補
- Entity
  - `WeeklyDutyPlan`
  - `DutyAssignment` (spot -> user)
  - `OffDutyRecord` (担当なしユーザー履歴)
- Value Object
  - `PlanVersion`
  - `AssignmentPolicy` (rotation/fairness 設定)
  - `AssignmentCycleState`

### コマンド
- `GenerateWeeklyDutyPlan`
- `RebalanceAssignmentsForUserEnrollment`
- `RebalanceAssignmentsForUserDeparture`
- `ConfirmPlanPublication`

### ドメインイベント
- `WeeklyDutyPlanGenerated`
- `DutyAssigned`
- `DutyUnassigned`
- `AssignmentsRebalanced`
- `WeeklyDutyPlanPublished`

## 6. コマンド -> ドメインイベント発行・購読（初期案）

| コマンド | 発行イベント | 主な購読先 | 目的 |
|---|---|---|---|
| `GenerateWeeklyDutyPlan` | `WeeklyDutyPlanGenerated`, `DutyAssigned`(複数) | 通知 BC, 実績記録 BC | 週次当番の確定共有 |
| `EnrollUserToArea` | `UserEnrolledToArea` | Duty Assignment App Service | 今週計画の再配分判断 |
| `RebalanceAssignmentsForUserEnrollment` | `AssignmentsRebalanced` | 通知 BC | 新規ユーザー反映 |
| `UnassignUserFromArea` | `UserUnassignedFromArea` | Duty Assignment App Service | 離脱時の再配分判断 |
| `RebalanceAssignmentsForUserDeparture` | `AssignmentsRebalanced`, `DutyAssigned` | 通知 BC | 離脱分の補充 |

## 7. 不変条件（現時点）
- 1 掃除箇所に同時に複数担当を割り当てない。
- 同一週・同一エリアで、同一掃除箇所に担当は必ず 1 人。
- ユーザーの同時所属エリアは最大 1。
- 入力集合（ユーザー、掃除箇所、回転位置）が同じなら出力割り当ても同じ。
- 担当なしの連続発生は、可能な限り最小化する。

## 8. ここから確定が必要な論点（ヒアリング）
1. 週の定義
- 週の開始曜日・締め時刻・タイムゾーン（例: 月曜 00:00 JST）をどう定義しますか。

2. 再配分の適用タイミング
- ユーザー追加/削除/異動時の再配分は「即時で当週に反映」か「翌週から反映」か。

3. 公平性の評価軸
- 「担当なし連続回避」は直近何週を評価対象にしますか（例: 直近 4 週）。

4. 同率時の決定規則
- 「最も担当数が多いユーザー」が複数いる場合の tie-break（ユーザーID昇順、前週担当回数、抽選不可など）。

5. 掃除箇所の増減
- 週の途中で掃除箇所が追加/削除された場合、当週計画を再計算しますか。

6. 例外運用
- 休暇/長期不在/当番スキップはコア領域に含めますか（初期リリース外なら明示的に除外）。

## 9. 次の設計ステップ
- 上記 6 論点を確定。
- 確定後、v1 で以下を追加:
  - 集約ごとの状態遷移図
  - 割り当てアルゴリズムの疑似コード
  - 例外ケース一覧（失敗時 DomainError を含む）
  - .NET 実装向けの型設計（Entity/VO/DomainEvent/Command DTO）
