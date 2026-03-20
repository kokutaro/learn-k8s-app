# WeeklyDutyPlan ユーザー情報可視化設計書（join-at-read, v1）

- Status: Proposed
- Date: 2026-03-08
- Related:
  - `docs/readmodel-cqrs-design-v1.md`
  - `docs/user-management-bc-design-v1.md`
  - `docs/api-endpoint-design-v1.md`
  - `docs/infrastructure-architecture-adr-v5.md`

## 1. 目的 / スコープ

本書は、`GET /api/v1/weekly-duty-plans/{planId}` で返却する `assignments` および `offDutyEntries` に対し、`userId` と `employeeNumber` だけでなく、表示名などのユーザー可視化情報を返却できるようにするための設計を定義する。

対象:

- `WeeklyDutyPlan` 詳細 API のレスポンス拡張
- `join-at-read` による read model 合成方針
- `User Management` BC から `Duty Assignment` 側 read store への連携項目
- Projection / ReadRepository / API 整形の責務分担
- 欠損時挙動、整合性、移行方針

非対象:

- `WeeklyDutyPlan` コマンド側 Aggregate の変更
- 認可ポリシー詳細
- UI 実装
- 外部 IdP 連携詳細

## 2. 背景

現状の `WeeklyDutyPlan` 詳細 read model は、`assignments` に `spotId`, `userId`、`offDutyEntries` に `userId` しか持たない。

そのため API 利用者は、当番や休みメンバーを識別するために追加のユーザー問い合わせを別途行う必要がある。

一方で、`User Management` BC には `ManagedUser` の正本があり、`DisplayName` などのプロフィールを保持している。ただし `Duty Assignment` 側の `projection_user_directory` は現在、`employee_number`, `lifecycle_status`, `department_code` までしか保持していない。

## 3. 課題整理

現状課題は次の 3 点である。

1. `WeeklyDutyPlan` API 単体で表示に必要なユーザー情報が完結しない
2. `ManagedUser` の表示名が `Duty Assignment` 側の read store に同期されていない
3. 将来のプロフィール変更に対し、既存 `WeeklyDutyPlan` Projection を大量再投影せずに追随したい

## 4. 設計方針

### 4.1. 採用方針

- `join-at-read` を採用する
- `WeeklyDutyPlan` Projection には `userId` を保持し続ける
- ユーザー表示情報は `projection_user_directory` に集約する
- `GetWeeklyDutyPlan` の read repository で `projection_weekly_plan_*` と `projection_user_directory` を join して API 向け read model を構成する

### 4.2. 採用理由

1. ユーザープロフィール更新時に、全 `WeeklyDutyPlan` Projection の再生成が不要
2. ユーザー表示属性の正本を `ManagedUser` に近い 1 箇所の read store に寄せられる
3. `WeeklyDutyPlan` Projection を表示要件で肥大化させず、計画そのものの責務を維持できる
4. API の互換性を壊さず加算変更で拡張できる

### 4.3. 非採用方針

以下は今回採用しない。

- `projection_weekly_plan_assignments` / `projection_weekly_plan_offduty` に `display_name` を複写する
- API 呼び出し時に `ManagedUser` Aggregate を都度再構築して参照する
- WebApi 層から別 API を呼び出してユーザー情報を水増しする

理由:

- Projection 複写はプロフィール変更時の再投影範囲が大きい
- Aggregate 再構築は CQRS 分離に反する
- API 間呼び出しは失敗点と遅延を増やす

## 5. To-Be アーキテクチャ

### 5.1. データフロー

1. `User Management` BC で `ManagedUser` が登録・更新される
2. `UserRegistered` / `UserUpdated` 統合イベントにユーザー表示情報を含める
3. `Duty Assignment` BC の consumer が `projection_user_directory` を upsert する
4. `GetWeeklyDutyPlan` 実行時に `projection_weekly_plans`, `projection_weekly_plan_assignments`, `projection_weekly_plan_offduty`, `projection_user_directory` を参照する
5. API は assignment / off-duty ごとに `user` オブジェクトを含めて返却する

### 5.2. 責務分担

- `User Management` BC
  - ユーザー正本を管理する
  - 下流 BC に必要な最小プロフィールをイベントへ載せる
- `projection_user_directory`
  - `Duty Assignment` BC 内の参照用ユーザーディレクトリとする
  - `WeeklyDutyPlan` 詳細、将来の area/member 表示など横断的な可視化に再利用する
- `WeeklyDutyPlan` Projection
  - 計画、割当、休みの事実のみを保持する
- `PostgresWeeklyDutyPlanReadRepository`
  - join を行い、API 向け read model を合成する
- `WebApi`
  - ReadModel を HTTP レスポンスへ変換する

## 6. 連携データ設計

### 6.1. ユーザー可視化用 Projection

`projection_user_directory` を、現在の所属可否確認用途だけでなく「参照用公開プロフィール Projection」として拡張する。

保持項目:

- `user_id`
- `employee_number`
- `display_name`
- `lifecycle_status`
- `department_code`
- `aggregate_version`
- `source_event_id`
- `updated_at`

今回の表示要件では `display_name` を必須の可視化項目とする。`email_address` は PII 最小化の観点から含めない。

### 6.2. 統合イベント契約

`UserRegistered` / `UserUpdated` に次の項目を追加する。

- `DisplayName`

補足:

- `EmployeeNumber` は既存どおり維持する
- `DepartmentCode` も既存どおり維持する
- `ChangedFields` は引き続き差分通知用途で保持する
- `DisplayName` は `ChangedFields` に含まれない更新でも常に最新スナップショットとして載せる

### 6.3. 欠損許容

`projection_user_directory` に対象 `user_id` が未反映の場合でも、`WeeklyDutyPlan` 詳細 API 自体は成功させる。

方針:

- `userId` は必ず返す
- `user` オブジェクトは `null` 許容とする
- user directory 欠損は `404` や `500` にしない

理由:

- at-least-once 配送や投影遅延の下では、一時的な projection 未整備は正常系として扱うべきため

## 7. ReadModel 設計

### 7.1. 新規 ReadModel

```csharp
public sealed record WeeklyDutyPlanUserSummaryReadModel(
    Guid UserId,
    string EmployeeNumber,
    string DisplayName,
    string? DepartmentCode,
    string LifecycleStatus);
```

```csharp
public sealed record DutyAssignmentReadModel(
    Guid SpotId,
    Guid UserId,
    WeeklyDutyPlanUserSummaryReadModel? User);
```

```csharp
public sealed record OffDutyEntryReadModel(
    Guid UserId,
    WeeklyDutyPlanUserSummaryReadModel? User);
```

`WeeklyDutyPlanDetailReadModel` 自体の構造は維持しつつ、子要素に `User` を追加する。

### 7.2. API レスポンス形

既存レスポンス互換を維持するため、`userId` は残し、加算で `user` を追加する。

```json
{
  "data": {
    "id": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0",
    "assignments": [
      {
        "spotId": "06f74c35-9126-4ced-bbc4-94e7f0c7df61",
        "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
        "user": {
          "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
          "employeeNumber": "000001",
          "displayName": "山田 太郎",
          "departmentCode": "OPS",
          "lifecycleStatus": "active"
        }
      }
    ],
    "offDutyEntries": [
      {
        "userId": "b7e0c75c-55fb-43e4-a915-36e287c6aa59",
        "user": {
          "userId": "b7e0c75c-55fb-43e4-a915-36e287c6aa59",
          "employeeNumber": "000017",
          "displayName": "佐藤 花子",
          "departmentCode": "OPS",
          "lifecycleStatus": "active"
        }
      }
    ]
  }
}
```

`user.userId` は冗長だが、UI 側で `user` オブジェクト単独を再利用しやすくするため許容する。

## 8. Query / Repository 設計

### 8.1. join 方針

`FindByIdAsync(planId)` の中で以下を行う。

1. `projection_weekly_plans` から plan header を取得
2. `projection_weekly_plan_assignments` を spot 順で取得
3. `projection_weekly_plan_offduty` を取得
4. 2, 3 に含まれる `user_id` 一覧を収集する
5. `projection_user_directory` から `WHERE user_id = ANY(@userIds)` で一括取得する
6. application memory 上で `user_id` をキーに assignment / off-duty へ紐付ける

### 8.2. join 方式を SQL 直結にしない理由

assignment と off-duty の両方で同一ユーザー群を使うため、別々に LEFT JOIN するよりも、ユーザー一覧を一括で引いてメモリ合成する方が次の点で扱いやすい。

- assignment と off-duty のマッピング処理を共通化できる
- `user directory` 欠損時の `null` 制御をアプリ側で明示しやすい
- 将来 `assignments` と `offDutyEntries` に同じ user summary を使い回せる

### 8.3. 性能前提

- 対象は単一 `planId` 詳細取得である
- `assignments` / `offDutyEntries` の件数は掃除箇所数・所属人数に比例し、通常は小規模
- `projection_user_directory.user_id` は主キーであり、一括取得コストは限定的

このため、単一詳細取得における 3 クエリ構成は許容する。

## 9. Projection 更新設計

### 9.1. user directory 更新トリガ

既存の `UserRegistryIntegrationRabbitMqMessageHandler` を継続利用する。

更新契約:

- `user-registry.user-registered`
- `user-registry.user-updated`

consumer は `aggregate_version` に基づき冪等 upsert を行う。

### 9.2. スキーマ変更

`projection_user_directory` に `display_name` 列を追加する。

DDL イメージ:

```sql
ALTER TABLE projection_user_directory
    ADD COLUMN IF NOT EXISTS display_name TEXT NOT NULL DEFAULT '';
```

最終 DDL では以下を追加で考慮する。

- 空文字 default は移行時の一時値としてのみ利用し、backfill 後は実データで埋める
- 必要なら文字数制約を `ManagedUserDisplayName` の制約に合わせる

## 10. 移行 / バックフィル

### 10.1. なぜバックフィルが必要か

イベント契約拡張後に新規・更新イベントだけを待つと、既存ユーザーの `display_name` が `projection_user_directory` に入らないため。

### 10.2. バックフィル方針

1. DB migration で `display_name` 列を追加する
2. アプリを新イベント契約対応版へ更新する
3. `managed_user` snapshot もしくは event store から既存ユーザーを走査し、`projection_user_directory` を再構築する
4. バックフィル完了後に `WeeklyDutyPlan` API の `user` 表示を有効化する

補足:

- `WeeklyDutyPlan` Projection 自体の再構築は不要
- backfill 対象は user directory のみ

## 11. 整合性 / 障害時挙動

### 11.1. 整合性モデル

- `WeeklyDutyPlan` と `projection_user_directory` の間は結果整合
- `WeeklyDutyPlan` 作成直後に user summary が `null` となる短時間の不整合は許容する

### 11.2. API 応答方針

- plan が存在すれば `200 OK`
- 一部ユーザー情報が欠損していても `200 OK`
- 欠損ユーザーだけ `user: null`

### 11.3. 監視ポイント

- `projection_user_directory` の更新失敗数
- `WeeklyDutyPlan` 詳細応答における `user == null` 件数
- consumer lag

`user == null` が継続する場合、イベント欠落またはバックフィル漏れを疑う。

## 12. API 契約変更方針

### 12.1. 後方互換性

既存クライアントを壊さないため、以下を守る。

- `assignments[].userId` を削除しない
- `offDutyEntries[].userId` を削除しない
- 新規追加は `user` プロパティのみに限定する

### 12.2. バージョニング

今回の変更は加算変更であり、`/api/v1` のまま継続する。

## 13. 実装タスク分解

1. `ManagedUser` 統合イベント契約に `DisplayName` を追加する
2. `projection_user_directory` の schema / repository / consumer を更新する
3. `WeeklyDutyPlan` 用 read model を拡張する
4. `PostgresWeeklyDutyPlanReadRepository` に user directory 一括取得処理を追加する
5. `WeeklyDutyPlanEndpoints` の response mapping を更新する
6. API 統合テストを更新し、`user.displayName` を検証する
7. user directory バックフィル手順を整備する

## 14. テスト観点

### 14.1. 正常系

- assignment に `user.displayName` が含まれる
- off-duty に `user.displayName` が含まれる
- 同一 user が assignment と off-duty の両方に出ても整形できる

### 14.2. 欠損系

- user directory に該当ユーザーが無い場合でも `200 OK`
- 欠損ユーザーだけ `user: null`

### 14.3. 更新追随

- `UpdateUserProfile` 実行後、projection 反映後の `WeeklyDutyPlan` 詳細で `displayName` が更新される
- `WeeklyDutyPlan` 自体を再生成しなくても最新表示名が反映される

### 14.4. 互換性

- 既存の `userId` 検証テストが壊れない
- `ETag`, `version`, `status` 等の既存応答が不変である

## 15. 将来拡張

将来、同じ `WeeklyDutyPlanUserSummaryReadModel` を以下にも再利用できる。

- `CleaningArea.members` の可視化強化
- 通知本文生成時のユーザー表示
- 管理 UI の簡易ユーザーチップ表示

ただし PII 増加は慎重に扱い、必要になった時点で項目追加を評価する。
