# ArgoCD通知連携（現行 `app-production` 構成前提）

このドキュメントは、以下の現行構成を前提にしています。

- 本番Applicationは `app-production`（multi-source）
- アプリチャート: `https://github.com/kokutaro/learn-k8s-app.git`
- infra参照: `https://github.com/kokutaro/learn-k8s.git` を `ref: infra` として利用

## 方針

- `app-production` の定義はそのまま（変更不要）
- PR環境用Application/ApplicationSetに `preview.argocd.io/pr-number` annotationを付与
- ArgoCD Notificationsが `Synced + Healthy` 到達時に `repository_dispatch` を送信
- `learn-k8s-app` 側の `.github/workflows/preview-environment-ready.yml` がPRコメントを更新

## 追加する変更

具体的なYAMLは以下にまとめています。

- [docs/infra/argocd-preview-ready-changes.yaml](/Users/nyanpass/source/repos/learn-k8s/docs/infra/argocd-preview-ready-changes.yaml)

含まれる内容:

1. `argocd-notifications-secret` に `github-dispatch-token` を追加
2. `argocd-notifications-cm` に以下を追加

   - `service.webhook.github-dispatch`
   - `template.preview-environment-ready`
   - `trigger.on-preview-environment-ready`

3. PR環境ApplicationSet例（`preview-ready` ラベルのPRのみ対象）
4. PR番号 annotation 契約（`preview.argocd.io/pr-number`）

## 実運用で置換が必要な値

- `github-dispatch-token`
- `preview_url` のドメイン（例では `preview.example.com`）
- `values-pr-{{number}}.yaml` の実ファイル運用

## 動作確認

1. PRに `preview-ready` が付与される
2. ArgoCDのPR環境Applicationが `Synced + Healthy` になる
3. `learn-k8s-app` で `Preview Environment Ready` workflow が起動
4. PRコメント `<!-- preview-environment-link -->` が新規作成/更新される
