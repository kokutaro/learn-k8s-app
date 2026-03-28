---
name: dotnet-feature-workflow
description: ".NET バックエンド向けフルサイクル開発ワークフロー。Use when: C# / .NET の新機能追加、バグ修正、リファクタリングを Issue 作成から PR 作成まで一貫して完結させたいとき。issue作成 TDD 実装 dotnet build test コードレビュー PR作成 振り返り。"
argument-hint: "実装したい機能・修正したいバグ・変更内容の概要を入力してください"
---

# .NET Feature Development Workflow

ユーザーの指示を入力として、Issue 作成からPR作成まで一気通貫で進める **.NET バックエンド向け**フルサイクル開発ワークフロー。

## When to Use

- C# / .NET の新機能・バグ修正・リファクタリングを最初から最後まで完結させたいとき
- Issue を起点に実装し、レビューを経てPRを作成する標準的な開発フローを実行するとき
- `dotnet build` / `dotnet test` によるCIパイプラインを伴う .NET プロジェクトの開発をするとき

## Prerequisites

以下のエージェントが `.github/agents/` に存在すること:
- `issue.agent.md`
- `impl.agent.md`
- `review.agent.md`
- `triage.agent.md`
- `doc-writer.agent.md`
- `pr.agent.md`

## Workflow Overview

```
ユーザー指示
    ↓
[Step 1] issue-agent → Issue 作成 (Issue番号を取得)
    ↓
[Step 2] tdd-workflow skill → TDD 実装 + テスト
    ↓
[Step 3] review-agent → コードレビュー
    ↓
[Step 4] triage-agent → PRブロッカー抽出
    ↓
 PRブロッカーあり? ──Yes──→ [Step 5] 修正 → Step 2 に戻る
    ↓No
[Step 6] doc-writer-agent → 振り返りドキュメント作成
    ↓
[Step 7] pr-agent → PR 作成
```

---

## Step-by-Step Procedure

### Step 1: Issue 作成 (`issue-agent`)

`@issue` エージェントを呼び出し、ユーザーの指示をもとにIssueを作成する。

**実行内容:**
- ユーザー指示の要件を精査し、GitHub Issueを作成する
- **Issue番号を必ず受け取り、以降のステップで使用する**

**引き渡し情報 (次Stepへ):**
- Issue番号 (例: `#42`)
- Issueタイトルと要件の概要

**注意:**
- Issue作成は必須。スキップしない
- Issue番号が確定するまでStep 2には進まない

---

### Step 2: TDD 実装 (`tdd-workflow` skill / `impl-agent`)

`skills/tdd-workflow` スキルまたは `@impl` エージェントを利用してTDDで実装する。

**実行内容:**
1. 失敗するテストを先に書く (RED)
2. テストを通す最小実装を書く (GREEN)
3. コードを整理する (REFACTOR)
4. `dotnet restore` → `dotnet build` → `dotnet test` を実行して全テストがパスすることを確認

**引き渡し情報 (次Stepへ):**
- 変更されたファイル一覧
- 追加・修正されたテストの概要

---

### Step 3: コードレビュー (`review-agent`)

`@review` エージェントを呼び出し、実装内容をレビューする。

**実行内容:**
- Step 2 で変更されたコード・テストを対象にレビューを実施
- Findingsを重大度付きで出力する

**引き渡し情報 (次Stepへ):**
- レビューのFindings全文 (重大度・カテゴリ・内容)

---

### Step 4: トリアージ (`triage-agent`)

`@triage` エージェントに Issue番号とレビューのFindingsを渡してトリアージする。

**呼び出し方の例:**
```
@triage Issue #42 の実装に対するレビュー結果です。
[Findingsをここに貼り付け]
```

**実行内容:**
- PRブロッカー（必須対応）と非ブロッカー（任意対応）を分類して返す

**分岐:**
- **PRブロッカーあり** → Step 5 へ進む
- **PRブロッカーなし** → Step 6 へ進む

---

### Step 5: ブロッカー修正 (条件付き)

トリアージで抽出されたPRブロッカーを修正する。

**実行内容:**
1. 各PRブロッカーに対して修正を実装する
2. `dotnet restore` → `dotnet build` → `dotnet test` を再実行して全テストがパスすることを確認

**完了後:**
- **Step 2 に戻り**、修正内容を対象にStep 2 → Step 3 → Step 4 を繰り返す
- 「PRブロッカーなし」になるまでループする

---

### Step 6: 振り返りドキュメント作成 (`doc-writer-agent`)

`@doc-writer` エージェントを呼び出し、振り返りドキュメントを作成する。

**実行内容:**
- Issue番号・実装内容・レビュー結果・学びをもとに `docs/issue-{番号}-reflection.md` を作成する
- DDD/クリーンアーキテクチャ・テスト・プロセスの観点で振り返りを記述

---

### Step 7: PR 作成 (`pr-agent`)

`@pr` エージェントを呼び出し、PRを作成する。

**実行内容:**
- Issue番号と実装内容に基づき、以下を含むPRを作成する
  - `closes #<Issue番号>` でIssueにリンク
  - 変更内容の説明
  - テスト計画

---

## Quality Gates

各ステップを次に進む前に確認すること:

| Gate | 確認内容 |
|------|----------|
| Step 1 完了 | Issue番号が確定している |
| Step 2 完了 | `dotnet test` が全パス |
| Step 4 完了 | PRブロッカーの有無が確定している |
| Step 5 完了 (該当時) | 修正後も `dotnet test` が全パス |
| Step 6 完了 | 振り返りドキュメントが `docs/` に保存されている |
| Step 7 完了 | PRがリモートに作成されている |

## Tips

- Step 5 のループは**最大3回**を目安にする。3回ループしても同一ブロッカーが残る場合はユーザーに判断を仰ぐ
- `triage-agent` の判断に迷う場合は、AGENTS.md の「Domain Rules That Must Not Drift」を優先基準とする
- PR作成前に `git diff main...HEAD` で変更全体を確認し、意図しないファイルが含まれていないかチェックする
