# お掃除当番システム 通知設計書（v1）

- Status: Draft
- Date: 2026-03-08
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/infrastructure-architecture-adr-v3.md`
  - `docs/readmodel-cqrs-design-v1.md`

## 1. 目的 / スコープ

本書は、今週のお掃除当番について「担当確定時」と「担当変更時」に、非同期でユーザー通知を行う機能の設計を定義する。

対象:

- 通知トリガーとなるドメインイベント
- 通知の責務分離（イベント解釈 / 配信抽象 / チャネル実装）
- RabbitMQ notification consumer を使った非同期配信フロー
- 冪等性、再送、重複防止
- 今回実装した通知モデルと拡張ポイント

非対象:

- Slack / Email / Push など個別プラットフォームの具体 API 実装
- ユーザープロファイル管理（メールアドレス、Slack ID 等の保管）
- 通知文面テンプレートの CMS 化
- 既読管理、通知一覧 UI

## 2. 背景

現状のシステムは、Event Store + Outbox + RabbitMQ による非同期イベント配送基盤を持つ。

既存状態:

- `weekly-plan.*` 系イベントは `q.notification.v1` へ配送される
- consumer の retry / DLQ / `(consumer_name, event_id)` 単位の冪等制御は実装済み
- ただし通知 consumer の業務ロジックは未実装で、実際の通知送信は行われていなかった

今回の目的は、既存の messaging 基盤を活かしつつ、通知チャネルに依存しない通知モデルを導入することにある。

## 3. 要件整理

### 3.1. 機能要件

1. 今週の担当が確定したタイミングで対象ユーザーへ通知する
2. 今週の担当が変更されたタイミングで対象ユーザーへ通知する
3. 通知プラットフォームに依存しない形で通知要求を表現する
4. RabbitMQ の再送が発生しても、同じ通知を二重送信しない

### 3.2. 非機能要件

1. コマンド処理とは非同期に通知する
2. 既存 Outbox / RabbitMQ retry / DLQ 方針に従う
3. 新しい通知チャネル追加時にドメインイベント解釈ロジックを変更しなくてよい
4. 送信失敗時は at-least-once を維持し、成功済みチャネルには再送しない

## 4. 通知トリガー

採用する通知トリガーは以下の 2 種類とする。

| イベント                 | routing key                | 意味                                 | 通知種別 |
| ------------------------ | -------------------------- | ------------------------------------ | -------- |
| `WeeklyPlanPublished`    | `weekly-plan.published`    | 週次計画が公開され、担当が確定した   | 確定通知 |
| `WeeklyPlanRecalculated` | `weekly-plan.recalculated` | 公開済み週次計画の担当が再計算された | 変更通知 |

不採用としたイベント:

- `DutyAssigned`
- `DutyReassigned`
- `UserMarkedOffDuty`

理由:

- これらは spot / user 単位の細粒度イベントであり、1 回の再計算で複数発生する
- 通知要件は「そのユーザーに見せる最終状態」であり、イベント単位で送ると重複・分断された通知になりやすい
- 通知生成時に最新の公開済み plan を読んで集約する方が、ユーザー向けメッセージとして自然である

## 5. 設計方針

### 5.1. 責務分離

通知機能は以下の 3 層に分離する。

1. イベント解釈

   - RabbitMQ から受信したメッセージを通知対象へ変換する
   - 対象イベントか、今週か、公開済みかを判定する

2. 通知論理モデル

   - 配信先プラットフォームに依存しない `UserNotification` を生成する
   - 送信対象ユーザー、通知種別、件名、本文、メタデータを保持する

3. 通知チャネル配信

   - `INotificationChannel` を通じて Email / Slack / Push 等へ送る
   - 送信済み記録を保持し、チャネル単位で重複送信を防ぐ

### 5.2. 同期境界

- コマンド処理: Event Store 保存 + Outbox 登録まで
- 非同期処理: Outbox publish 後、RabbitMQ notification consumer が通知を作成・送信

このため、通知失敗はユーザー操作をロールバックしない。

## 6. アーキテクチャ

### 6.1. 主要インターフェイス

Application 層:

```csharp
public sealed record UserNotification(
    string NotificationId,
    string NotificationType,
    Guid RecipientUserId,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Metadata);

public interface INotificationDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<UserNotification> notifications,
        CancellationToken ct);
}

public interface INotificationChannel
{
    string ChannelName { get; }

    Task SendAsync(
        UserNotification notification,
        CancellationToken ct);
}
```

Infrastructure 層:

- `INotificationRabbitMqMessageHandler`
- `IIntegrationRabbitMqMessageHandler`
- `INotificationDeliveryLogRepository`
- `WeeklyPlanNotificationFactory`
- `NotificationDispatcher`

### 6.2. 処理フロー

1. `PublishWeeklyPlanUseCase` が `WeeklyPlanPublished` を発行する
2. `OutboxDomainEventDispatcher` が event を outbox に書き込む
3. `OutboxPublisherWorker` が RabbitMQ へ publish する
4. `q.notification.v1` が `weekly-plan.published` / `weekly-plan.recalculated` を受信する
5. `NotificationConsumerWorker` が `NotificationRabbitMqMessageHandler` を呼ぶ
6. `WeeklyPlanNotificationFactory` が event を解釈し `UserNotification[]` を作る
7. `NotificationDispatcher` が登録済み `INotificationChannel` へ送信する
8. 成功した `(channel_name, notification_id)` を記録する

## 7. 通知生成ルール

### 7.1. 前提チェック

通知生成時に以下を確認する。

1. メッセージヘッダに `event_id` が存在すること
2. routing key が `weekly-plan.published` または `weekly-plan.recalculated` であること
3. 対応する `WeeklyDutyPlan` が取得できること
4. `WeeklyDutyPlan.Status == Published` であること
5. plan の `WeekId` が area の `WeekRule` から解決した「今週」と一致すること

上記を満たさない場合:

- 対象外イベントは no-op
- plan / area 不整合は例外として扱い retry 対象にする
- 今週でない、未公開の plan は通知しない

### 7.2. 宛先決定

通知対象は plan の最終状態から決定する。

1. `Assignments`

   - `UserId` ごとに group 化する
   - 同一ユーザーに複数 spot がある場合は 1 通にまとめる

2. `OffDutyEntries`

   - 担当なしユーザーとして 1 通送る

### 7.3. 通知種別

| 条件                                  | NotificationType                        |
| ------------------------------------- | --------------------------------------- |
| `WeeklyPlanPublished` + assignment    | `weekly-duty-plan.assignment.confirmed` |
| `WeeklyPlanPublished` + off-duty      | `weekly-duty-plan.off-duty.confirmed`   |
| `WeeklyPlanRecalculated` + assignment | `weekly-duty-plan.assignment.changed`   |
| `WeeklyPlanRecalculated` + off-duty   | `weekly-duty-plan.off-duty.changed`     |

### 7.4. 文面ポリシー

本文は通知プラットフォーム共通で最低限の情報を含める。

必須要素:

- エリア名
- `WeekId`
- 担当 spot 一覧、または担当なし
- 確定通知か変更通知か

プラットフォーム固有の装飾は `INotificationChannel` 側で加えてよい。

### 7.5. メタデータ

`UserNotification.Metadata` に以下を保持する。

- `planId`
- `areaId`
- `areaName`
- `weekId`
- `revision`
- `assignmentState`
- `spotNames`

目的:

- チャネル実装でリンク組み立てやテンプレート分岐に利用できるようにする

## 8. 冪等性 / 重複防止

### 8.1. consumer 単位の冪等性

RabbitMQ consumer 基盤では既に `(consumer_name, event_id)` で処理済み管理している。

これにより:

- 同一 event の再配信時に consumer 全体として多重処理を避けられる

### 8.2. チャネル単位の重複防止

通知配信ではさらに `(channel_name, notification_id)` で成功記録を保持する。

テーブル:

- `notification_channel_deliveries`

主キー:

- `(channel_name, notification_id)`

これにより:

- Email は成功済みだが Slack は失敗、のような部分成功を扱える
- retry 時は未成功チャネルだけを再送できる

### 8.3. NotificationId 設計

`NotificationId` は以下の形式を採用する。

`{eventId}:{userId}:{state}`

`state`:

- `assigned`
- `off-duty`

意図:

- 同じ event に対する同一ユーザー向け通知を一意に識別する
- 1 ユーザーに複数 spot があっても 1 通に集約する

## 9. データモデル

### 9.1. `notification_channel_deliveries`

```sql
CREATE TABLE IF NOT EXISTS notification_channel_deliveries (
    channel_name TEXT NOT NULL,
    notification_id TEXT NOT NULL,
    notification_type TEXT NOT NULL,
    recipient_user_id UUID NOT NULL,
    title TEXT NOT NULL,
    delivered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT pk_notification_channel_deliveries PRIMARY KEY (channel_name, notification_id)
);
```

用途:

- チャネルごとの送信成功履歴
- 再送時の重複防止
- 障害調査時の最低限の監査補助

非保持項目:

- 本文全文
- プラットフォーム宛先 ID

理由:

- 本テーブルの主目的は監査正本ではなく冪等制御である
- PII / 可変テンプレート依存を減らすため、保存項目は必要最小限とする

## 10. DI / 実装配置

### 10.1. 登録方針

EventStore モードでは以下を登録する。

- `INotificationDispatcher -> NotificationDispatcher`
- `INotificationRabbitMqMessageHandler -> NotificationRabbitMqMessageHandler`
- `IIntegrationRabbitMqMessageHandler -> NoopIntegrationRabbitMqMessageHandler`
- `INotificationDeliveryLogRepository -> NotificationDeliveryLogRepository`
- `WeeklyPlanNotificationFactory`

### 10.2. consumer 分離

`RabbitMqConsumerWorkerBase<TMessageHandler>` を generic 化し、通知 consumer と integration consumer で別ハンドラを注入する。

効果:

- 通知 consumer の業務ロジックを integration 用 no-op 実装と分離できる
- 将来 `q.integration.v1` 側に外部連携処理を追加しても通知ロジックと干渉しない

## 11. 拡張ポイント

### 11.1. 新しい通知チャネル追加

追加手順:

1. `INotificationChannel` 実装を追加する
2. DI に登録する
3. 必要なら `Metadata` を参照してプラットフォーム向け payload を生成する

既存ロジックへの影響:

- `WeeklyPlanNotificationFactory` は変更不要
- `NotificationDispatcher` が自動で全チャネルへ配信する

### 11.2. ユーザー連絡先解決

現在の通知モデルは `RecipientUserId` までしか持たない。

将来必要な拡張:

- `IUserNotificationDestinationResolver`
- `UserId -> Email / SlackMemberId / PushToken` 解決

この責務は通知チャネルまたは専用 resolver に持たせ、core domain には持ち込まない。

### 11.3. 差分通知の高度化

現行実装は「再計算後の最終状態」を通知する。

未対応:

- 変更前後差分の明示
- 変更されたユーザーだけへの限定通知

これが必要になった場合の候補:

1. `WeeklyPlanRecalculated` に差分 payload を持たせる
2. 変更前 revision を read model / history から比較する
3. 通知専用 projection を作る

## 12. 制約 / 既知のトレードオフ

1. `WeeklyPlanRecalculated` だけでは厳密な差分受信者は分からない

   - そのため現行設計では「公開済みの今週 plan の現状態に含まれる全対象者」へ変更通知する

2. 連絡先情報はまだ存在しない

   - 本実装は通知要求生成までを確定し、具体チャネル実装は後続とする

3. 配信ログは最低限

   - 本文や外部 message id は保存していないため、詳細な監査要件が必要なら別テーブル設計が必要

## 13. エラーハンドリング方針

1. 一時障害

   - channel 送信失敗時は例外を投げ、RabbitMQ retry に委譲する

2. 部分成功

   - 既に成功したチャネルは `notification_channel_deliveries` により再送しない
   - 未成功チャネルだけ再試行される

3. 恒久障害

   - retry 上限超過後は notification DLQ へ移送する

4. 対象外イベント

   - no-op として ACK する

## 14. テスト方針

優先テスト:

1. `weekly-plan.published` 受信時に今週かつ公開済み plan だけ通知が生成されること
2. assignment / off-duty の通知種別と本文が正しいこと
3. 今週以外の plan は通知対象外になること
4. 既に成功済みチャネルには retry 時も再送しないこと
5. DI で通知 dispatcher が解決できること

## 15. 今回実装した範囲

実装済み:

- プラットフォーム非依存の通知契約
- notification consumer 専用ハンドラ
- 今週 / 公開済み判定を含む通知生成
- `(channel_name, notification_id)` 単位の重複防止
- 送信履歴テーブル migration
- 基本テスト

未実装:

- Email / Slack / Push の具体チャネル
- 変更差分の厳密抽出
- ユーザー連絡先解決
