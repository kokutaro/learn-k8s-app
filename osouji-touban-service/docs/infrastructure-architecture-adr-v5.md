# お掃除当番システム Infrastructure Architecture ADR（v5 / ReadModel Cache Implementation Alignment）

- Status: Accepted
- Date: 2026-03-07
- Extends:
  - `docs/infrastructure-architecture-adr-v4.md`
- Related:
  - `docs/infrastructure-implementation-plan-v1.md`
  - `docs/application-usecase-design-v1.md`

## 1. 目的
`docs/infrastructure-architecture-adr-v4.md` に基づく ReadModel cache 実装と、その検証結果を反映し、実装済みアーキテクチャを ADR と一致させる。

本 ADR は「v4 の未実装項目一覧」ではなく、実装・テストを通じて確定した設計差分を正式決定に置き換えるものである。

## 2. 実装確認で判明した差分
v4 と実装の差分は以下である。

1. detail GET は `projector 主導の write-through` ではなく、`projector による pointer invalidate + GET 時 read-through refill` になっている。
2. detail GET の negative cache は採用していない。
3. `readmodel_cache_refresh_tasks` と `ReadModelCacheRefreshWorker` は導入していない。
4. `refresh_backlog` / `namespace_version` gauge は導入していない。
5. list warmer は導入していない。

差分のうち 1 と 2 は、実装上の簡略化ではなく、統合テストで確認された整合性要件を踏まえた設計変更である。  
差分のうち 3-5 は、現段階では追加複雑性に対して効果が不足すると判断し、明示的に採用しない。

## 3. 決定サマリ

| 領域 | 決定 |
|---|---|
| Detail cache 更新方式 | `pointer invalidate + read-through refill` を正式採用する |
| Negative cache | detail GET でも不採用とする |
| Projector 責務 | detail payload 再生成ではなく、latest pointer 削除と list namespace bump に限定する |
| 回復戦略 | Redis 障害時は即座に PostgreSQL fallback し、非同期 retry task は持たない |
| メトリクス | 実装済みの request/fill/payload/refresh_failures に限定する |
| 将来拡張 | refresh task / warmer / backlog gauge は将来の容量圧迫時に再検討する |

## 4. 変更理由

## 4.1 Projector write-through を採用しなかった理由
detail payload は `Postgres*ReadRepository` 内で Projection 結合結果として構築される。  
Projector 側で同じ payload を再生成すると、Projection 更新ロジックと Query 組み立てロジックが二重化する。

問題点:
1. `projection_cleaning_areas` と `projection_area_members` と `projection_cleaning_area_spots` の組み立て規則が二重定義になる。
2. `projection_weekly_plans` と assignment/offduty の detail も同様に二重化する。
3. Query 表現変更時に projector と read repository の双方を同期修正する必要がある。

したがって、Projector は Query cache の fully materialized payload を作らず、以下に責務を絞る。

1. Projection を commit する
2. detail latest pointer を削除する
3. list namespace version を進める

detail payload の再生成は、次回 GET miss 時に `Cached*ReadRepository` が origin repository を通じて行う。

## 4.2 Negative cache をやめた理由
統合テストで、新規作成直後の最初の GET が Projection 未反映タイミングに当たると、一時的 404 が発生した。  
この 404 を negative cache すると、Projection 反映後もしばらく 404 を返し続ける。

これは以下の要件と衝突する。

1. 作成直後の GET で新規 resource を確認したい
2. 更新直後の再取得で最新 state を確認したい
3. Projection lag は許容しても、cache がその lag を延長してはいけない

そのため、detail GET の negative cache は採用しない。

## 5. 正式な ReadModel cache フロー

## 5.1 Detail GET
1. `latest` pointer を Redis から読む
2. pointer があれば `v{version}` payload を読む
3. hit なら返す
4. miss / Redis error なら PostgreSQL Projection を読む
5. Projection で見つかれば `v{version}` payload と `latest` pointer を Redis に格納する
6. Projection で見つからなければそのまま `404` を返す

キー:
- `readmodel:cleaning-area:{areaId}:latest`
- `readmodel:cleaning-area:{areaId}:v{aggregateVersion}`
- `readmodel:weekly-plan:{planId}:latest`
- `readmodel:weekly-plan:{planId}:v{aggregateVersion}`

## 5.2 List GET
1. `readmodel:ns:*:list` の namespace version を取得
2. canonicalized query から hash を作る
3. `n{namespaceVersion}:q{queryHash}` を参照する
4. miss / Redis error 時は PostgreSQL Projection を読み、Redis に再格納する

補足:
- cache admission は `limit <= ReadModelCacheMaxListLimit`
- これを超える query は `bypass` とする

## 5.3 Projector
Projection commit 後に以下のみ行う。

1. `CleaningArea` 変更:
- `readmodel:cleaning-area:{areaId}:latest` を削除
- `readmodel:ns:cleaning-areas:list` を increment

2. `WeeklyDutyPlan` 変更:
- `readmodel:weekly-plan:{planId}:latest` を削除
- `readmodel:ns:weekly-duty-plans:list` を increment

versioned payload の旧キー削除は不要とし、TTL 失効に任せる。

## 6. 障害時方針
Redis 操作失敗時は以下を標準動作とする。

1. GET:
- PostgreSQL Projection へ fallback
- cache refill は best-effort

2. Projector:
- cache pointer 削除 / namespace bump の失敗を warning log と metric へ記録
- Projection commit 自体は成功扱いとする

採用しない:
- retry task テーブル
- refresh worker
- projector からの再試行キュー投入

理由:
1. GET fallback だけでユーザー影響を局所化できる
2. 現段階では Redis 障害のために追加状態管理テーブルを持つ複雑性が高い
3. namespace bump 失敗時も TTL と miss refill により徐々に収束する

## 7. メトリクス方針の更新
正式採用するメトリクス:

1. `osouji_readmodel_cache_requests_total{resource,operation,result}`
- `hit` / `miss` / `fill` / `error` / `bypass`

2. `osouji_readmodel_cache_fill_duration_seconds{resource,operation}`

3. `osouji_readmodel_cache_payload_bytes{resource,operation}`

4. `osouji_readmodel_cache_refresh_failures_total{resource,scope}`

継続利用:
- `osouji_projection_checkpoint_lag_seconds`
- `osouji_http_request_duration_seconds`
- `osouji_http_requests_total`

採用しない:
- `negative_hit`
- `refresh_backlog`
- `namespace_version`

理由:
1. 実装していない制御面に対するメトリクスは false signal になる
2. まずは hit ratio と fill latency と projection lag で十分に運用判断できる

## 8. 影響

利点:
1. Query cache 実装を read repository に集約でき、保守責務が明確になる。
2. Projection lag を cache が延長しない。
3. 実装複雑性を抑えつつ、GET の Redis-first 経路は維持できる。

トレードオフ:
1. detail write-through を行わないため、更新直後の最初の GET は miss になりやすい。
2. Redis 障害時の自動修復は request traffic に依存する。
3. cache refresh backlog のような運用可視化はまだ持たない。

## 9. 今後の再検討条件
以下のいずれかを満たした場合のみ、v4 で想定した強化案を再検討する。

1. detail cache miss ratio が継続して SLO を下回る
2. list query の namespace invalidation で Redis メモリ効率が悪化する
3. Redis 障害後の回復が request-driven では遅すぎる
4. Query 経路の payload 生成コストが projector 側での先行生成に見合う

再検討候補:
- projector write-through
- negative cache の条件付き再導入
- refresh task / worker
- list warmer
