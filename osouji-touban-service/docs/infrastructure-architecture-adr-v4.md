# お掃除当番システム Infrastructure Architecture ADR（v4 / ReadModel Cache & Metrics）

- Status: Accepted
- Date: 2026-03-07
- Extends:
  - `docs/infrastructure-architecture-adr-v2.md`
  - `docs/infrastructure-architecture-adr-v3.md`
- Related:
  - `docs/infrastructure-implementation-plan-v1.md`
  - `docs/application-usecase-design-v1.md`
  - `docs/readmodel-cqrs-design-v1.md`

## 1. 目的

WebAPI の GET リクエストで返す ReadModel を、可能な限り PostgreSQL Projection ではなく Redis から返すためのキャッシュ戦略と、その成否を運用判断できるメトリクス設計を確定する。

対象:

- `GET /api/v1/cleaning-areas`
- `GET /api/v1/cleaning-areas/{areaId}`
- `GET /api/v1/weekly-duty-plans`
- `GET /api/v1/weekly-duty-plans/{planId}`
- ReadRepository 前段の Redis 利用方式
- キャッシュ更新 / 無効化 / 回復
- ReadModel 用 SLI / SLO / Alert 追加

非対象:

- Command 側 Aggregate キャッシュの廃止
- CDN / HTTP reverse proxy 設計
- 認可・マルチテナント境界の追加設計

## 2. 現状確認

既存実装を前提に、以下を設計上の出発点とする。

1. WebAPI の GET は Query Handler 経由で `ICleaningAreaReadRepository` / `IWeeklyDutyPlanReadRepository` を呼ぶ。
2. `PostgresCleaningAreaReadRepository` と `PostgresWeeklyDutyPlanReadRepository` は毎回 PostgreSQL Projection を直接読んでおり、ReadModel 用 Redis キャッシュは未実装である。
3. Redis は `EventStoreCleaningAreaRepository` / `EventStoreWeeklyDutyPlanRepository` で Aggregate Snapshot 復元用にのみ使われている。
4. 現在の可観測性は HTTP、Projection lag、Outbox backlog が中心で、ReadModel cache hit ratio は観測できない。

結論として、GET の高速化要件は「既存 Aggregate キャッシュを流用する」のではなく、「Projection 起点の ReadModel 専用キャッシュを追加する」ことで解く。

## 3. 決定サマリ

| 領域       | 決定                                                                                     |
| ---------- | ---------------------------------------------------------------------------------------- |
| Cache 層   | ReadRepository の前段に Redis-first の ReadModel cache を追加する                        |
| Detail GET | Projector 主導の write-through + GET 時 read-through fallback を採用する                 |
| List GET   | Namespace version 付き query-result cache を採用し、対象クエリを絞って高 hit 率を狙う    |
| 整合性起点 | Event Store ではなく Projection commit 完了を正とし、Projector が cache version を進める |
| 404 応答   | detail GET のみ短TTLの negative cache を採用する                                         |
| 失敗回復   | Redis 更新失敗は retry task に退避し、GET は PostgreSQL fallback を継続する              |
| メトリクス | hit/miss/error/bypass、fill latency、payload size、refresh backlog を追加する            |

## 4. 基本方針

### 4.1. Command Cache と ReadModel Cache を分離する

Aggregate cache は Command 側 Repository の復元高速化に残す。  
GET は ReadModel / Projection に対する最適化であり、Aggregate Snapshot をそのまま返す経路は採用しない。

理由:

1. `application-usecase-design-v1.md` の CQRS 境界と整合する。
2. GET のレスポンス形は Projection 結合結果であり、Aggregate Snapshot とは責務が異なる。
3. Projector の進捗と cache 整合性を同じ監視軸で扱える。

### 4.2. Redis を「DB の代替」ではなく「ReadModel 配信面」として使う

GET の基本経路は以下とする。

1. WebAPI
2. Query Handler
3. Cached ReadRepository
4. Redis hit の場合は即返却
5. miss / deserialize failure / Redis failure の場合のみ PostgreSQL Projection を読む
6. PostgreSQL から組み立てた ReadModel を Redis へ再格納する

この方針により、Redis 障害時にも機能停止せず、通常時は Redis 優先の配信が成立する。

## 5. ReadModel キャッシュ戦略

### 5.1. Detail GET

対象:

- `GET /cleaning-areas/{areaId}`
- `GET /weekly-duty-plans/{planId}`

採用方式:

- versioned entity cache + latest pointer
- Projector commit 後に最新 detail payload を Redis へ反映
- GET 時は pointer -> versioned payload の順で読む
- miss 時は PostgreSQL Projection から再構築し、Redis を補充する

キー例:

- `readmodel:cleaning-area:{areaId}:v{aggregateVersion}`
- `readmodel:cleaning-area:{areaId}:latest`
- `readmodel:weekly-plan:{planId}:v{aggregateVersion}`
- `readmodel:weekly-plan:{planId}:latest`

保持内容:

- API 応答直前に近い JSON payload
- `aggregateVersion`
- `cachedAt`
- `schemaVersion`

理由:

1. detail GET は key cardinality が安定しており、Redis に最も載せやすい。
2. `aggregateVersion` を key に含めることで delete 依存を減らせる。
3. ETag 用 version と整合させやすい。

### 5.2. List GET

対象:

- `GET /cleaning-areas`
- `GET /weekly-duty-plans`

採用方式:

- namespace version + canonical query hash 方式
- Projector が対象 ReadModel の namespace token を進める
- list cache key は `namespaceVersion + querySignature` を含める
- old key の即時削除は不要とし、TTL 失効に任せる

キー例:

- namespace:
  - `readmodel:ns:cleaning-areas:list`
  - `readmodel:ns:weekly-duty-plans:list`
- result:
  - `readmodel:list:cleaning-areas:n{namespaceVersion}:q{queryHash}`
  - `readmodel:list:weekly-duty-plans:n{namespaceVersion}:q{queryHash}`

Admission 制御:

1. cache 対象は `limit <= 50` のみ
2. `cursor` なしの先頭ページを最優先で cache する
3. 2 ページ目以降は `nextCursor` ベースで継続 cache してよいが、最大 3 ページ分までに制限する
4. 極端に高 cardinality な query は `bypass` として PostgreSQL 直読に落とす

補足:

- `cleaning-areas?sort=name`
- `weekly-duty-plans?sort=-weekId`

上記の既定クエリは projector 完了後または定期 warmer で先行生成してよい。  
これにより、実トラフィックの大半を占める先頭ページ GET を Redis に寄せる。

### 5.3. Negative Cache

採用:

- detail GET の `404` のみ短TTLで cache する

キー例:

- `readmodel:cleaning-area:{areaId}:missing`
- `readmodel:weekly-plan:{planId}:missing`

TTL:

- 15 秒

不採用:

- list GET の empty result negative cache

理由:

1. detail の repeated miss は bot / retry / 画面再試行で発生しやすい。
2. list は filter 条件の組み合わせが多く、negative cache の価値より cardinality 増加の方が大きい。

### 5.4. TTL 方針

| 種別                     | TTL     | 備考                                        |
| ------------------------ | ------- | ------------------------------------------- |
| detail versioned payload | 24 時間 | immutable key。TTL には 10% jitter を入れる |
| detail latest pointer    | 24 時間 | projector / fallback の両方で更新           |
| list result              | 10 分   | namespace version 前提。10% jitter          |
| list namespace token     | 24 時間 | projector が継続更新                        |
| negative cache           | 15 秒   | 短期のみ                                    |

## 6. 更新・無効化・回復

### 6.1. 正常系フロー

1. Command が Event Store と Snapshot を更新する。
2. Projector が Projection テーブルを更新する。
3. Projection transaction commit 後に、該当 detail cache を再生成する。
4. 同時に list namespace token を increment する。
5. optional で既定 list query を warm する。

重要:

- ReadModel cache の整合性基準は Event Store commit ではなく Projection commit とする。
- これにより、GET が参照する Postgres Projection と Redis payload の意味を揃える。

### 6.2. 失敗時

Redis 更新に失敗しても Projection commit はロールバックしない。  
代わりに `readmodel_cache_refresh_tasks` を追加し、以下を保存する。

- `task_id UUID`
- `cache_scope TEXT` (`detail` / `list_namespace` / `list_warm`)
- `resource_type TEXT`
- `resource_id TEXT NULL`
- `namespace_name TEXT NULL`
- `payload_json JSONB NULL`
- `retry_count INT`
- `next_retry_at TIMESTAMPTZ`
- `last_error TEXT NULL`
- `resolved_at TIMESTAMPTZ NULL`

再試行ポリシー:

- 1m, 5m, 15m, 1h, 6h
- 上限超過後も task は残し、運用アラート対象にする

GET 時の fallback:

1. Redis error
2. cache miss
3. schemaVersion mismatch
4. deserialize failure

上記はいずれも PostgreSQL Projection 読み出しへフォールバックしてよい。  
このとき cache refill は best-effort とする。

### 6.3. 不採用事項

不採用:

- stale-while-revalidate で古い WeeklyDutyPlan detail を返す
- Event Store 書き込み直後に Query cache を更新する

理由:

1. `Published` / `Closed` 直後に stale detail を返すと業務上の誤認を起こしやすい。
2. Query の真実源は Projection であり、Projection lag とは別の古さを増やしたくない。

## 7. 実装境界

### 7.1. 追加コンポーネント

- `IReadModelCache`
- `RedisReadModelCache`
- `CachedCleaningAreaReadRepository`
- `CachedWeeklyDutyPlanReadRepository`
- `ReadModelCacheRefreshWorker`

DI 方針:

- `Postgres*ReadRepository` は origin repository として残す
- WebAPI / Application から見える ReadRepository は cached decorator を登録する

### 7.2. Projector 変更点

`MainProjector` は Projection 更新後に、最低限以下を行う。

1. `CleaningArea` 更新時:
   - detail cache refresh
   - `cleaning-areas:list` namespace bump

2. `WeeklyDutyPlan` 更新時:
   - detail cache refresh
   - `weekly-duty-plans:list` namespace bump

3. area member / spot / status / revision 変更を含むため、list namespace は細粒度 invalidate ではなく collection 単位で進める

### 7.3. 設定追加

`Infrastructure:Redis` 配下に以下を追加する。

```json
{
  "ReadModelDetailTtlSeconds": 86400,
  "ReadModelListTtlSeconds": 600,
  "ReadModelNegativeTtlSeconds": 15,
  "ReadModelWarmEnabled": true,
  "ReadModelWarmTopPages": 3,
  "ReadModelCacheMaxListLimit": 50
}
```

既存の `DefaultTtlSeconds` は Aggregate cache 用として残してよい。

## 8. メトリクス / SLI / Alert

### 8.1. 追加メトリクス

1. `osouji_readmodel_cache_requests_total{resource,operation,result}`
   - `resource`: `cleaning_area` / `weekly_plan`
   - `operation`: `detail` / `list`
   - `result`: `hit` / `miss` / `fill` / `error` / `bypass` / `negative_hit`

2. `osouji_readmodel_cache_fill_duration_seconds{resource,operation}`
   - PostgreSQL fallback から Redis 再格納完了までの所要時間

3. `osouji_readmodel_cache_payload_bytes{resource,operation}`
   - cache payload サイズ分布

4. `osouji_readmodel_cache_refresh_failures_total{resource,scope}`
   - projector / worker による Redis refresh 失敗回数

5. `osouji_readmodel_cache_refresh_backlog{scope}`
   - 未解決 refresh task 数

6. `osouji_readmodel_cache_namespace_version{resource}`
   - 現在の namespace token。障害調査用途

7. 継続利用:
   - `osouji_http_request_duration_seconds`
   - `osouji_http_requests_total`
   - `osouji_projection_checkpoint_lag_seconds`

### 8.2. SLI / SLO

| SLI                                         | 定義                                  | SLO        |
| ------------------------------------------- | ------------------------------------- | ---------- |
| Detail cache hit ratio                      | `hit / (hit + miss + error)`          | 95% 以上   |
| List cache hit ratio（cache対象クエリのみ） | `hit / (hit + miss + error)`          | 85% 以上   |
| Cache bypass ratio                          | `bypass / list requests`              | 10% 未満   |
| Cache refresh error ratio                   | `refresh_failures / refresh attempts` | 1% 未満    |
| Query API p95 latency                       | 既存定義を継続                        | 250ms 以下 |

解釈:

- list hit ratio は対象クエリを絞る前提で測る。全 query を分母に入れない。
- Detail hit ratio を最重要 KGI とし、まず `/{id}` 系を Redis 優先に寄せる。

### 8.3. Alert

追加 alert:

1. `ReadModelDetailCacheHitRatioLow`
   - 15 分平均で 95% 未満なら Warning

2. `ReadModelListCacheHitRatioLow`
   - 15 分平均で 85% 未満なら Warning

3. `ReadModelCacheRefreshBacklogHigh`
   - `refresh_backlog > 100` が 10 分継続で Warning

4. `ReadModelCacheRefreshErrorRateHigh`
   - 5 分平均で 1% 超なら Warning、5% 超なら Critical

5. `ProjectionLagAndCacheMissSpike`
   - `projection lag > 60s` かつ `miss ratio` 急増時は Critical

理由:

- cache 単独ではなく projection lag と組み合わせて見ることで、原因が Redis 側か projector 側かを切り分けやすい。

## 9. 影響

利点:

1. GET の主経路を Redis に移し、ReadModel 応答を安定して低レイテンシ化できる。
2. Projection commit 起点の整合性で、Query 側の説明が単純になる。
3. cache hit 率を SLO として扱えるため、「Redis を導入したが効いていない」を検知できる。

トレードオフ:

1. Projector と ReadRepository の双方に cache ロジックが入り、実装面は複雑になる。
2. list cache は cardinality 制御を誤ると Redis メモリを圧迫する。
3. refresh task / warmer の運用対象が追加される。

## 10. 実装フェーズへ持ち込む項目

1. `Postgres*ReadRepository` を origin とした cached decorator 実装
2. `MainProjector` からの detail refresh / namespace bump 実装
3. `readmodel_cache_refresh_tasks` DDL と retry worker 実装
4. `OsoujiTelemetry` への ReadModel cache metrics 追加
5. `docs/prometheus-alert-rules-draft-v1.yaml` の v2 化
6. Integration test 追加

- detail hit / miss / negative cache
- list namespace bump 後の stale key 非参照
- Redis 障害時の PostgreSQL fallback
