# Facility管理BC設計書（v1）

- Status: Implemented
- Date: 2026-03-08
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/readmodel-cqrs-design-v1.md`
  - `docs/api-endpoint-design-v1.md`
  - `docs/infrastructure-architecture-adr-v5.md`

## 1. 目的 / スコープ

本書は、DDD における別 BC としての `Facility Structure` を、実装対象として具体化する。  
ここでいう Facility は、`Duty Assignment` が掃除エリアをぶら下げる上位の施設マスタを指す。

目的:

1. Facility の正本を `Facility Structure` BC に集約する
2. Facility の追加、編集、アクティブ / 非アクティブ切り替えを一貫したモデルで扱う
3. 別 BC が統合イベントを購読し、ローカル投影として Facility 情報を参照できるようにする

対象:

- `Facility Structure` BC の責務境界
- 集約、ValueObject、ユースケース
- Facility 公開 API 契約
- 統合イベント契約
- 別 BC 側の購読 / 投影 / 参照ルール

非対象:

- 建屋 / 階 / 部屋の多階層モデリング
- 施設ごとの認可ポリシー
- UI 画面遷移
- `Duty Assignment` 内の `CleaningSpot` / `AreaMember` の再設計

## 2. Context Map

### 2.1. BC の位置づけ

- Core BC: `Duty Assignment`
- Supporting BC: `User Management`
- Supporting BC: `Facility Structure`

`Facility Structure` は Facility の正本を持つ。  
`Duty Assignment` は Facility のローカル投影を持ち、`CleaningArea` 登録や参照表示に利用するが、Facility 自体は更新しない。

### 2.2. 境界の引き方

`Facility Structure` が所有するもの:

- `FacilityId` の採番とライフサイクル
- 施設コード、施設名、タイムゾーンなどの基本属性
- Facility の Active / Inactive 状態
- 登録 / 更新 / 状態変更の統合イベント発行

別 BC が参照のみするもの:

- `FacilityId`
- `FacilityCode`
- `FacilityName`
- `TimeZoneId`
- `LifecycleStatus`

### 2.3. 統合方式

- 書き込み連携: なし。別 BC から Facility を直接更新しない
- 読み取り連携: 非同期の統合イベント購読 + ローカル投影
- 例外経路: 初回同期欠損時のみ、将来の補助手段として照会 API を許可する

## 3. 設計原則

1. `FacilityId` を全 BC 共通で参照する正規識別子とする
2. Facility はハードデリートせず、`Active` / `Inactive` で運用状態を表す
3. 統合イベントには consumer が投影を更新するための最小スナップショットを含める
4. イベント配送は at-least-once 前提とし、consumer 側で冪等に投影する
5. 他 BC の業務ルールに必要な存在 / 状態確認は、投影を経由した参照で完結させる
6. Facility 非アクティブ化は他 BC の履歴を壊さず、新規関連付けの制御に使う

## 4. ユビキタス言語

- `Facility`: 掃除エリアが所属する施設マスタ
- `FacilityId`: Facility の内部識別子
- `FacilityCode`: 施設コード。業務上の一意キー
- `FacilityProfile`: 名称、説明、タイムゾーンなどの編集可能属性
- `FacilityLifecycleStatus`: `Active` / `Inactive`
- `FacilityExport`: 他 BC へ通知する最小スナップショット

## 5. 集約設計

## 6. 5.1 Aggregate: `Facility`

### 6.1. 責務

- Facility の新規登録
- Facility プロフィール更新
- Facility の有効 / 無効切り替え
- 統合イベントの発行起点

### 6.2. モデル

- AggregateRoot: `Facility`
- ValueObject:
  - `FacilityId`
  - `FacilityCode`
  - `FacilityName`
  - `FacilityDescription`
  - `FacilityTimeZone`
  - `FacilityLifecycleStatus`

### 6.3. 不変条件

1. `FacilityCode` は Facility 全体で一意
2. `FacilityName` は必須で、空白のみを許可しない
3. `FacilityTimeZone` は IANA Time Zone ID を正とする
4. `Inactive` でも履歴参照のためレコードは保持する
5. `Activate` / `Deactivate` は冪等 no-op を許可する

### 6.4. 状態

- `Active`
  - 別 BC が新規関連付けしてよい状態
- `Inactive`
  - 履歴参照のみ許可する状態

### 6.5. コマンド

- `RegisterFacility`
- `UpdateFacilityProfile`
- `ActivateFacility`
- `DeactivateFacility`

### 6.6. ドメインイベント

- `FacilityRegistered`
- `FacilityUpdated`

`FacilityUpdated` は `ChangeType` を持ち、少なくとも次を表現する。

- `ProfileUpdated`
- `LifecycleChanged`

### 6.7. DomainError

- `DuplicateFacilityCodeError`
- `InvalidFacilityCodeError`
- `InvalidFacilityNameError`
- `InvalidFacilityTimeZoneError`

## 7. アプリケーションインターフェイス

### 7.1. 主要ユースケース

1. `RegisterFacilityUseCase`

   - Input: `FacilityCode`, `FacilityName`, `Description?`, `TimeZoneId`
   - Output: `FacilityId`, `LifecycleStatus`
   - SideEffect: `Facility` 作成、`FacilityRegistered` 発行

2. `UpdateFacilityUseCase`

   - Input: `FacilityId`, `FacilityName`, `Description?`, `TimeZoneId`
   - Output: `FacilityId`, `Version`
   - SideEffect: `FacilityUpdated` 発行

3. `ChangeFacilityActivationUseCase`

   - Input: `FacilityId`, `TargetStatus`
   - Output: `FacilityId`, `Version`
   - SideEffect: `FacilityUpdated(ChangeType=LifecycleChanged)` 発行

### 7.2. C# 契約イメージ

```csharp
public sealed record RegisterFacilityRequest(
    string FacilityCode,
    string FacilityName,
    string? Description,
    string TimeZoneId);

public sealed record UpdateFacilityRequest(
    Guid FacilityId,
    string FacilityName,
    string? Description,
    string TimeZoneId,
    long ExpectedVersion);

public sealed record ChangeFacilityActivationRequest(
    Guid FacilityId,
    string TargetStatus,
    long ExpectedVersion);
```

実装注記:

- `TargetStatus` は API では `active` / `inactive` を受け付ける
- レスポンスの `lifecycleStatus` も同じく `active` / `inactive` で返す

## 8. 公開 API

### 8.1. エンドポイント

| UseCase                  | Method | Path                                         | 用途                   | 成功          |
| ------------------------ | ------ | -------------------------------------------- | ---------------------- | ------------- |
| RegisterFacility         | `POST` | `/api/v1/facilities`                         | Facility 新規登録      | `201 Created` |
| GetFacility              | `GET`  | `/api/v1/facilities/{facilityId}`            | Facility 詳細取得      | `200 OK`      |
| ListFacilities           | `GET`  | `/api/v1/facilities`                         | Facility 一覧          | `200 OK`      |
| UpdateFacility           | `PUT`  | `/api/v1/facilities/{facilityId}`            | Facility 編集          | `200 OK`      |
| ChangeFacilityActivation | `PUT`  | `/api/v1/facilities/{facilityId}/activation` | Active / Inactive 切替 | `200 OK`      |

### 8.2. リソースモデル

```json
{
  "id": "c6f1db02-6d7d-49c6-bf39-4f5d11edbb95",
  "facilityCode": "TOKYO-HQ",
  "name": "Tokyo Head Office",
  "description": "Main office building",
  "timeZoneId": "Asia/Tokyo",
  "lifecycleStatus": "active",
  "version": 4
}
```

### 8.3. HTTP 方針

- `POST` は `Location: /api/v1/facilities/{facilityId}` を返す
- `PUT` 系は `If-Match` を必須とし、`ETag` で楽観排他する
- エラーは既存 API 方針に合わせる
  - 入力不正: `400`
  - 未存在: `404`
  - 重複 / 非アクティブなどの業務競合: `409`

## 9. 統合イベント設計

### 9.1. 発行する統合イベント

最小セットは次の 2 種類とする。

1. `facility-structure.facility-registered.v1`

   - 新規 Facility 登録完了時に発行

2. `facility-structure.facility-updated.v1`

   - プロフィール更新または状態変更時に発行

`facility-updated` に状態変更も含める理由:

- consumer 側は「Facility 投影を最新化する」だけでよい
- 下流 BC の契約数を増やしすぎない
- `User Management` BC の `user-registered` / `user-updated` パターンと揃えられる

### 9.2. Routing Key

- `facility-structure.facility-registered`
- `facility-structure.facility-updated`

### 9.3. イベントペイロード

```json
{
  "facilityId": "c6f1db02-6d7d-49c6-bf39-4f5d11edbb95",
  "facilityCode": "TOKYO-HQ",
  "name": "Tokyo Head Office",
  "description": "Main office building",
  "timeZoneId": "Asia/Tokyo",
  "lifecycleStatus": "Active"
}
```

ヘッダ:

- `event_id`
- `aggregate_version`
- `occurred_at`

## 10. 別 BC からの取り込み設計

### 10.1. ローカル投影

別 BC は次の投影を持つ。

```csharp
public sealed record FacilityDirectoryProjection(
    FacilityId FacilityId,
    string FacilityCode,
    string Name,
    string TimeZoneId,
    FacilityLifecycleStatus LifecycleStatus,
    long AggregateVersion);
```

Repository 抽象:

```csharp
public interface IFacilityDirectoryProjectionRepository
{
    Task<FacilityDirectoryProjection?> FindByFacilityIdAsync(
        FacilityId facilityId,
        CancellationToken ct);

    Task UpsertAsync(
        FacilityDirectoryProjection projection,
        long aggregateVersion,
        Guid sourceEventId,
        CancellationToken ct);
}
```

PostgreSQL 投影テーブル例:

- `projection_facilities`
- 主キー: `facility_id`
- 更新条件: `WHERE aggregate_version <= EXCLUDED.aggregate_version`

同一サービス内の実装では、`Facility Structure` の read model と `Duty Assignment` の参照投影を `projection_facilities` に集約している。  
別プロセス / 別サービスへ分離する場合は、同じイベント契約を使って各 BC 側でローカル投影を再構築する。

### 10.2. `Duty Assignment` BC での利用方針

`Duty Assignment` は Facility を直接更新しない。  
代わりに `FacilityDirectoryProjection` を参照し、以下の業務前提に使う。

1. `CleaningArea` は必ず 1 つの `FacilityId` を持つ
2. `RegisterCleaningAreaUseCase` は参照先 Facility が存在し `Active` であることを検証する
3. `UpdateCleaningAreaUseCase` で Facility 再紐付けを許可する場合も同じ検証を行う
4. 非アクティブ Facility は新規の `CleaningArea` 紐付け対象にできない
5. 既存 `CleaningArea` や `WeeklyDutyPlan` の履歴は残し、自動的に削除 / 閉鎖しない

### 10.3. UI / Query での利用方針

- `CleaningArea` 詳細や一覧では `facilityId` を返す
- 必要に応じて Facility 名は query 側で `FacilityDirectoryProjection` と join して返してよい
- ただし Facility の正本更新は Facility API 経由に限定する

## 11. 導入順序

1. `Facility Structure` BC の集約、UseCase、API を追加する
2. Outbox / RabbitMQ に `facility-structure.*` の publish / bind を追加する
3. 下流 BC に `FacilityDirectoryProjection` と consumer を追加する
4. `Duty Assignment` の `CleaningArea` に `FacilityId` を導入する
5. 既存 `CleaningArea` データへ `facility_id` をバックフィルした後、新規作成で必須化する

### 11.1. 既存データ移行

既存 `CleaningArea` には Facility 概念が存在しなかったため、実装では互換用の `Legacy Facility` を 1 件 seed している。

- `FacilityId`: `00000000-0000-0000-0000-000000000001`
- `FacilityCode`: `LEGACY-DEFAULT`
- 用途: 既存 `CleaningArea` snapshot / projection の `facilityId` バックフィル

これにより、既存データを壊さずに `CleaningArea.FacilityId` を必須化できる。

## 12. 非技術者向け要約

- Facility は「掃除エリアの上位にある施設マスタ」として別管理にする
- Facility は追加、編集、利用停止 / 再開ができる
- 他の機能は Facility を直接更新せず、通知された最新状態を自分の参照用データへ取り込む
- これにより、施設マスタの責務を分離しながら、当番管理側でも存在確認と利用可否確認ができる
