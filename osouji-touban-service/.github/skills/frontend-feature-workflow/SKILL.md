---
name: frontend-feature-workflow
description: "React / TypeScript フロントエンド向けフルサイクル開発ワークフロー。Use when: Vite + React + TanStack Router / Query の新機能追加、バグ修正、UIコンポーネント実装を Issue 作成から PR 作成まで一貫して完結させたいとき。issue作成 TDD 実装 vitest playwright ESLint コードレビュー PR作成 振り返り。"
argument-hint: "実装したい機能・修正したいバグ・変更内容の概要を入力してください"
---

# Frontend Feature Development Workflow

ユーザーの指示を入力として、Issue 作成からPR作成まで一気通貫で進める **React / TypeScript フロントエンド向け**フルサイクル開発ワークフロー。

## When to Use

- React / TypeScript の新機能・バグ修正・UIコンポーネント実装を最初から最後まで完結させたいとき
- Issue を起点に実装し、レビューを経てPRを作成する標準的な開発フローを実行するとき
- `vitest` (ユニット) / `playwright` (E2E) によるテストを伴うフロントエンド開発をするとき

## Tech Stack

| 項目 | 技術 |
|------|------|
| ビルド | Vite + TypeScript (`tsc -b`) |
| ルーティング | TanStack Router |
| サーバー状態 | TanStack Query |
| スタイル | Tailwind CSS |
| ユニットテスト | Vitest + Testing Library + MSW |
| E2Eテスト | Playwright |
| Lint | ESLint (TypeScript / React) |

## Prerequisites

以下のエージェントが `.github/agents/` に存在すること:
- `issue.agent.md`
- `impl.agent.md`
- `review.agent.md`
- `triage.agent.md`
- `doc-writer.agent.md`
- `pr.agent.md`

作業ディレクトリ: `osouji-system-frontend/`

## Workflow Overview

```
ユーザー指示
    ↓
[Step 1] issue-agent → Issue 作成 (Issue番号を取得)
    ↓
[Step 2] impl-agent (tdd-workflow skill) → TDD 実装 + ユニットテスト
    ↓
[Step 3] E2E 確認 → Playwright でクリティカルパスを検証
    ↓
[Step 4] review-agent → コードレビュー
    ↓
[Step 5] triage-agent → PRブロッカー抽出
    ↓
 PRブロッカーあり? ──Yes──→ [Step 6] 修正 → Step 2 に戻る
    ↓No
[Step 7] doc-writer-agent → 振り返りドキュメント作成
    ↓
[Step 8] pr-agent → PR 作成
```

---

## Step-by-Step Procedure

### Step 1: Issue 作成 (`issue-agent`)

`@issue` エージェントを呼び出し、ユーザーの指示をもとにIssueを作成する。

**実行内容:**
- ユーザー指示の要件を精査し、GitHub Issueを作成する
- **Issue番号を必ず受け取り、以降のステップで使用する**

**注意:**
- Issue作成は必須。スキップしない
- Issue番号が確定するまでStep 2には進まない

---

### Step 2: TDD 実装 (`impl-agent` / `tdd-workflow` skill)

`@impl` エージェントを利用してTDDでユニットテストと実装を行う。

**実行内容:**
1. 失敗するテストを先に書く (RED) — `vitest` ベース
2. テストを通す最小実装を書く (GREEN)
3. コードを整理する (REFACTOR)
4. 以下のコマンドで全チェックをパスすることを確認:

```bash
cd osouji-system-frontend
npm run lint          # ESLint
npx tsc -b --noEmit   # 型チェック
npm run test          # Vitest ユニットテスト
```

**テスト配置規則:**
- コンポーネントテスト: `src/components/**/*.test.tsx`
- フィーチャーテスト: `src/features/**/*.test.tsx`
- ユーティリティテスト: `src/lib/**/*.test.ts`
- MSW モック: `src/test/` 配下のハンドラーを活用する

**引き渡し情報 (次Stepへ):**
- 変更されたファイル一覧
- 追加・修正されたテストの概要

---

### Step 3: E2E 確認 (Playwright)

クリティカルパス（主要ユーザーフロー）のE2Eテストを実施する。

**実行内容:**
```bash
cd osouji-system-frontend
npm run e2e
```

**対象シナリオ:**
- 今回の変更が影響するページ・フローのテストが通ること
- 新UIを追加した場合は `tests/e2e/` に対応するテストを追加する

**E2Eが失敗した場合:**
- 失敗内容を確認し、ユニットテストレベルで原因を特定してStep 2に戻る

---

### Step 4: コードレビュー (`review-agent`)

`@review` エージェントを呼び出し、実装内容をレビューする。

**実行内容:**
- Step 2-3 で変更されたコード・テストを対象にレビューを実施
- Findingsを重大度付きで出力する

**フロントエンド固有のレビュー観点:**
- `React.memo` / `useMemo` / `useCallback` の不適切な使用
- コンポーネントの責務分離（UIとロジックの混在）
- TanStack Query の `staleTime` / `gcTime` 設定の妥当性
- Zod スキーマのバリデーション漏れ
- アクセシビリティ (aria属性、キーボード操作)

**引き渡し情報 (次Stepへ):**
- レビューのFindings全文 (重大度・カテゴリ・内容)

---

### Step 5: トリアージ (`triage-agent`)

`@triage` エージェントに Issue番号とレビューのFindingsを渡してトリアージする。

**呼び出し方の例:**
```
@triage Issue #42 の実装に対するレビュー結果です。
[Findingsをここに貼り付け]
```

**実行内容:**
- PRブロッカー（必須対応）と非ブロッカー（任意対応）を分類して返す

**フロントエンド固有のブロッカー基準:**
| カテゴリ | 具体例 |
|----------|--------|
| 型安全性の破綻 | `any` 型の多用、型アサーション (`as`) のリスク |
| ランタイムエラー | 境界値でのクラッシュ、未ハンドルの Promise rejection |
| セキュリティ | XSS (`dangerouslySetInnerHTML`)、機密情報の `localStorage` 保存 |
| テスト欠落 | 変更に対応するユニットテストが存在しない |
| ビルド失敗 | `tsc -b` または `vite build` が通らない |

**分岐:**
- **PRブロッカーあり** → Step 6 へ進む
- **PRブロッカーなし** → Step 7 へ進む

---

### Step 6: ブロッカー修正 (条件付き)

トリアージで抽出されたPRブロッカーを修正する。

**実行内容:**
1. 各PRブロッカーに対して修正を実装する
2. 以下のコマンドで全チェックを再確認:

```bash
cd osouji-system-frontend
npm run lint
npx tsc -b --noEmit
npm run test
npm run e2e
```

**完了後:**
- **Step 2 に戻り**、修正内容を対象にStep 2 → Step 5 を繰り返す
- 「PRブロッカーなし」になるまでループする (最大3回)

---

### Step 7: 振り返りドキュメント作成 (`doc-writer-agent`)

`@doc-writer` エージェントを呼び出し、振り返りドキュメントを作成する。

**実行内容:**
- Issue番号・実装内容・レビュー結果・学びをもとに `docs/issue-{番号}-reflection.md` を作成する
- フロントエンド固有の観点 (コンポーネント設計・状態管理・パフォーマンス) を含めて記述

---

### Step 8: PR 作成 (`pr-agent`)

`@pr` エージェントを呼び出し、PRを作成する。

**実行内容:**
- Issue番号と実装内容に基づき、以下を含むPRを作成する
  - `closes #<Issue番号>` でIssueにリンク
  - 変更内容の説明 (コンポーネント・ルート・APIフックごとの変更点)
  - テスト計画 (ユニットテスト + E2E の確認手順)

---

## Quality Gates

各ステップを次に進む前に確認すること:

| Gate | 確認内容 |
|------|----------|
| Step 1 完了 | Issue番号が確定している |
| Step 2 完了 | `npm run lint` + `tsc -b --noEmit` + `npm run test` が全パス |
| Step 3 完了 | `npm run e2e` が全パス |
| Step 5 完了 | PRブロッカーの有無が確定している |
| Step 6 完了 (該当時) | 修正後も全チェックがパス |
| Step 7 完了 | 振り返りドキュメントが `docs/` に保存されている |
| Step 8 完了 | PRがリモートに作成されている |

## Tips

- E2Eテストはサーバーが起動済みであることが前提。ローカルでは `npm run dev` を別ターミナルで起動してから `npm run e2e` を実行する
- `msw` のモックハンドラーは `src/test/` に集約されている。新しいAPIエンドポイントを呼ぶ場合はハンドラーを追加する
- Step 6 のループは **最大3回** を目安にする。3回ループしても同一ブロッカーが残る場合はユーザーに判断を仰ぐ
- PR作成前に `git diff main...HEAD -- osouji-system-frontend/` で変更全体を確認し、意図しないファイルが含まれていないかチェックする
