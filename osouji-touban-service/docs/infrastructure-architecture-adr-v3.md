# お掃除当番システム Infrastructure Architecture ADR（v3 / Governance & Messaging SLO）

- Status: Accepted
- Date: 2026-03-05
- Extends: `docs/infrastructure-architecture-adr-v2.md`
- Decision Makers: Core/Platform Team
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/infrastructure-architecture-adr-v2.md`

## 1. 目的
v2 の「次 ADR へ持ち越し」3項目を確定する。

1. 監査ログ保持年数、PII 匿名化ポリシー
2. RabbitMQ トポロジ（Exchange/Queue/Retry/DLQ）
3. SLI/SLO 目標値（p95、lag、エラー率）

## 2. 決定サマリ

| 領域 | 決定 |
|---|---|
| 監査ログ保持 | Event Store/Snapshot は 7 年保持（法務・監査向け正本） |
| PII 方針 | 最小化・秘匿化・ログ匿名化を強制し、非本番で実値 PII を禁止 |
| RabbitMQ | Topic Exchange + Consumer別Queue + 段階 Retry Queue + 専用 DLQ |
| SLO 評価窓 | 月次（暦月）で評価、計画メンテナンスは除外上限 2 時間/月 |

## 3. 監査ログ保持年数と PII 匿名化ポリシー

## 3.1 データ区分と保持年数

| データ | 保持期間 | 根拠 |
|---|---|---|
| `event_store_events` | 7年 | 監査証跡の正本、履歴再演算 |
| `event_store_snapshots` | 7年（世代は最新+月次末） | 高速復元と監査再現性 |
| `outbox_messages`（published） | 180日 | 配信追跡と障害調査 |
| `outbox_messages`（failed / 未解決） | 1年 | 恒久障害の事後分析 |
| RabbitMQ DLQ メッセージ | 30日 | 再処理判断の運用窓 |
| アプリケーション構造化ログ | 180日 | 通常障害調査 |
| 分散トレース | 14日 | 性能劣化の短期分析 |
| メトリクス | 13か月 | 季節性比較と容量計画 |

補足:
- 保持期限到達データは日次バッチで削除する。
- Event Store の 7年満了削除は月次で実施し、削除監査ログ（件数・期間）を別途 2 年保持する。

## 3.2 PII の定義と取り扱い
本システムで PII 扱いとする項目:
- `EmployeeNumber`
- `UserId`（単独では疑似識別子だが、他データ結合で個人識別可能なため準PII扱い）

ルール:
1. Event payload / metadata には氏名・メール等の直接識別子を保存しない。
2. `EmployeeNumber` は DB 内では業務要件上保持可。ただしログ出力は禁止。
3. ログ・メトリクス・トレースに出す ID は `HMAC-SHA256(tenant_salt, original_id)` で匿名化する。
4. 画面/API 応答で表示する `EmployeeNumber` は原則マスキング（例: `12****`）。管理者のみ復号可能。
5. 非本番環境は匿名化済みテストデータのみ利用し、本番ダンプ持ち込みを禁止する。
6. バックアップは暗号化（KMS 管理鍵）を必須とし、復元時も同ポリシーを適用する。

## 4. RabbitMQ トポロジ詳細

## 4.1 Exchange
1. `osouji.domain.events.v1`（type: `topic`, durable）
- Outbox publisher の標準 publish 先。

2. `osouji.domain.retry.v1`（type: `direct`, durable）
- Retry 待機キューへの振り分け用。

3. `osouji.domain.dlq.v1`（type: `topic`, durable）
- 再試行上限超過メッセージの退避先。

## 4.2 Routing Key 規約
- `weekly-plan.generated`
- `weekly-plan.recalculated`
- `weekly-plan.published`
- `weekly-plan.closed`
- `cleaning-area.user-assigned`
- `cleaning-area.user-unassigned`
- `cleaning-area.spot-added`
- `cleaning-area.spot-removed`

## 4.3 Queue 構成（consumer単位）
1. 通知系
- Primary: `q.notification.v1`
- Bind: `weekly-plan.*`
- Retry: `q.notification.retry.1m`, `q.notification.retry.5m`, `q.notification.retry.30m`
- DLQ: `q.notification.dlq.v1`

2. 外部連携系（将来拡張）
- Primary: `q.integration.v1`
- Bind: `weekly-plan.*`, `cleaning-area.*`
- Retry: `q.integration.retry.1m`, `q.integration.retry.5m`, `q.integration.retry.30m`
- DLQ: `q.integration.dlq.v1`

## 4.4 Retry / DLQ ポリシー
1. Primary queue 消費失敗時:
- `x-retry-count` を +1 して `osouji.domain.retry.v1` へ再送する。

2. Retry queue:
- 各 queue は TTL 付き（1m / 5m / 30m）。
- TTL 到達後、`osouji.domain.events.v1` へ dead-letter し再配信する。

3. 上限:
- `x-retry-count >= 5` で `osouji.domain.dlq.v1` へ送る。
- DLQ 監視アラートを即時発火（P1 ではないが当日対応）。

## 4.5 メッセージ契約（必須ヘッダ）
- `message_id`（UUID）
- `event_id`（Event Store の `event_id`）
- `event_type`
- `event_schema_version`
- `occurred_at`
- `trace_id`
- `correlation_id`
- `causation_id`
- `x-retry-count`

冪等性:
- Consumer は `(consumer_name, event_id)` の処理履歴テーブルで重複実行を抑止する。

## 5. SLI/SLO 目標値（確定）

## 5.1 SLO 一覧（月次評価）

| SLI | 定義 | SLO 目標 |
|---|---|---|
| API 可用性 | `2xx/3xx/4xx` を成功、`5xx` を失敗として算出 | 99.9%以上 |
| Command API 応答時間 p95 | 書き込み系 API 全体（`/api/*` command） | 500ms 以下 |
| Query API 応答時間 p95 | 参照系 API 全体 | 250ms 以下 |
| Projection lag p95 | `now - projection_checkpoints.updated_at` | 10秒 以下 |
| Outbox publish lag p95 | `published_at - created_at` | 15秒 以下 |
| RabbitMQ consumer error rate | `(nack + exception) / total messages` | 0.5% 未満 |
| DLQ 流入率 | `dlq_messages / total consumed` | 0.1% 未満 |

## 5.2 補助 SLI（運用目標）
1. Projection lag p99: 60秒以下
2. Outbox publish lag p99: 60秒以下
3. API 5xx error rate（日次）: 0.1% 未満
4. 競合率（`RepositoryConcurrency` / 書き込み要求）: 1.0% 未満

## 5.3 アラートしきい値
1. `Projection lag > 60秒` が 5 分継続で Warning、`> 300秒` で Critical
2. `Outbox 未送信件数 > 1,000` が 10 分継続で Warning
3. `DLQ 流入 > 100件/時` で Critical
4. `API 5xx > 1%` が 5 分継続で Critical

## 6. 影響とトレードオフ

利点:
1. 保持年数・匿名化ルール・SLO が定量化され、監査/運用判断が統一される。
2. RabbitMQ 障害時の再試行導線と行き止まり（DLQ）が明確になる。
3. 目標値ベースで運用改善（容量、再試行回数、処理性能）の優先度を決められる。

トレードオフ:
1. Retry/DLQ/匿名化/保持削除ジョブの運用実装コストが増える。
2. SLO を満たすために Projection/Outbox ワーカーのリソース確保が必要。

## 7. 実装フェーズに持ち込む項目
1. 保持期限削除ジョブ（テーブル別）と監査レポート出力の実装
2. ログ匿名化ミドルウェア（PII 検出ガード含む）の実装
3. RabbitMQ 宣言コード（Exchange/Queue/Binding/TTL/DLX）と契約テスト
4. SLI 計測クエリ・ダッシュボード・アラート定義（Prometheus/Grafana）
