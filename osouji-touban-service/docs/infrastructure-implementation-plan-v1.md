# お掃除当番システム Infrastructure 実装計画書（v1）

- Date: 2026-03-07
- Source ADR:
  - `docs/infrastructure-architecture-adr-v1.md`
  - `docs/infrastructure-architecture-adr-v2.md`
  - `docs/infrastructure-architecture-adr-v3.md`
  - `docs/readmodel-write-visibility-design-v1.md`
  - `docs/infrastructure-ddl-draft-v1.sql`
  - `docs/rabbitmq-topology-draft-v1.yaml`
  - `docs/prometheus-alert-rules-draft-v1.yaml`

## 1. 目的

本書は、ADRで確定したInfrastructure設計を実装へ落とし込むための実行計画である。  
新しいチャットセッションでも再利用できるよう、技術選定・設定連携・実装順序を固定する。

## 2. 最終到達像（実装スコープ）

1. Stub Repository を EventSourcing 実装へ置き換える。
2. PostgreSQL 上に Event Store / Snapshot / Outbox / Projection を実装する。
3. Redis cache-aside と cache invalidation recovery を実装する。
4. Outbox -> RabbitMQ publish、Consumer idempotency、Retry/DLQ を実装する。
5. OpenTelemetry + Prometheus で SLI/SLO 観測を実装する。
6. 監査保持・PII匿名化ポリシーを運用可能な形で実装する。

## 3. プロジェクト構成変更

### 3.1. 新規プロジェクト

- `src/OsoujiSystem.Infrastructure/OsoujiSystem.Infrastructure.csproj`
  - Domain/Application の interface 実装（Repository, Transaction, Dispatcher）
  - DBアクセス、Outbox、Projector、Retention、CacheInvalidation

### 3.2. 参照関係

- `WebApi -> Application -> Domain`
- `WebApi -> Infrastructure -> (Application, Domain)`
- `Infrastructure` は `Domain` の契約を実装し、`Application` の `IApplicationTransaction` 等も実装する。
- `AppHost` は Aspire 13.1.2 の `WithReference(...)` による接続情報注入を前提にする。

## 4. NuGet選定（確定）

確認日: 2026-03-05（NuGet Gallery 最新版）

| プロジェクト   | Package                                           | 固定バージョン  | 用途                              |
| -------------- | ------------------------------------------------- | --------------- | --------------------------------- |
| Infrastructure | `Npgsql`                                          | `10.0.1`        | PostgreSQL 接続・トランザクション |
| Infrastructure | `Dapper`                                          | `2.1.66`        | SQLベースの軽量マッピング         |
| Infrastructure | `Microsoft.Extensions.Caching.StackExchangeRedis` | `10.0.3`        | Redis cache-aside                 |
| Infrastructure | `StackExchange.Redis`                             | `2.11.8`        | Redis操作（キー削除/バッチ制御）  |
| Infrastructure | `RabbitMQ.Client`                                 | `7.2.1`         | Outbox publish / consumer         |
| Infrastructure | `OpenTelemetry.Extensions.Hosting`                | `1.15.0`        | OTelホスト統合                    |
| Infrastructure | `OpenTelemetry.Instrumentation.AspNetCore`        | `1.15.0`        | API計測                           |
| Infrastructure | `OpenTelemetry.Instrumentation.Http`              | `1.15.0`        | 外部通信計測                      |
| Infrastructure | `OpenTelemetry.Instrumentation.Runtime`           | `1.15.0`        | Runtime計測                       |
| Infrastructure | `OpenTelemetry.Exporter.Prometheus.AspNetCore`    | `1.15.0-beta.1` | Prometheus scrape                 |
| Infrastructure | `OpenTelemetry.Exporter.OpenTelemetryProtocol`    | `1.15.0`        | OTLP出力                          |
| Infrastructure | `Serilog.AspNetCore`                              | `10.0.0`        | 構造化ログ                        |
| Infrastructure | `Serilog.Enrichers.Span`                          | `3.1.0`         | `trace_id` / `span_id` 付与       |
| Infrastructure | `AspNetCore.HealthChecks.NpgSql`                  | `9.0.0`         | DBヘルスチェック                  |
| Infrastructure | `AspNetCore.HealthChecks.Redis`                   | `9.0.0`         | Redisヘルスチェック               |
| Infrastructure | `AspNetCore.HealthChecks.Rabbitmq`                | `9.0.0`         | RabbitMQヘルスチェック            |
| Infrastructure | `Polly`                                           | `8.6.6`         | publish/retry の耐障害制御        |

補足:

1. `OpenTelemetry.Exporter.Prometheus.AspNetCore` は現時点で最新が prerelease（`beta`）である。
2. 依存バージョンは `Directory.Packages.props` で一元管理する。

## 5. 設定連携方式（2026-03-07 更新）

方針:

1. インフラ接続先は `Infrastructure:*` ではなく `ConnectionStrings` を正とする。
2. 開発環境では Aspire AppHost の `WithReference(...)` で注入される接続情報をそのまま利用する。
3. 本番環境では Helm から同じ接続名で `ConnectionStrings__*` を注入する。
4. 旧 `INFRASTRUCTURE__POSTGRES__CONNECTIONSTRING`、`INFRASTRUCTURE__REDIS__CONNECTIONSTRING`、`INFRASTRUCTURE__RABBITMQ__*` はサポート対象外とする。

### 5.1. appsettings 構造

`src/OsoujiSystem.WebApi/appsettings*.json` に以下を追加する。

```json
{
  "ConnectionStrings": {
    "osouji-db": "",
    "osouji-redis": "",
    "osouji-rabbitmq": ""
  },
  "Infrastructure": {
    "PersistenceMode": "Stub",
    "Postgres": {
      "Schema": "public",
      "CommandTimeoutSeconds": 30
    },
    "Redis": {
      "DefaultTtlSeconds": 300
    },
    "RabbitMq": {},
    "Outbox": {
      "BatchSize": 100,
      "PollIntervalMs": 1000
    },
    "Projection": {
      "BatchSize": 200,
      "PollIntervalMs": 1000
    },
    "Retention": {
      "DailyRunJst": "03:30",
      "EventStoreYears": 7,
      "OutboxPublishedDays": 180,
      "OutboxFailedDays": 365,
      "DlqDays": 30,
      "LogDays": 180,
      "TraceDays": 14,
      "MetricsMonths": 13
    },
    "Pii": {
      "TenantSaltSecretName": "osouji-pii-salt",
      "MaskEmployeeNumber": true
    }
  }
}
```

接続名は以下で固定する。

1. PostgreSQL: `osouji-db`
2. Redis: `osouji-redis`
3. RabbitMQ: `osouji-rabbitmq`

### 5.2. バインド/検証

1. `InfrastructureOptions` 配下に `PostgresOptions`, `RedisOptions`, `RabbitMqOptions` などを定義。
2. `services.AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` を標準化。
3. 接続文字列は `IConfiguration.GetConnectionString("<name>")` から取得する。
4. RabbitMQ は `amqp://...` の単一 connection string を使い、分解済み `Host/Port/UserName/Password` 設定は持たない。
5. PII salt のような接続情報以外の秘密値は従来どおり個別環境変数で扱う。

### 5.3. Aspire / Compose / Helm 連携

1. `src/OsoujiSystem.AppHost/AppHost.cs` では `WithReference(db)`, `WithReference(redis)`, `WithReference(rabbitMq)` のみを使い、手動 `WithEnvironment(...)` ブリッジは置かない。
2. `docker-compose.yml` では以下を注入する。
   - `ConnectionStrings__osouji-db`
   - `ConnectionStrings__osouji-redis`
   - `ConnectionStrings__osouji-rabbitmq`
3. Helm でも同じ 3 キーを注入し、開発環境と本番環境の設定モデルを一致させる。
4. RabbitMQ connection string は `amqp://user:password@host:5672/` 形式を使う。

### 5.4. Feature Toggle

- `Infrastructure:PersistenceMode`
  - `Stub`: 現行の StubRepository を使用
  - `EventStore`: Infrastructure 実装を使用
- 切替は DI 登録で制御し、段階移行を可能にする。

## 6. 実装フェーズ

### 6.1. Phase 0: 土台作成

1. `OsoujiSystem.Infrastructure` プロジェクト作成
2. `AddOsoujiInfrastructure(IConfiguration)` 拡張メソッド追加
3. `WebApi/Program.cs` で `AddOsoujiApplication()` 後に `AddOsoujiInfrastructure()` を呼ぶ
4. `PersistenceMode` で Stub/実装を切替
5. `ConnectionStrings` ベースの接続解決を標準化

完了条件:

- 既存テストが壊れずに起動する。

### 6.2. Phase 1: DBスキーマ導入

1. `docs/infrastructure-ddl-draft-v1.sql` を実行可能な migration として配置
2. 起動時マイグレーション（Devのみ）またはCIジョブを準備
3. 初期データ（`projection_checkpoints`）の seed

完了条件:

- ローカル Postgres で全テーブル作成確認。

### 6.3. Phase 2: EventStore Repository 実装

1. `ICleaningAreaRepository` 実装
2. `IWeeklyDutyPlanRepository` 実装
3. `IAssignmentHistoryRepository` 実装（`projection_user_weekly_workloads` 集計）
4. `IApplicationTransaction` 実装（`NpgsqlTransaction`）
5. `RepositoryConcurrencyException` / `RepositoryDuplicateException` 変換

完了条件:

- Repository contract test が全通過。

### 6.4. Phase 3: Projection / Snapshot / Cache

1. Snapshot保存（50イベントごと）
2. Projector worker（`global_position` 順、checkpoint更新）
3. Redis cache-aside
4. `cache_invalidation_tasks` 再試行 worker 実装

完了条件:

- `GenerateWeeklyPlan` 後に Projection とキャッシュが更新される。

### 6.5. Phase 4: Outbox / RabbitMQ

1. Outbox writer（DB transaction 同梱）
2. Outbox publisher worker（batch publish）
3. RabbitMQ topology 宣言（`docs/rabbitmq-topology-draft-v1.yaml`準拠）
4. Consumer idempotency（`consumer_processed_events`）
5. Retry/DLQ（`x-retry-count`、1m/5m/30m、max 5）
6. RabbitMQ 接続は `ConnectionStrings:osouji-rabbitmq` の URI 解決に統一する

完了条件:

- 故意失敗時に Retry -> DLQ が期待どおりに遷移する。

### 6.6. Phase 5: 可観測性 / SLO

1. OpenTelemetry 計測の導入
2. Prometheus メトリクス公開
3. `docs/prometheus-alert-rules-draft-v1.yaml` を監視基盤へ適用
4. SLI ダッシュボード作成（API p95, projection lag, outbox lag, DLQ流入）
5. ReadModel visibility wait / checkpoint gap / invalidation pending の可視化

完了条件:

- v3で定義した主要SLIが可視化される。

### 6.7. Phase 6: データ保持 / PII

1. 保持期限削除ジョブ（テーブル別）
2. `data_retention_purge_reports` 記録
3. ログ匿名化（HMAC-SHA256）実装
4. `EmployeeNumber` マスキングポリシー適用

完了条件:

- 実データでPIIがログに出ないことをテストで保証。

## 7. 実装順序（推奨）

1. Phase 0 -> 1 -> 2（まず永続化置換）
2. 次に Phase 3（読み取り安定化）
3. 次に Phase 4（非同期連携）
4. 最後に Phase 5 -> 6（運用品質・ガバナンス）

## 8. テスト戦略

### 8.1. 自動テスト

1. Repository Contract Test（必須）
2. Projection Idempotency Test（同一イベント再適用）
3. Outbox Publish Test（重複送信なし）
4. Retry/DLQ E2E Test（失敗注入）
5. PII Logging Test（禁止フィールドのログ出力検知）

### 8.2. 負荷/運用試験

1. API p95 試験（command/query）
2. Projection lag 試験（イベントバースト投入）
3. 障害復旧試験（RabbitMQ停止、Redis停止、DB failover）

## 9. 受け入れ基準（Definition of Done）

1. Stub を使わず `EventStore` モードで主要ユースケースが動作する。
2. `Generate/Rebalance/Publish/Close` のイベントが永続化される。
3. Projection と Redis が整合する（revisionベース）。
4. Retry/DLQ と idempotency が機能する。
5. 監視で v3 SLI を観測できる。
6. PII 匿名化と保持削除ジョブが稼働する。
7. 開発環境では Aspire AppHost の `WithReference(...)` だけで Postgres / Redis / RabbitMQ に接続できる。

## 10. 実装メモ（2026-03-07 時点）

1. `AppHost` の `INFRASTRUCTURE__*` 手動ブリッジは削除済み。
2. `ServiceCollectionExtensions` は `ConnectionStrings:osouji-db` / `osouji-redis` / `osouji-rabbitmq` を正として解決する実装へ更新済み。
3. RabbitMQ は `ConnectionFactory.Uri` を使う単一 URI モデルへ移行済み。
4. `docker-compose.yml` と WebApi テスト fixture は新しい `ConnectionStrings__*` キーに更新済み。
5. 2026-03-07 に `dotnet test -maxcpucount:1` 全件成功、`dotnet build src/OsoujiSystem.AppHost/OsoujiSystem.AppHost.csproj -maxcpucount:1` 成功、`dotnet run --project src/OsoujiSystem.AppHost/OsoujiSystem.AppHost.csproj` で Aspire 13.1.2 の起動ログを確認済み。

## 11. 実装チケット分割（初期バックログ）

1. INFRA-01: Infrastructure project + DI toggle
2. INFRA-02: DDL migration導入
3. INFRA-03: EventStore repositories
4. INFRA-04: Application transaction (Npgsql)
5. INFRA-05: Projector worker + checkpoints
6. INFRA-06: Redis cache + invalidation recovery
7. INFRA-07: Outbox writer/publisher
8. INFRA-08: RabbitMQ topology + retry/dlq
9. INFRA-09: OTel metrics/traces + alerts
10. INFRA-10: Retention + PII anonymization
11. RMV-01: Projection visibility options
12. RMV-02: Event write metadata with global position
13. RMV-03: ReadModel consistency context accessor
14. RMV-04: Commit後 token 発行
15. RMV-05: visibility checkpoint / invalidation task migrations
16. RMV-06: visibility checkpoint repository / advance service
17. RMV-07: ReadModel invalidation recovery worker
18. RMV-08: Projector visibility integration
19. RMV-09: ReadModel visibility waiter
20. RMV-10: WebApi mutation visibility helper
21. RMV-11: create / update endpoint `202 Accepted` 対応
22. RMV-12: delete endpoint visibility wait 対応
23. RMV-13: visibility telemetry / alerts
