# ユーザー管理BC設計書（v1）

- Status: Proposed
- Date: 2026-03-08
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/infrastructure-architecture-adr-v3.md`
  - `docs/notification-design-v1.md`

## 1. 目的 / スコープ

本書は、DDD における別 BC としての `User Management` を定義する。目的は次の 3 点である。

1. 特定 IdP に依存しないユーザー登録インターフェイスを持つこと
2. ユーザー登録通知、ユーザー情報更新通知を統合イベントとして発行できること
3. `Duty Assignment` など別 BC がそれらを購読し、自BC内の参照モデルへ反映できること

対象:
- `User Management` BC の責務境界
- 集約、ValueObject、業務操作
- IdP 非依存のアプリケーションインターフェイス
- 登録通知 / 更新通知の統合イベント契約
- 別 BC 側の購読 / 投影モデル

非対象:
- 具体的な IdP SDK 実装
- 認可モデル詳細
- 組織階層、権限ロール、グループ管理
- UI 画面遷移

## 2. Context Map

### 2.1 BC の位置づけ

- Core BC: `Duty Assignment`
- Supporting BC: `User Management`
- Supporting BC: `Facility Structure`

`User Management` は「ユーザーの正本」を持つ。  
`Duty Assignment` はユーザーの配属・当番計算に必要な最小情報だけをローカル投影として持ち、正本更新は行わない。

### 2.2 境界の引き方

`User Management` が所有するもの:
- `UserId` の採番とライフサイクル
- 社員番号など業務上のユーザー識別情報
- 氏名、表示名、メール等のプロフィール正本
- 認証主体との紐付け状態
- 登録 / 更新の統合イベント発行

`Duty Assignment` が参照のみするもの:
- `UserId`
- `EmployeeNumber`
- 在籍 / 利用可否
- 必要に応じて表示用の非正本属性

### 2.3 統合方式

- 書き込み連携: なし。別 BC からユーザー情報を直接更新しない
- 読み取り連携: 非同期の統合イベント購読 + ローカル投影
- 例外経路: 初回同期欠損時のみ、将来の補助手段として照会 API を許可する

## 3. 設計原則

1. `UserId` を業務上の正規識別子とし、IdP の subject を主キーにしない
2. ユーザー登録は「業務ユーザーの登録」と「認証主体との紐付け」を分離する
3. IdP 固有情報は ACL / adapter に閉じ込め、BC の公開インターフェイスへ漏らさない
4. 統合イベントは pull-back を減らすため、別 BC が必要とする最小スナップショットを含める
5. イベントは at-least-once 前提とし、consumer 側で冪等に投影する
6. PII 最小化方針に従い、イベントには下流BCで本当に必要な項目だけを載せる

## 4. ユビキタス言語

- `ManagedUser`: ユーザー管理BCが保持する業務ユーザー
- `UserId`: 全BC共通で参照する内部ユーザー識別子
- `EmployeeNumber`: 社員番号。業務上の一意キー
- `UserProfile`: 氏名、表示名、連絡先などのプロフィール
- `AuthIdentityLink`: 認証主体との紐付け
- `LifecycleStatus`: `PendingActivation` / `Active` / `Suspended` / `Archived`
- `RegistrationSource`: `AdminPortal` / `HrImport` / `SelfService` / `IdpProvisioning`
- `UserExport`: 他 BC へ通知するための最小スナップショット

## 5. 集約設計

## 5.1 Aggregate: `ManagedUser`

### 責務

- ユーザー登録と一意性維持
- プロフィール更新
- 利用状態変更
- 認証主体との紐付け管理
- 統合イベントの発行起点

### モデル

- AggregateRoot: `ManagedUser`
- Child Entity: `AuthIdentityLink`
- ValueObject:
  - `UserId`
  - `EmployeeNumber`
  - `DisplayName`
  - `EmailAddress`
  - `DepartmentCode`
  - `LifecycleStatus`
  - `RegistrationSource`
  - `IdentityProviderKey`
  - `IdentitySubject`

### 不変条件

1. `EmployeeNumber` はアーカイブされていないユーザー間で一意
2. 同一 `(IdentityProviderKey, IdentitySubject)` は同時に 1 ユーザーへしか紐付けできない
3. `ManagedUser` は認証主体未紐付けでも作成できる
4. `Archived` ユーザーは通常更新不可
5. 統合イベントに載せる `UserExport` は consumer 契約で定めた最小項目に限定する

### 状態

- `PendingActivation`
  - 業務ユーザーは登録済みだが、まだ利用開始していない
- `Active`
  - 別 BC が通常利用してよい状態
- `Suspended`
  - 一時停止。別 BC は新規配属やログインを拒否する
- `Archived`
  - 論理削除。新規利用不可、履歴参照のみ

### コマンド

- `RegisterUser`
- `UpdateUserProfile`
- `ActivateUser`
- `SuspendUser`
- `ArchiveUser`
- `LinkAuthIdentity`
- `UnlinkAuthIdentity`

### ドメインイベント

- `UserRegistered`
- `UserProfileUpdated`
- `UserActivated`
- `UserSuspended`
- `UserArchived`
- `AuthIdentityLinked`
- `AuthIdentityUnlinked`

### DomainError

- `DuplicateEmployeeNumberError`
- `DuplicateAuthIdentityLinkError`
- `ManagedUserAlreadyArchivedError`
- `ManagedUserNotActiveError`
- `InvalidEmailAddressError`
- `InvalidDisplayNameError`

## 5.2 Child Entity: `AuthIdentityLink`

### 責務

- IdP 固有 subject を内部 `UserId` へ対応づける
- 同一ユーザーに複数認証方式を許可する

### 属性

- `IdentityProviderKey`
- `IdentitySubject`
- `LoginHint`
- `LinkedAt`
- `LastValidatedAt`

### 補足

`AuthIdentityLink` は登録時必須ではない。  
これにより、「まず業務ユーザーを作る」「後で Entra ID / Google / 社内 SSO を紐付ける」を同一モデルで扱える。

## 6. アプリケーションインターフェイス

## 6.1 IdP 非依存の原則

公開ユースケースは vendor 固有 DTO を受け取らない。  
IdP と連携する adapter は、外部イベントや SDK の型を BC 内部の抽象型へ変換してから UseCase を呼ぶ。

### 例

- 許可する: `IdentityProviderKey`, `IdentitySubject`, `LoginHint`
- 禁止する: `MicrosoftGraphUser`, `CognitoUserType`, `FirebaseUserRecord`

## 6.2 主要ユースケース

1. `RegisterUserUseCase`
- Input: `EmployeeNumber`, `DisplayName`, `EmailAddress?`, `DepartmentCode?`, `RegistrationSource`
- Output: `UserId`, `LifecycleStatus`
- SideEffect: `ManagedUser` 作成、`UserRegistered` 発行

2. `UpdateUserProfileUseCase`
- Input: `UserId`, `DisplayName?`, `EmailAddress?`, `DepartmentCode?`
- Output: `UserId`, `Version`
- SideEffect: `UserProfileUpdated` 発行

3. `ChangeUserLifecycleUseCase`
- Input: `UserId`, `TargetStatus`
- Output: `UserId`, `Version`
- SideEffect: `UserActivated` / `UserSuspended` / `UserArchived` 発行

4. `LinkAuthIdentityUseCase`
- Input: `UserId`, `IdentityProviderKey`, `IdentitySubject`, `LoginHint?`
- Output: `UserId`, `Version`
- SideEffect: `AuthIdentityLinked` 発行

## 6.3 C# 契約イメージ

```csharp
public sealed record RegisterUserRequest(
    string EmployeeNumber,
    string DisplayName,
    string? EmailAddress,
    string? DepartmentCode,
    RegistrationSource RegistrationSource);

public sealed record LinkAuthIdentityRequest(
    Guid UserId,
    string IdentityProviderKey,
    string IdentitySubject,
    string? LoginHint);
```

この分離により、ユーザー登録そのものは IdP なしで成立する。  
IdP 主導の自動連携が必要な場合は adapter が次の順で呼ぶ。

1. `RegisterUser`
2. `LinkAuthIdentity`

## 7. 通知設計

本書でいう「通知」は、エンドユーザー通知ではなく BC 間の統合イベント通知を指す。

## 7.1 発行する統合イベント

最小セットは次の 2 種類とする。

1. `user-registry.user-registered.v1`
- 新規ユーザー登録完了時に発行

2. `user-registry.user-updated.v1`
- プロフィール更新、状態変更、認証主体紐付け変更のいずれかで発行

`user-updated` を単一イベントにまとめる理由:
- consumer 側が「ローカル投影を最新化する」だけなら event 種別を細かく分ける必要が薄い
- 下流BCの契約を増やしすぎず、バージョン互換性を維持しやすい

## 7.2 Routing Key

- `user-registry.user-registered`
- `user-registry.user-updated`

将来の RabbitMQ binding は `q.integration.v1` へ `user-registry.*` を追加する。  
ただし consumer 実装前に bind を先行導入すると未処理イベントを握りつぶすため、導入順は次の通りとする。

1. consumer 実装
2. projection 永続化
3. binding 追加

## 7.3 統合イベント共通ヘッダ

`docs/infrastructure-architecture-adr-v3.md` の契約に従う。

- `message_id`
- `event_id`
- `event_type`
- `event_schema_version`
- `occurred_at`
- `trace_id`
- `correlation_id`
- `causation_id`
- `x-retry-count`

追加ルール:
- `aggregate_id = UserId`
- `aggregate_version = ManagedUser.Version`

## 7.4 イベント本文

consumer が問い合わせなしで upsert できるよう、最小スナップショットを含める。

```json
{
  "userId": "3cb8e4f8-ec91-4e0e-9f07-2b7c3aef7e31",
  "employeeNumber": "123456",
  "lifecycleStatus": "Active",
  "departmentCode": "OPS",
  "changeType": "Registered",
  "changedFields": ["employeeNumber", "displayName", "departmentCode"],
  "version": 1,
  "occurredAt": "2026-03-08T09:00:00Z"
}
```

### PII ポリシー

- `DisplayName`, `EmailAddress` は全BC共通で必要と確定するまでは統合イベントへ載せない
- 直接識別子が必要な BC には、専用の export contract か照会 API を別途定義する
- `Duty Assignment` が現時点で必要なのは `UserId`, `EmployeeNumber`, `LifecycleStatus` が中心

## 7.5 発行ルール

1. 集約更新完了後に domain event を outbox へ保存する
2. publisher が `osouji.domain.events.v1` へ publish する
3. consumer は `(consumer_name, event_id)` で冪等化する
4. projection は `aggregate_version` が現行以下なら捨てる

## 8. 別BC側の受け方

## 8.1 受信モデル

別 BC は `User Management` を参照専用 upstream とし、ローカル projection を持つ。

推奨テーブル:
- `projection_user_directory`

推奨カラム:
- `user_id`
- `employee_number`
- `lifecycle_status`
- `department_code`
- `source_event_id`
- `aggregate_version`
- `updated_at`

## 8.2 consumer の責務

1. `user-registry.user-registered` / `user-registry.user-updated` を受信する
2. `aggregate_version` を比較し、古いイベントを無視する
3. projection を upsert する
4. 自BC の業務ルールに使う

## 8.3 Duty Assignment BC への反映

`Duty Assignment` では、ユーザー管理BCのイベントを拾って `projection_user_directory` を維持する。

利用箇所:
- `AssignUserToArea`
- `TransferUserToArea`
- 将来の通知宛先解決

期待する変更:

1. `AssignUserToAreaUseCase` の入力から `EmployeeNumber` を外す
2. `UserId` で `projection_user_directory` を引き、`EmployeeNumber` と `LifecycleStatus` を取得する
3. `Active` 以外は配属不可にする

これにより、他 BC がユーザー属性の正本を持たずに済む。

## 8.4 C# インターフェイス案

```csharp
public interface IUserDirectoryProjectionRepository
{
    Task<UserDirectoryProjection?> FindByUserIdAsync(Guid userId, CancellationToken ct);

    Task UpsertAsync(
        UserDirectoryProjection projection,
        long aggregateVersion,
        string sourceEventId,
        CancellationToken ct);
}
```

## 9. ドメインイベントと統合イベントの使い分け

`User Management` BC 内では細かい domain event を使う。  
BC 外へは consumer 契約に合わせた coarse-grained な統合イベントへ変換する。

対応:

- `UserRegistered` -> `user-registry.user-registered.v1`
- `UserProfileUpdated` -> `user-registry.user-updated.v1`
- `UserActivated` -> `user-registry.user-updated.v1`
- `UserSuspended` -> `user-registry.user-updated.v1`
- `UserArchived` -> `user-registry.user-updated.v1`
- `AuthIdentityLinked` -> `user-registry.user-updated.v1`
- `AuthIdentityUnlinked` -> `user-registry.user-updated.v1`

この分離により、BC 内部の変更理由は保ちながら、外部契約は安定させられる。

## 10. 導入順

1. `User Management` BC の集約 / repository / usecase を実装する
2. outbox 経由で `user-registry.user-registered.v1` / `user-registry.user-updated.v1` を発行する
3. `Duty Assignment` 側に `projection_user_directory` と integration consumer を実装する
4. `AssignUserToArea` / `TransferUserToArea` を投影参照方式へ変更する
5. その後に RabbitMQ binding へ `user-registry.*` を追加する

## 11. テスト観点

1. `RegisterUser` が IdP 未連携でも成功すること
2. 同一 `EmployeeNumber` の二重登録が拒否されること
3. 同一 `(IdentityProviderKey, IdentitySubject)` の二重紐付けが拒否されること
4. `user-registered` 発行後、consumer が projection を upsert できること
5. `user-updated` の旧 version を consumer が無視できること
6. `Suspended` / `Archived` ユーザーを `Duty Assignment` が新規配属拒否できること

## 12. 残課題

1. `DisplayName` や `EmailAddress` をどこまで cross-BC へ出してよいかは PII ポリシーの追加整理が必要
2. HR マスタ連携を主登録経路にする場合、`RegistrationSource` ごとの重複解消ルールを確定する必要がある
3. `UserId` 採番主体を `User Management` BC へ寄せる場合、既存 `Duty Assignment` の `UserId` 入力境界を移行する計画が必要
