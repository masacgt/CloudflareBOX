# Implementation / distribution

## 利用者側

公開配布版の通常手順は次の通りです。

1. Windows ZIP を展開し、管理者 PowerShell で `install.ps1` を実行します。`ApiBase`、R2 key、D1 ID、setup token の入力は不要です。
2. 起動したタスクトレイ UI で「Cloudflareと連携」を押します。
3. ブラウザで利用者自身の Cloudflare アカウントへログインして許可します。
4. CloudflareBOX が workers.dev、R2、D1、Worker、schema、binding、cron を自動作成または修復します。
5. Windows が初回 Android pairing 情報を発行します。Android アプリへ取り込み、登録を完了します。
6. 2台目以降は Windows の「Androidを追加」から pairing を発行します。

Windows service はインストール直後から自動起動し、Cloudflare 未連携の間は `not_connected` で待機します。Cloudflare 連携後は同じ service が設定を読み直して受信を開始します。

## 配布者側

Cloudflare OAuth の Public Client を登録し、Authorization Code + PKCE の loopback redirect を許可します。公開 Client ID は GitHub repository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID` に設定します。Client Secret はデスクトップ配布物へ入れません。

GitHub Actions は次を検証・生成します。

- Worker: typecheck、test、bundle、Wrangler dry-run
- Windows: Release build、Core tests
- Android: debug APK build
- Windows package: publish 済み service/tray、Worker bundle、installer をまとめた `CloudflareBOX-Windows.zip`

repository variable が設定されている場合、Windows ZIP に `oauth-client-id.txt` が自動同梱されます。未設定でも CI はビルドできますが、利用者は Cloudflare 連携できないため公開配布には使えません。

## 開発用の手動経路

`CloudflareBox.Service --init <api-base> <setup-token> [destination]` は開発・移行用に残しています。通常利用者向けセットアップでは使用しません。

## 修復と解除

「修復」は同じ installation ID の専用資源を再検査し、不足している R2/D1/Worker を作成し、schema・binding・cron・workers.dev 有効化を再適用します。無関係な Cloudflare 資源は対象にしません。

「Cloudflare連携解除」は未完了 transfer がある場合に拒否します。安全条件を満たす場合だけ専用 Worker、D1、R2 を削除して OAuth token を失効します。アカウント共通 workers.dev サブドメインは削除しません。

## 現時点のリリース境界

ソースと CI の検証は実施できますが、本番 Cloudflare アカウントでの OAuth 自動構築 E2E は別途必要です。MSI、Windows コード署名、Android 正式署名・Play 配布も未実施です。公開配布時はこれらを CI 成功と混同しません。
