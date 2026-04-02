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
- `deep-research.agent.md`
- `impl.agent.md`
- `review.agent.md`
- `triage.agent.md`
- `doc-writer.agent.md`
- `pr.agent.md`
- `pr-semver-labeler.agent.md`

## Workflow Overview

```
ユーザー指示
    ↓
[Step 1] issue-agent → Issue 作成 (Issue番号を取得)
    ↓
[Step 2] deep-research-agent → リポジトリ調査 + 改修候補/参照先の整理
    ↓
[Step 3] tdd-workflow skill → TDD 実装 + テスト
    ↓
[Step 4] review-agent → コードレビュー
    ↓
[Step 5] triage-agent → PRブロッカー抽出
    ↓
 PRブロッカーあり? ──Yes──→ [Step 6] 修正 → Step 3 に戻る
    ↓No
[Step 7] doc-writer-agent → 振り返りドキュメント作成
    ↓
[Step 8] pr-agent → PR 作成
    ↓
[Step 9] pr-semver-labeler-agent → semver ラベル付与 + 付与確認
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

### Step 2: リポジトリ調査 (`deep-research-agent`)

`@deep-research` エージェントを呼び出し、Issue の要件を入力としてリポジトリ内の調査を行う。

**実行内容:**
- Issue の要件に対して、どこを改修すべきかを具体的なファイル、型、関数、テスト単位まで洗い出す
- 参照すべき既存実装、関連ドキュメント、類似パターン、影響範囲、実装時の注意点を整理する
- 必要に応じて `AGENTS.md` と関連 `docs/` を優先して調査根拠を固める

**引き渡し情報 (次Stepへ):**
- 改修候補箇所の一覧
- 参照すべき既存実装とドキュメント
- 影響範囲と実装上の注意点

**注意:**
- 調査を省略して実装に入らない
- `deep-research-agent` の調査結果を、以降のテスト設計と実装方針の根拠として使う

---

### Step 3: TDD 実装 (`tdd-workflow` skill / `impl-agent`)

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

### Step 4: コードレビュー (`review-agent`)

`@review` エージェントを呼び出し、実装内容をレビューする。必要に応じて Step 2 の調査結果も渡し、設計逸脱がないか確認する。

**実行内容:**
- Step 2 で変更されたコード・テストを対象にレビューを実施
- Findingsを重大度付きで出力する

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

**分岐:**
- **PRブロッカーあり** → Step 6 へ進む
- **PRブロッカーなし** → Step 7 へ進む

---

### Step 6: ブロッカー修正 (条件付き)

トリアージで抽出されたPRブロッカーを修正する。

**実行内容:**
1. 各PRブロッカーに対して修正を実装する
2. `dotnet restore` → `dotnet build` → `dotnet test` を再実行して全テストがパスすることを確認

**完了後:**
- **Step 3 に戻り**、修正内容を対象にStep 3 → Step 4 → Step 5 を繰り返す
- 「PRブロッカーなし」になるまでループする

---

### Step 7: 振り返りドキュメント作成 (`doc-writer-agent`)

`@doc-writer` エージェントを呼び出し、振り返りドキュメントを作成する。

**実行内容:**
- Issue番号・実装内容・レビュー結果・学びをもとに `docs/issue-{番号}-reflection.md` を作成する
- DDD/クリーンアーキテクチャ・テスト・プロセスの観点で振り返りを記述

---

### Step 8: PR 作成 (`pr-agent`)

`@pr` エージェントを呼び出し、PRを作成する。

**実行内容:**
- Issue番号と実装内容に基づき、以下を含むPRを作成する
  - `closes #<Issue番号>` でIssueにリンク
  - 変更内容の説明
  - テスト計画

---

### Step 9: semver ラベル付与と確認 (`pr-semver-labeler-agent`)

`@pr-semver-labeler` エージェントを呼び出し、PRの変更内容から `patch` / `minor` / `major` を判定させる。

**実行内容:**
- PR番号を引数として渡し、ラベルを Exactly 1つ付与する
- `patch` / `minor` / `major` の3種のうち、選択された1つだけが付与されていることを確認する
- 他のラベル（例: `bug`, `enhancement`）は保持されていることを確認する

**呼び出し方の例:**
```
@pr-semver-labeler 123
```

**確認コマンド例:**
```bash
gh pr view 123 --json labels --jq '.labels[].name'
```

---

## Quality Gates

各ステップを次に進む前に確認すること:

| Gate | 確認内容 |
|------|----------|
| Step 1 完了 | Issue番号が確定している |
| Step 2 完了 | 改修候補、参照先、影響範囲が整理されている |
| Step 3 完了 | `dotnet test` が全パス |
| Step 5 完了 | PRブロッカーの有無が確定している |
| Step 6 完了 (該当時) | 修正後も `dotnet test` が全パス |
| Step 7 完了 | 振り返りドキュメントが `docs/` に保存されている |
| Step 8 完了 | PRがリモートに作成されている |
| Step 9 完了 | `patch` / `minor` / `major` のラベルが Exactly 1つ付与されている |

## Tips

- Step 5 のループは**最大3回**を目安にする。3回ループしても同一ブロッカーが残る場合はユーザーに判断を仰ぐ
- Step 2 の deep-research 結果は、実装着手前の設計レビューとして扱い、改修箇所とテスト観点の根拠を必ず残す
- `triage-agent` の判断に迷う場合は、AGENTS.md の「Domain Rules That Must Not Drift」を優先基準とする
- PR作成前に `git diff main...HEAD` で変更全体を確認し、意図しないファイルが含まれていないかチェックする
- PR作成後は `@pr-semver-labeler <PR番号>` を必ず実行し、release自動化用ラベルを確定させる
