# Issue #79 振り返り

## 1. 概要

- 対象Issue: #79
- 要約: Glassmorphism ベースのフロントエンドに、CSS カスタムプロパティによるカラースキーム切り替え（6パレット）と3モードダークモード（ライト/ダーク/システム）を追加した。
- 結果: PRブロッカー 0（2件検出・いずれも対応済み） / ユニットテスト 95件・E2Eテスト 20件 全パス / ESLint・tsc パス

## 2. 背景と目的

- 背景: 既存フロントエンドはハードコードされた Tailwind カラークラスで構成されており、カラースキーム変更やダークモード対応が困難な状態であった。
- 影響: ユーザーが画面の配色を好みに合わせて変更できず、暗所での使用時に眩しさを感じる可能性があった。
- 目的:
  - 6種のカラーパレット（teal, blue, violet, emerald, amber, rose）から任意に選択できるようにする
  - ライト/ダーク/システム連動の3モードダークモードを提供する
  - 設定を localStorage に永続化し、再訪問時に復元する
  - FOUC（Flash of Unstyled Content）を防止する

## 3. 実装内容とポイント

- 新規ファイル:
  - [src/lib/theme-colors.ts](../src/OsoujiSystem.Frontend/src/lib/theme-colors.ts) — 6パレット定義
  - [src/lib/theme-settings.ts](../src/OsoujiSystem.Frontend/src/lib/theme-settings.ts) — Zod スキーマ、localStorage 永続化、DOM 操作ヘルパー
  - [src/components/ThemeProvider.tsx](../src/OsoujiSystem.Frontend/src/components/ThemeProvider.tsx) — React Context + useLayoutEffect
  - [src/components/ui/ColorPalettePicker.tsx](../src/OsoujiSystem.Frontend/src/components/ui/ColorPalettePicker.tsx) — 6色スウォッチ radiogroup
  - [src/components/ui/DarkModeToggle.tsx](../src/OsoujiSystem.Frontend/src/components/ui/DarkModeToggle.tsx) — 3択セグメント
  - [src/components/ThemeSettingsPanel.tsx](../src/OsoujiSystem.Frontend/src/components/ThemeSettingsPanel.tsx) — ポップオーバーパネル
- 変更ファイル:
  - `index.html` — FOUC 防止インラインスクリプト追加
  - `src/index.css` — CSS 変数定義、ダークモードオーバーライド、glass-panel/field-shell テーマ化
  - `src/main.tsx` — ThemeProvider でアプリ全体をラップ
  - `src/routes/_app.tsx` — サイドバー色の CSS 変数化 + テーマ設定ボタン追加
  - UI コンポーネント 10 ファイル — ハードコードカラーを CSS 変数に置換
- 主な実装:
  1. **CSS 変数 + Tailwind v4 テーマング**: `@theme` ディレクティブで CSS 変数を Tailwind ユーティリティクラスとして登録し、`@variant dark` でクラスベースのダークモードバリアントを定義した。従来の tailwind.config は不使用。
  2. **React Context によるテーマ状態管理**: ThemeProvider が useLayoutEffect で同期的に DOM（`<html>` 要素のクラスと CSS 変数）を更新し、描画前にテーマを確定させる。
  3. **Zod スキーマによる永続化データ検証**: localStorage から読み込んだ設定を Zod でバリデーションし、不正値の場合はデフォルトにフォールバックする。
  4. **FOUC 防止**: index.html にインラインスクリプトを配置し、React ハイドレーション前に localStorage → DOM 変更を同期的に適用する。
- 設計判断:
  - **採用: CSS カスタムプロパティベースのテーマング** — Tailwind v4 の `@theme` と相性が良く、ランタイム JS の介入を最小限に抑えられるため採用した。
  - **採用: systemIsDark を独立 state として保持** — useLayoutEffect 内で setState する場合 ESLint の set-state-in-effect ルールに抵触するため、systemIsDark を独立 state とし isDark は純粋な派生値として計算する設計を採用した。
  - **トレードオフ: FOUC 防止スクリプトのパレットデータ二重管理** — index.html と theme-colors.ts に色値が重複する。React ハイドレーション前に色を適用する必要があるため、現時点では許容した。今後の改善候補として記録済み。

## 4. レビュー指摘と対応

| 区分 | 内容 | 判定 | 対応 |
|---|---|---|---|
| 必須対応 | Banner の success バリアントにダークモードオーバーライドが欠落していた | 対応済み | success バリアントに `dark:` オーバーライドを追加 |
| 必須対応 | ポップオーバーに `role="dialog"` を付与しているがフォーカス管理が未実装で ARIA 契約に違反 | 対応済み | `role="dialog"` を除去し `aria-expanded` のみに統一 |
| 任意課題 | Context value の `useMemo` 化による不要な再レンダリング防止 | 未対応 | 今後の改善候補として記録 |
| 任意課題 | radiogroup の矢印キーナビゲーション（roving tabindex）未実装 | 未対応 | 今後の改善候補として記録 |
| 任意課題 | パレットデータの二重管理（index.html + theme-colors.ts） | 未対応 | ビルド時コード生成等で解消を検討 |
| 任意課題 | ring-offset / field-shell focus ring のダークモード微調整 | 未対応 | 今後の改善候補として記録 |
| 任意課題 | `<html lang="ja">` の修正 | 未対応 | 今後の改善候補として記録 |
| 任意課題 | `loadThemeSettings()` の重複呼び出し最適化 | 未対応 | 今後の改善候補として記録 |
| 任意課題 | ThemeSettingsPanel のユニットテスト追加 | 未対応 | 今後の改善候補として記録 |

- 最終判定: PRブロッカー なし（2件のブロッカーはいずれも対応済み）

## 5. 検証結果

- ESLint: pass
- typecheck (tsc): pass
- ユニットテスト: pass（95件全パス、うち新規53件）
- E2Eテスト: pass（20件全パス、うち新規7件）
- 補足: 全検証項目がパスしており、スコープ外の失敗は確認されていない。

## 6. 学びと改善アクション

- 学び:
  - **Tailwind CSS v4 のテーマング手法**: `@theme` ディレクティブで CSS 変数を Tailwind ユーティリティクラスとして使用でき、`@variant dark` でクラスベースのダークモード切り替えを宣言的に定義できる。従来の `tailwind.config` は不要である。
  - **FOUC 防止のトレードオフ**: React ハイドレーション前にインラインスクリプトでテーマを適用する手法は FOUC を防止できるが、色値のハードコード二重管理が発生する。
  - **React の派生状態設計**: useLayoutEffect 内での setState は ESLint ルールに抵触する。独立 state + 派生値計算のパターンの方がクリーンである。
  - **ARIA role="dialog" の契約**: 非モーダルポップオーバーに `role="dialog"` を付けるとフォーカストラップが必須になる。`aria-expanded` のみの方が軽量かつ正確である。
  - **CSS 変数化の影響範囲**: ハードコードされた Tailwind クラスが多数のコンポーネントに散在していたため、一括置換でも個々のコンポーネントで既存テストとの整合性確認が必要であった。
  - **E2E セレクターの選定**: `aria-label` が複数要素に付与される場合、`data-testid` で明確に識別する方が堅牢である。role ベースのセレクターは ARIA 属性変更時に脆い。
- 改善アクション:
  1. Context value の `useMemo` 化を実施し、テーマ変更時の不要な再レンダリングを抑制する。
  2. パレットデータの二重管理を解消する仕組み（ビルド時コード生成やシングルソース化）を検討する。
  3. ThemeSettingsPanel のユニットテストを追加し、ポップオーバー開閉・キーボード操作のカバレッジを確保する。
  4. radiogroup に roving tabindex を実装し、キーボードアクセシビリティを向上させる。

## 7. 残課題

- Context value の `useMemo` 化
- radiogroup の矢印キーナビゲーション（roving tabindex）
- パレットデータの二重管理（index.html + theme-colors.ts）の解消
- ring-offset / field-shell focus ring のダークモード微調整
- `<html lang="ja">` の修正
- `loadThemeSettings()` の重複呼び出し最適化
- ThemeSettingsPanel のユニットテスト追加

## 8. 参照

- Issue: [#79 feat(frontend): カラースキーム変更 & ダークモード対応](https://github.com/kokutaro/learn-k8s-app/issues/79)
