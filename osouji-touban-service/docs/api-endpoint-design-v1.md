# お掃除当番システム API エンドポイント設計書（v1）

## 1. 目的 / スコープ

本書は `application-usecase-design-v1.md` に定義された Application UseCase を HTTP API にマッピングする。

対象:

- WebApi の公開エンドポイント設計
- バッチ / システム実行用の内部エンドポイント設計
- リソース URL、HTTP メソッド、ステータスコード、エラー形式、楽観排他

非対象:

- 実装コード
- 認可ポリシー詳細
- ReadModel の永続化 / 最適化詳細
- 更新直後の ReadModel 可視化待機の内部実装詳細（`docs/readmodel-write-visibility-design-v1.md` を参照）

## 2. API 共通方針

- ベースパス: `/api/v1`
- データ形式: `application/json`
- URL 命名: 複数形・kebab-case
- JSON プロパティ: `camelCase`
- ID 表現:
  - `facilityId`: UUID 文字列
  - `areaId`, `spotId`, `memberId`, `userId`, `planId`: UUID 文字列
  - `weekId`: `YYYY-Www` 形式。例: `2026-W10`
- タイムゾーン: API 入力は IANA Time Zone ID を正とする。例: `Asia/Tokyo`
- 成功レスポンス: `data` ラッパーを使用
- エラーレスポンス: `error` オブジェクトを使用
- OpenAPI: 全 endpoint / method / status で返却スキーマを明示定義する。匿名型は使用しない
- 楽観排他:
  - 単一集約更新 API は `ETag` を返却し、更新時は `If-Match` を必須とする
  - `TransferUserToArea` のような複数集約更新は `If-Match` では表現できないため、body に `fromAreaVersion` / `toAreaVersion` を持たせる

### 2.1. 成功レスポンス

```json
{
  "data": {
    "id": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6"
  }
}
```

### 2.2. エラーレスポンス

```json
{
  "error": {
    "code": "ValidationError",
    "message": "Request validation failed.",
    "details": [
      {
        "field": "employeeNumber",
        "message": "EmployeeNumber must be exactly 6 digits.",
        "code": "validation"
      },
      {
        "field": "displayName",
        "message": "DisplayName is required.",
        "code": "validation"
      }
    }
  }
}
```

ドメイン / 業務エラー時は `details` を省略し、`args` に補足識別子を含めてもよい。

### 2.3. HTTP ヘッダー

- `ETag: "7"`: 集約 version
- `If-Match: "7"`: 更新時の期待 version
- `Location`: 新規作成時の取得先 URL
- `Retry-After`: `202 Accepted` 時の再取得待機秒数
- `X-ReadModel-Visibility`: `ready | pending`

### 2.4. 更新直後の ReadModel 可視化待機

公開 mutation endpoint は、command commit 後に対応する ReadModel の可視化待機を行う。

- 可視化待機が成功した場合:
  - create は `201 Created`
  - update 系は `200 OK`
  - delete 系は `204 No Content`
- 可視化待機が timeout した場合:
  - `202 Accepted`
  - command 自体は成功済みであり、同一 request の再送は不要
  - `Location` に再取得先 resource URL を返す
  - `Retry-After` を返す
  - `X-ReadModel-Visibility: pending` を返す

`202 Accepted` は「command 未完了」ではなく、「通常の GET で観測できる ReadModel はまだ保証しない」を意味する。

`202 Accepted` body 共通形:

```json
{
  "data": {
    "resourceId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "location": "/api/v1/cleaning-areas/8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "readModelStatus": "pending",
    "version": 7
  }
}
```

補足:

- `version` は新しい aggregate version を endpoint が確定できる場合のみ返す
- delete timeout 時は削除対象の詳細 URL ではなく、通常は関連一覧または親 resource の取得先を `Location` に返す

## 3. リソースモデル

### 3.1. Facility

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

### 3.2. CleaningArea

```json
{
  "id": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
  "facilityId": "c6f1db02-6d7d-49c6-bf39-4f5d11edbb95",
  "name": "3F East",
  "currentWeekRule": {
    "startDay": "monday",
    "startTime": "09:00:00",
    "timeZoneId": "Asia/Tokyo",
    "effectiveFromWeek": "2026-W10",
    "effectiveFromWeekLabel": "2026/3/2 週"
  },
  "pendingWeekRule": {
    "startDay": "tuesday",
    "startTime": "08:30:00",
    "timeZoneId": "Asia/Tokyo",
    "effectiveFromWeek": "2026-W12",
    "effectiveFromWeekLabel": "2026/3/17 週"
  },
  "rotationCursor": 2,
  "spots": [
    {
      "id": "06f74c35-9126-4ced-bbc4-94e7f0c7df61",
      "name": "Pantry",
      "sortOrder": 10
    }
  ],
  "members": [
    {
      "id": "f8e592ee-06f4-44a0-80a7-0d37d665c38f",
      "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
      "employeeNumber": "000001",
      "displayName": null
    }
  ],
  "version": 7
}
```

`members[*].displayName` は nullable。ユーザー表示名が read model に未反映または未設定の場合、`null` を返しうる。

運用上は `0010_backfill_area_member_display_name.sql` により、active member の `displayName` 欠損を補完できる。補完優先順位は `user_id` 一致を優先し、次点で `employee_number` 一意一致を利用する。`employee_number` が複数一致する場合は補完せず、既存の有効な `displayName` は上書きしない。

`0010_backfill_area_member_display_name.sql` 実行時は、以下の観測値が `migration_area_member_display_name_backfill_runs` に 1 行記録される。

- `target_member_count`: 対象 active member 件数
- `updated_member_count`: display_name 更新/挿入件数
- `unresolved_member_count`: 実行後も display_name 欠損の件数
- `ambiguous_match_count`: employee_number 候補が複数で補完を見送った件数
- `missing_rate_before`, `missing_rate_after`: 欠損率の実行前後

運用確認クエリ例:

```sql
SELECT
   run_id,
   target_member_count,
   updated_member_count,
   unresolved_member_count,
   ambiguous_match_count,
   missing_rate_before,
   missing_rate_after,
   executed_at
FROM migration_area_member_display_name_backfill_runs
ORDER BY run_id DESC
LIMIT 5;
```

### 3.2.1. migration 後のキャッシュ反映運用

`0010_backfill_area_member_display_name.sql` は projection table を直接更新するため、イベント経由の read-model cache invalidation は発火しない。表示改善を即時確認したい場合は、以下のいずれかを実施する。

1. Redis キャッシュを明示的に無効化する（推奨）
  - 例: 対象環境 Redis に対して `FLUSHDB` または運用手順で定義済みの key pattern 削除を実行
  - 実行後に `GET /api/v1/cleaning-areas/{areaId}` を再取得して `members[*].displayName` を確認
2. 即時無効化を行わない場合
  - `Infrastructure:Redis:ReadModelDetailTtlSeconds`（既定 86400 秒）以内は旧表示が残りうる
  - TTL 経過後に自動的に再読込される

実行直後の確認手順:

1. migration 実行
2. `migration_area_member_display_name_backfill_runs` を確認し、`updated_member_count > 0` と `missing_rate_after <= missing_rate_before` を確認
3. Redis 無効化を実施
4. 代表エリアの `GET /api/v1/cleaning-areas/{areaId}` で表示名改善を確認

### 3.3. WeeklyDutyPlan

```json
{
  "id": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0",
  "areaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
  "weekId": "2026-W10",
  "weekLabel": "2026/3/2 週",
  "revision": 3,
  "status": "published",
  "assignmentPolicy": {
    "fairnessWindowWeeks": 4
  },
  "assignments": [
    {
      "spotId": "06f74c35-9126-4ced-bbc4-94e7f0c7df61",
      "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60"
    }
  ],
  "offDutyEntries": [
    {
      "userId": "b7e0c75c-55fb-43e4-a915-36e287c6aa59"
    }
  ],
  "version": 5
}
```

## 4. 公開 API

### 4.1. Facility 系

| UseCase                  | Method | Path                                         | 用途                            | 成功          |
| ------------------------ | ------ | -------------------------------------------- | ------------------------------- | ------------- |
| RegisterFacility         | `POST` | `/api/v1/facilities`                         | Facility 新規登録               | `201 Created` |
| GetFacility              | `GET`  | `/api/v1/facilities/{facilityId}`            | Facility 詳細取得               | `200 OK`      |
| ListFacilities           | `GET`  | `/api/v1/facilities`                         | Facility 一覧 / 絞り込み        | `200 OK`      |
| UpdateFacility           | `PUT`  | `/api/v1/facilities/{facilityId}`            | Facility 編集                   | `200 OK`      |
| ChangeFacilityActivation | `PUT`  | `/api/v1/facilities/{facilityId}/activation` | Facility Active / Inactive 切替 | `200 OK`      |

#### 4.1.1. `POST /api/v1/facilities`

```json
{
  "facilityCode": "TOKYO-HQ",
  "name": "Tokyo Head Office",
  "description": "Main office building",
  "timeZoneId": "Asia/Tokyo"
}
```

- `Location: /api/v1/facilities/{facilityId}`
- エラー: `400`, `409`

#### 4.1.2. `GET /api/v1/facilities`

クエリ:

- `query`: `facilityCode` / `name` の前方一致または部分一致
- `status`: `active`, `inactive`
- `cursor`: カーソル
- `limit`: 1-100、既定 20
- `sort`: `name`, `-name`, `facilityCode`, `-facilityCode`

#### 4.1.3. `PUT /api/v1/facilities/{facilityId}`

ヘッダー:

- `If-Match: "4"`

```json
{
  "name": "Tokyo Head Office Annex",
  "description": "Main office building",
  "timeZoneId": "Asia/Tokyo"
}
```

- エラー: `400`, `404`, `409`

#### 4.1.4. `PUT /api/v1/facilities/{facilityId}/activation`

ヘッダー:

- `If-Match: "4"`

```json
{
  "lifecycleStatus": "inactive"
}
```

- エラー: `404`, `409`

### 4.2. CleaningArea 系

| UseCase                    | Method   | Path                                                | 用途                     | 成功             |
| -------------------------- | -------- | --------------------------------------------------- | ------------------------ | ---------------- |
| RegisterCleaningArea       | `POST`   | `/api/v1/cleaning-areas`                            | エリア新規登録           | `201 Created`    |
| GetCleaningArea            | `GET`    | `/api/v1/cleaning-areas/{areaId}`                   | エリア詳細取得           | `200 OK`         |
| GetCleaningAreaCurrentWeek | `GET`    | `/api/v1/cleaning-areas/{areaId}/current-week`      | エリア定義上の現在週解決 | `200 OK`         |
| ListCleaningAreas          | `GET`    | `/api/v1/cleaning-areas`                            | エリア一覧 / 絞り込み    | `200 OK`         |
| ScheduleWeekRuleChange     | `PUT`    | `/api/v1/cleaning-areas/{areaId}/pending-week-rule` | 次回週ルール予約         | `200 OK`         |
| AddCleaningSpot            | `POST`   | `/api/v1/cleaning-areas/{areaId}/spots`             | 掃除箇所追加             | `201 Created`    |
| RemoveCleaningSpot         | `DELETE` | `/api/v1/cleaning-areas/{areaId}/spots/{spotId}`    | 掃除箇所削除             | `204 No Content` |
| AssignUserToArea           | `POST`   | `/api/v1/cleaning-areas/{areaId}/members`           | メンバー所属             | `201 Created`    |
| UnassignUserFromArea       | `DELETE` | `/api/v1/cleaning-areas/{areaId}/members/{userId}`  | メンバー離脱             | `204 No Content` |
| TransferUserToArea         | `POST`   | `/api/v1/area-member-transfers`                     | エリア間異動             | `200 OK`         |

#### 4.2.1. `POST /api/v1/cleaning-areas`

```json
{
  "facilityId": "c6f1db02-6d7d-49c6-bf39-4f5d11edbb95",
  "areaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
  "name": "3F East",
  "initialWeekRule": {
    "startDay": "monday",
    "startTime": "09:00:00",
    "timeZoneId": "Asia/Tokyo",
    "effectiveFromWeek": "2026-W10"
  },
  "initialSpots": [
    {
      "spotId": "06f74c35-9126-4ced-bbc4-94e7f0c7df61",
      "spotName": "Pantry",
      "sortOrder": 10
    }
  ]
}
```

- `Location: /api/v1/cleaning-areas/{areaId}`
- エラー: `400`, `404`, `409`

#### 4.2.2. `GET /api/v1/cleaning-areas`

クエリ:

- `facilityId`: 所属 Facility で絞り込み
- `userId`: 所属ユーザーで絞り込み
- `cursor`: カーソル
- `limit`: 1-100、既定 20
- `sort`: `name`, `-name`

一覧は将来 ReadModel 実装に載せる。`Location` 先と UI 参照のため契約だけ先に固定する。

#### 4.2.3. `GET /api/v1/cleaning-areas/{areaId}/current-week`

```json
{
  "data": {
    "areaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "weekId": "2026-W10",
    "weekLabel": "2026/3/2 週",
    "timeZoneId": "Asia/Tokyo"
  }
}
```

- フロントエンドが `WeekRule` の週解決ロジックを再実装しないための helper read API
- `weekId` は識別子、`weekLabel` は `WeekRule.startDay` 基準の表示用ラベル
- `404`: 対象エリアが存在しない

#### 4.2.4. `PUT /api/v1/cleaning-areas/{areaId}/pending-week-rule`

ヘッダー:

- `If-Match: "7"`

```json
{
  "startDay": "tuesday",
  "startTime": "08:30:00",
  "timeZoneId": "Asia/Tokyo",
  "effectiveFromWeek": "2026-W12"
}
```

- 既存 pending rule は置換扱い
- エラー: `400`, `404`, `409`

#### 4.2.5. `POST /api/v1/cleaning-areas/{areaId}/spots`

ヘッダー:

- `If-Match: "7"`

```json
{
  "spotId": "1cd59cf9-1af4-4f79-b729-120b1fe9be9e",
  "name": "Meeting Room",
  "sortOrder": 20
}
```

- `Location: /api/v1/cleaning-areas/{areaId}/spots/{spotId}`
- エラー: `404`, `409`

#### 4.2.6. `DELETE /api/v1/cleaning-areas/{areaId}/spots/{spotId}`

ヘッダー:

- `If-Match: "8"`

仕様:

- 対象 `areaId` が存在しない場合は `404`
- `spotId` が未登録でも `204` を返す
- 最後の 1 件を削除しようとした場合は `409 CleaningAreaHasNoSpotError`

#### 4.2.7. `POST /api/v1/cleaning-areas/{areaId}/members`

ヘッダー:

- `If-Match: "7"`

```json
{
  "memberId": "f8e592ee-06f4-44a0-80a7-0d37d665c38f",
  "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
  "employeeNumber": "000001"
}
```

- `memberId` は省略可能。省略時はサーバー採番
- `Location: /api/v1/cleaning-areas/{areaId}/members/{userId}`
- エラー: `400`, `404`, `409`

#### 4.2.8. `DELETE /api/v1/cleaning-areas/{areaId}/members/{userId}`

ヘッダー:

- `If-Match: "8"`

仕様:

- 対象 `areaId` が存在しない場合は `404`
- `userId` が未所属でも `204` を返す

#### 4.2.9. `POST /api/v1/area-member-transfers`

```json
{
  "fromAreaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
  "toAreaId": "f04b375f-a351-4a19-af40-06184b98a3be",
  "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
  "toAreaMemberId": "5a1b2254-c449-4a87-b661-ead4502dc406",
  "employeeNumber": "E0001",
  "fromAreaVersion": 7,
  "toAreaVersion": 12
}
```

```json
{
  "data": {
    "fromAreaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
    "toAreaId": "f04b375f-a351-4a19-af40-06184b98a3be",
    "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
    "transferred": true
  }
}
```

- `fromAreaId == toAreaId` は `409 InvalidTransferRequest`
- `fromArea` に未所属の `userId` は no-op とせず、仕様明確化のため `409 InvalidTransferRequest` に寄せる案もあり得るが、現実装は `fromArea.UnassignUser` が no-op になる
- v1 は現実装準拠で `200 OK` とし、必要なら v2 で厳格化する

### 4.2. WeeklyDutyPlan 系

| UseCase             | Method | Path                                             | 用途               | 成功          |
| ------------------- | ------ | ------------------------------------------------ | ------------------ | ------------- |
| GenerateWeeklyPlan  | `POST` | `/api/v1/weekly-duty-plans`                      | 週次計画生成       | `201 Created` |
| GetWeeklyDutyPlan   | `GET`  | `/api/v1/weekly-duty-plans/{planId}`             | 計画詳細取得       | `200 OK`      |
| ListWeeklyDutyPlans | `GET`  | `/api/v1/weekly-duty-plans`                      | 計画一覧 / 検索    | `200 OK`      |
| PublishWeeklyPlan   | `PUT`  | `/api/v1/weekly-duty-plans/{planId}/publication` | 公開状態へ遷移     | `200 OK`      |
| CloseWeeklyPlan     | `PUT`  | `/api/v1/weekly-duty-plans/{planId}/closure`     | クローズ状態へ遷移 | `200 OK`      |

#### 4.3.1. `POST /api/v1/weekly-duty-plans`

```json
{
  "areaId": "8be9c0eb-7c33-4dd5-bf97-700d66f65ca6",
  "weekId": "2026-W10",
  "policy": {
    "fairnessWindowWeeks": 4
  }
}
```

```json
{
  "data": {
    "planId": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0",
    "weekId": "2026-W10",
    "weekLabel": "2026/3/2 週",
    "revision": 1,
    "status": "draft"
  }
}
```

- `Location: /api/v1/weekly-duty-plans/{planId}`
- エラー: `404`, `409`

#### 4.3.2. `GET /api/v1/weekly-duty-plans`

クエリ:

- `areaId`
- `weekId`
- 応答では `weekId` に加えて `weekLabel` を返す。`weekLabel` は適用される `WeekRule.startDay` 基準
- `status`: `draft`, `published`, `closed`
- `cursor`
- `limit`: 1-100、既定 20
- `sort`: `weekId`, `-weekId`, `createdAt`, `-createdAt`

`areaId + weekId` は重複防止キーのため、単一件取得用途にも利用できる。

#### 4.3.3. `PUT /api/v1/weekly-duty-plans/{planId}/publication`

ヘッダー:

- `If-Match: "5"`

レスポンス:

```json
{
  "data": {
    "planId": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0",
    "status": "published"
  }
}
```

- `Closed -> Published` は `409 WeekAlreadyClosedError`
- `Draft -> Published` と `Published -> Published` はともに `200`

#### 4.3.4. `PUT /api/v1/weekly-duty-plans/{planId}/closure`

ヘッダー:

- `If-Match: "6"`

レスポンス:

```json
{
  "data": {
    "planId": "39a2d91f-3984-4e33-b418-0bbccfe1e4d0",
    "status": "closed"
  }
}
```

- 既に `Closed` の場合も `200` を返す
- 現実装上 no-op でも、最新 `ETag` を返してクライアント同期を容易にする

### 4.3. UserManagement 系

| UseCase             | Method  | Path                                    | 用途                | 成功          |
| ------------------- | ------- | --------------------------------------- | ------------------- | ------------- |
| RegisterUser        | `POST`  | `/api/v1/users`                         | ユーザー新規登録    | `201 Created` |
| ListUsers           | `GET`   | `/api/v1/users`                         | ユーザー一覧 / 検索 | `200 OK`      |
| GetUser             | `GET`   | `/api/v1/users/{userId}`                | ユーザー詳細取得    | `200 OK`      |
| UpdateUserProfile   | `PATCH` | `/api/v1/users/{userId}`                | プロフィール更新    | `200 OK`      |
| ChangeUserLifecycle | `POST`  | `/api/v1/users/{userId}/lifecycle`      | ライフサイクル変更  | `200 OK`      |
| LinkAuthIdentity    | `POST`  | `/api/v1/users/{userId}/identity-links` | 認証主体紐付け      | `200 OK`      |

#### 4.4.1. `GET /api/v1/users`

クエリ:

- `query`: `employeeNumber` / `displayName` / `departmentCode` の部分一致
- `status`: `pendingActivation`, `active`, `suspended`, `archived`
- `cursor`
- `limit`: 1-100、既定 20
- `sort`: `displayName`, `-displayName`, `employeeNumber`, `-employeeNumber`

レスポンス例:

```json
{
  "data": [
    {
      "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
      "employeeNumber": "000001",
      "displayName": "Hanako",
      "lifecycleStatus": "active",
      "departmentCode": "OPS",
      "version": 3
    }
  ],
  "meta": {
    "limit": 20,
    "hasNext": false,
    "nextCursor": null
  },
  "links": {
    "self": "/api/v1/users?status=active&sort=displayName"
  }
}
```

用途:

- WebUI のユーザー一覧表示
- `CleaningArea` への所属追加時の候補検索
- `status=active` を使ったアサイン可能ユーザーの絞り込み

#### 4.4.2. `GET /api/v1/users/{userId}`

```json
{
  "data": {
    "userId": "4a8f4ec2-b164-4da7-8132-4f527e054a60",
    "employeeNumber": "000001",
    "displayName": "Hanako",
    "emailAddress": "hanako@example.com",
    "departmentCode": "OPS",
    "lifecycleStatus": "active",
    "version": 3
  }
}
```

- `ETag: "3"`
- 編集 UI の初期表示と `If-Match` 同期に利用する

## 5. 内部 API

内部 API はバッチ、運用ジョブ、システム連携専用とする。外部ユーザー向けには公開しない。

| UseCase                       | Method | Path                                             | 成功     |
| ----------------------------- | ------ | ------------------------------------------------ | -------- |
| ApplyDueWeekRuleChanges       | `POST` | `/api/v1/internal/week-rule-applications`        | `200 OK` |
| GenerateCurrentWeekPlansBatch | `POST` | `/api/v1/internal/current-week-plan-generations` | `200 OK` |

### 5.1. `POST /api/v1/internal/week-rule-applications`

```json
{
  "currentWeek": "2026-W10"
}
```

- `currentWeek` 省略時はサーバー時計から解決

```json
{
  "data": {
    "appliedCount": 4
  }
}
```

### 5.2. `POST /api/v1/internal/current-week-plan-generations`

```json
{
  "policy": {
    "fairnessWindowWeeks": 4
  }
}
```

```json
{
  "data": {
    "generatedCount": 12,
    "skippedCount": 3,
    "failedCount": 1
  }
}
```

## 6. 非公開 UseCase の扱い

以下の UseCase は DomainEvent により同期起動される内部オーケストレーションであり、v1 公開 API では直接公開しない。

- `RebalanceForUserAssignedUseCase`
- `RebalanceForUserUnassignedUseCase`
- `RecalculateForSpotChangedUseCase`

理由:

- クライアントの業務意図は「メンバー追加」「メンバー削除」「掃除箇所変更」であり、再配分そのものではない
- 直接公開すると計画再計算のトリガー責務が API 利用者へ漏れる
- 現仕様では EventHandler が自動実行するため、公開 API に露出させる必然性がない

運用上の手動再実行が必要になった場合は、`/api/v1/internal/weekly-duty-plans/{planId}/recalculations` 系の内部 API を別版で追加する。

## 7. エラーマッピング

| HTTP                        | Code                                                                                                                                                                                                                                                                                                                          | 主な発生条件                        |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------- |
| `400 Bad Request`           | `InvalidWeekIdError`, `InvalidWeekRuleError`, `InvalidWeekRuleTimeZoneError`, `InvalidEmployeeNumberError`                                                                                                                                                                                                                    | JSON は正しいが入力値が不正         |
| `404 Not Found`             | `NotFound`                                                                                                                                                                                                                                                                                                                    | `areaId`, `planId` などの対象未存在 |
| `409 Conflict`              | `RepositoryConcurrency`, `RepositoryDuplicate`, `WeeklyPlanAlreadyExists`, `DuplicateCleaningSpotError`, `DuplicateAreaMemberError`, `UserAlreadyAssignedToAnotherAreaError`, `WeekAlreadyClosedError`, `InvalidTransferRequest`, `CleaningAreaHasNoSpotError`, `NoAvailableUserForSpotError`, `InvalidRebalanceRequestError` | 業務競合 / 保存競合 / 重複          |
| `500 Internal Server Error` | `Unexpected`                                                                                                                                                                                                                                                                                                                  | 未処理障害                          |

補足:

- `RepositoryConcurrency` は `If-Match` 不一致または body version 不一致として扱う
- `DELETE` の no-op はエラーにしない

## 8. ページング / 検索

一覧系は v1 ではカーソル方式を採用する。

例:

```http
GET /api/v1/weekly-duty-plans?areaId=8be9c0eb-7c33-4dd5-bf97-700d66f65ca6&cursor=eyJ3ZWVrSWQiOiIyMDI2LVcxMCIsImlkIjoiMzkifQ&limit=20
```

```json
{
  "data": [],
  "meta": {
    "limit": 20,
    "hasNext": false,
    "nextCursor": null
  },
  "links": {
    "self": "/api/v1/weekly-duty-plans?areaId=8be9c0eb-7c33-4dd5-bf97-700d66f65ca6&limit=20"
  }
}
```

## 9. 認証 / 認可の前提

- 全 API は認証必須
- 公開 API:
  - `cleaning-areas:*` 権限を要求
  - `weekly-duty-plans:*` 権限を要求
- 内部 API:
  - バッチ / サービスアカウントのみ許可

詳細な RBAC は別設計とする。

## 10. 実装優先順位

1. `POST /api/v1/cleaning-areas`
2. `PUT /api/v1/cleaning-areas/{areaId}/pending-week-rule`
3. `POST` / `DELETE` の spot/member 操作
4. `POST /api/v1/area-member-transfers`
5. `POST /api/v1/weekly-duty-plans`
6. `PUT` publication / closure
7. `GET` 系 ReadModel API
8. `POST /api/v1/internal/*` バッチ API

## 11. 補足判断

- `ScheduleWeekRuleChange` は action URL ではなく `pending-week-rule` というサブリソースへ正規化した
- `PublishWeeklyPlan` / `CloseWeeklyPlan` は verb URL を避け、状態遷移先を表す `publication` / `closure` を採用した
- `TransferUserToArea` は単一集約ではないため、`cleaning-areas/{id}` 配下へ無理にねじ込まず、独立した operation resource とした
- 参照系 UseCase は現仕様書にないが、`Location` ヘッダーと UI 利用のため GET 契約のみ先に定義した
