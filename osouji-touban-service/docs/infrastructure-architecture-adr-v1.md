# お掃除当番システム Infrastructure Architecture ADR（v1）

- Status: Accepted
- Date: 2026-03-05
- Decision Makers: Core/Platform Team
- Related:
  - `docs/core-domain-design-v3.md`
  - `docs/core-domain-repository-abstraction-v1.md`
  - `docs/application-usecase-design-v1.md`

## 1. 背景
Domain/Application 設計では、集約・ユースケース・Repository 契約までは確定したが、Infrastructure の具体サービスは未確定である。  
特に「履歴を監査可能に保持する」要求に対して、永続化方式・キャッシュ方針・イベント連携基盤が設計書として未定義である。

## 2. 設計書として解決すべき課題
本 ADR で解く課題は、実装不足ではなく設計未決定事項である。

1. Repository 契約（楽観排他・履歴取得）を満たす保存モデルが未定義。
2. 監査要件を満たす履歴保持方式（EventSourcing採用可否）が未定義。
3. Read 性能と再計算頻度を両立するキャッシュ戦略が未定義。
4. 同期 In-Process Event から将来の非同期連携へ移行可能な基盤が未定義。
5. 運用要件（可観測性、バックアップ/復旧）の標準構成が未定義。

## 3. 決定事項（採用サービス）

| 領域 | 採用 | 目的 |
|---|---|---|
| 永続化（イベントストア） | PostgreSQL 17（Managed PG 推奨） | EventSourcing の正本保持、楽観排他、トランザクション |
| ReadModel | PostgreSQL（Projection テーブル） | API 応答向け参照最適化 |
| キャッシュ | Redis（Managed Redis / Valkey 互換） | ReadModel キャッシュ、冪等キー、短期ロック |
| 非同期連携 | PostgreSQL Outbox + RabbitMQ | 外部通知/連携の at-least-once 配送 |
| 可観測性 | OpenTelemetry + Prometheus + Grafana + Loki | メトリクス/トレース/ログ統合監視 |
| バックアップ | PostgreSQL PITR + 日次スナップショット | 監査要件を満たす復旧性 |

## 4. 主要判断 1: EventSourcing を採用し、保存先は PostgreSQL とする

### 4.1 採用理由
1. 変更履歴を完全に残せる（監査・再演算・説明責任に強い）。
2. `expectedVersion` を SQL 条件で確実に実装でき、Repository 契約と整合する。
3. `TransferUserToArea` など複数集約更新を単一 DB トランザクションで扱える。
4. 運用コストと学習コストのバランスが良く、初期導入リスクが低い。

### 4.2 比較
| 候補 | 利点 | 懸念 | 不採用理由 |
|---|---|---|---|
| PostgreSQL + Event テーブル（採用） | ACID、SQL、運用知見が豊富、コスト予測しやすい | 専用 EventStore 機能は自前設計が必要 | 総合バランスが最良 |
| EventStoreDB | EventSourcing への最適化、ストリーム操作が強い | 運用スキル・監視設計が追加で必要 | 現段階では運用負荷が高い |
| DynamoDB/Cosmos + Streams | 水平スケール、マネージド性 | 複数集約一貫性、ローカル検証、学習コスト | 現行ユースケースに対し過剰 |

### 4.3 設計方針
- 1 Aggregate = 1 Stream (`stream_id`, `stream_type`)。
- `UNIQUE(stream_id, version)` で楽観排他を保証。
- `snapshot` を一定間隔（目安: 50イベント）で保存。
- Read 用には Projection テーブルを分離し、API は Projection を参照する。

## 5. 主要判断 2: キャッシュは Redis の Cache-Aside を採用する

### 5.1 採用理由
1. 週次計画参照のレイテンシを安定化できる。
2. ReadModel と独立して TTL/無効化を設計できる。
3. 将来の冪等制御（Command 重複防止）にも同じ基盤を再利用できる。

### 5.2 比較
| 候補 | 利点 | 懸念 | 不採用理由 |
|---|---|---|---|
| Redis（採用） | 分散キャッシュ標準、ロック/冪等キーに流用可 | 追加運用対象が増える | 性能・機能の両立で最適 |
| In-Memory Cache | 実装が容易 | Pod 間不整合、スケール時に不安定 | K8s 水平スケールと不整合 |
| キャッシュなし | 構成が単純 | 再計算/参照負荷が DB に集中 | 運用余裕が小さい |

### 5.3 キャッシュ戦略
- パターン: Cache-Aside。
- キー例: `weekly-plan:{areaId}:{weekId}`。
- TTL 初期値: 5分（負荷観測で調整）。
- 無効化: `WeeklyPlanGenerated/Recalculated/Published/Closed` 投影完了時に関連キー削除。
- 冪等キー: `idempotency:{commandId}` を 24 時間保持。

## 6. 主要判断 3: 非同期連携は Outbox + RabbitMQ を採用する

### 6.1 採用理由
1. 現在の同期 In-Process イベントを維持しつつ、外部連携のみ段階的に非同期化できる。
2. Outbox により DB 更新とイベント公開の不整合を回避できる。
3. 通知系ワークロード規模では RabbitMQ の運用・コスト効率が高い。

### 6.2 比較
| 候補 | 利点 | 懸念 | 不採用理由 |
|---|---|---|---|
| Outbox + RabbitMQ（採用） | 実装容易、低レイテンシ、運用実績 | 厳密順序や長期保持は設計が必要 | 現状要件に適合 |
| Outbox + Kafka | 大規模ストリーミングに強い | 運用/構成が重い | 現時点では過剰 |
| 同期 In-Process のみ | 単純 | サービス分離時に破綻、再送制御なし | 将来拡張性が不足 |

## 7. 運用設計の標準
1. 可観測性
- 全リクエストに `trace_id` を付与し、API/UseCase/Repository を横断トレース。
- SLI: API p95 latency、Outbox 遅延、Projection 遅延、DB 競合率。

2. バックアップ/復旧
- PostgreSQL: PITR（WAL 保管） + 日次フルバックアップ。
- 目標: RPO 15分、RTO 60分。
- 復旧訓練を月1回実施。

3. セキュリティ
- Secrets は Kubernetes Secret + External Secrets（クラウド Secret Manager 連携）を標準とする。
- DB/RabbitMQ/Redis 接続は TLS を必須化。

## 8. この決定の影響

### 利点
1. 監査可能性と説明可能性が設計時点で担保される。
2. Repository 契約と運用基盤の整合が取れる。
3. 将来の通知・外部連携に対し、段階的拡張が可能になる。

### トレードオフ
1. Projection/Outbox/キャッシュ無効化の設計・運用が追加される。
2. イベントスキーマ進化（バージョニング）の運用規律が必要。

## 9. 実装フェーズに持ち込む設計タスク（次ADRで詳細化）
1. `event_store` / `snapshot` / `outbox` / `projection` の論理スキーマ ADR。
2. Projection 更新順序と再構築（リプレイ）手順 ADR。
3. キャッシュ無効化失敗時の回復戦略 ADR。
4. 監査ログの保持期間・匿名化ポリシー ADR。
