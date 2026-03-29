---
description: 指定されたイシューと実装に対するプルリクエストを作成します。
tools:
  [
    "execute",
    "read",
    "search",
    "todo",
    "web",
    "ms-vscode.vscode-websearchforcopilot/websearch",
  ]
---

与えられたイシューと実装に対する、プルリクエストを作成してください。

## 手順 (#tool:todo)

1. PR が作成できる状態にあるのか確認する
  - ドキュメント更新の忘れがないか
  - 未コミットの変更がないか
  - テスト (CI) が通過するか
2. 作成にふさわしくない状況だと判断される場合、修正案を示して終了します。そうでなければ `gh pr create` コマンドを使用して PR を作成します。
3. 作成された PR の内容とリンクをユーザーに通知します。

## Notes

- 関連する Issue がある場合、その Issue 番号を含めてください (e.g., `Closes #<number>`)
- GitHub Issue に追加のコメントが必要であれば、コメントを残しておいてください。
- ghコマンドを使用する際、Bodyを一時ファイルに書き出さずに、直接コマンドに渡してください。
- `gh pr create` が失敗しやすい主因は、`--body` の改行やクオート崩れです。以下を必ず守ってください。
- Body の改行コードは **LF (`\n`) のみ** を使用し、`\r\n` (CRLF) を混在させないでください。
- 複数行 Body は `--body $'...'` 形式で `\n` を明示し、1 コマンドで実行してください。

推奨テンプレート)
```bash
gh pr create \
  --title "fix: PR Title" \
  --base main \
  --head feature-branch \
  --body $'## 概要\n- 変更点A\n- 変更点B\n\n## 変更ファイル\n- path/to/file1\n- path/to/file2\n\n## テスト計画\n- [x] テスト1\n- [x] テスト2\n\nCloses #123'
```

改行コードの注意)
```bash
# NG: CRLF が混ざる可能性のある貼り付けをそのまま使う
gh pr create --title "PR Title" --base main --head feature-branch --body "1行目\\r\\n2行目"

# OK: LF を明示
gh pr create --title "PR Title" --base main --head feature-branch --body $'1行目\n2行目'
```

## ツール

- #tool:ms-vscode.vscode-websearchforcopilot/websearch: ウェブ検索
- `gh`: GitHub リポジトリの操作

## ドキュメント

- `docs/`