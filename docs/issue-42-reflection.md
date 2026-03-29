# Issue #42 振り返り: 既存アサイン済みメンバー displayName バックフィル

## 1. 実装の背景と目的

Issue #42 では、Issue #40 で ReadModel 取得時の JOIN 改善を入れた後も、既存のアサイン済みメンバーに displayName 欠損が残り、UI 上で社員番号フォールバック表示が継続していた問題を扱った。

目的は以下の 3 点。

- 既存データに対して安全に displayName を補完する
- 再実行しても不整合を起こさない idempotent なバックフィルを提供する
- 実行結果を定量的に追跡できるよう、可観測性を担保する

## 2. 意思決定と実装方針

今回の設計上の主要な意思決定は次のとおり。

- 解決優先順位を明示
  - 第一候補: user_id 一致
  - 第二候補: employee_number 一致
- employee_number で複数候補が存在する曖昧ケースは更新しない
  - 誤更新リスクを避けるため、補完可能性より正確性を優先
- display_name は空文字・空白を正規化して扱う
- UPSERT 条件を限定し、既存の有効 display_name を劣化上書きしない
- 実行ごとにメトリクスを記録し、欠損率の前後比較を可能にする

## 3. 実装内容とポイント

### 3.1 Migration 0010 の追加

対象: src/OsoujiSystem.Infrastructure/Migrations/0010_backfill_area_member_display_name.sql

実装ポイント:

- active member を対象にバックフィル候補を作成
- user_id で displayName を解決できる場合は最優先で採用
- user_id で解決不能な場合のみ employee_number で補完候補を探索
- employee_number 側で候補が複数になる場合は曖昧扱いとして不更新
- projection_user_directory への INSERT/ON CONFLICT UPDATE により idempotent に適用
- UPDATE 条件で既存 display_name が空の場合のみに限定し、既存有効値を保護

### 3.2 可観測性の追加

Migration 内で以下を実施。

- 実行結果テーブル migration_area_member_display_name_backfill_runs を作成
- 指標を run ごとに記録
  - target_member_count
  - updated_member_count
  - unresolved_member_count
  - ambiguous_match_count
  - missing_rate_before
  - missing_rate_after
- 最終的な実行結果を NOTICE として出力し、運用時に即時確認可能化

### 3.3 テストと docs 更新

入力情報ベースで、以下を実施済み。

- API 統合テストを追加し、補完後の displayName 取得挙動を検証
- docs を更新し、バックフィル方針と運用観点を反映

## 4. テスト結果

- dotnet test: 216/216 success

本変更により、機能追加後の回帰が発生していないことを確認した。

## 5. コードレビュー指摘と対応

レビュー結果:

- PR ブロッカー: なし
- Medium 指摘: migration 内の通常 CREATE INDEX による運用ロックリスク

対応状況:

- 現時点では CREATE INDEX IF NOT EXISTS を採用し、重複作成は防止
- ただし通常 CREATE INDEX はテーブル書き込みをブロックしうるため、運用上の注意として明示
- 実運用では以下を推奨
  - 低トラフィック帯で migration を適用
  - 必要に応じて事前に別手順で index 作成を実施
  - 監視でロック待ち/遅延を観測し、適用ウィンドウを再調整

## 6. 残リスクと運用注意

残リスク:

- employee_number のデータ品質が低い環境では未解決件数が残る
- 曖昧一致を不更新にしているため、欠損率を 0 にできないケースがある
- index 作成タイミング次第で一時的なロック影響が出る可能性がある

運用注意:

- migration_area_member_display_name_backfill_runs の before/after 指標を run ごとに確認
- unresolved_member_count と ambiguous_match_count が高い場合は、元データ補正計画を別途立てる
- 再実行時も idempotent であることを前提に、段階的適用と観測を行う

## 7. 学びと改善点

学び:

- バックフィルは「更新率最大化」より「誤更新回避」を優先する設計が重要
- SQL migration でも可観測性を内包すると、運用判断と障害切り分けが速くなる
- idempotency と更新ガードの両立が、本番再実行可能性を高める

改善できる点:

- index ロック影響をさらに抑える運用設計（適用順序・時間帯・監視手順）の標準化
- 曖昧一致の未解決を減らすためのデータ品質改善プロセス（社員番号マスタ整備など）
- バックフィル run 指標のダッシュボード化としきい値アラート整備

## 8. 完了判定

Issue #42 の目的に対し、以下を満たした。

- user_id 優先・employee_number 次点の補完ルール実装
- 曖昧一致の不更新方針実装
- idempotent なバックフィル実装
- 可観測化メトリクス記録実装
- API 統合テスト追加と docs 更新
- テスト全件成功 (216/216)
