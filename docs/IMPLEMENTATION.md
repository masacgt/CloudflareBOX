# Implementation / distribution

## 利用者側

公開配布版の通常手順は次の通りです。

1. Windows ZIP を展開します。
2. `Install-CloudflareBOX.cmd` をダブルクリックします。
3. UAC が表示されたら許可します。CMD から `install.ps1` が管理者権限で実行されます。
4. installer が Service と Tray を配置し、Windows service を登録・起動します。
5. タスクトレイ UI で「Cloudflareと連携」を押します。
6. ブラウザで利用者自身の Cloudflare アカウントへログインして許可します。
7. CloudflareBOX が workers.dev、R2、D1、Worker、schema、binding、cron を自動作成または修復します。
8. Windows の「Androidを追加」から pairing 情報を発行し、Android アプリへ取り込みます。2台目以降も同じ方法で追加します。

配布 ZIP の Service と Tray は `win-x64 --self-contained true` で publish されるため、利用者が .NET Runtime を別途入れる必要はありません。

Windows service はインストール直後から自動起動し、Cloudflare 未連携の間は待機します。Cloudflare 連携後は同じ service が `ProgramData\CloudflareBOX` の共有設定を読み、再インストールせず受信を開始します。

## Cloudflare 自動構築

Cloudflare 連携後は、OAuth で得た権限を使って、その installation ID に属する資源だけを構築します。

- workers.dev の利用可能状態を確認し、必要なら有効化
- installation 専用 R2 bucket の作成
- installation 専用 D1 database の作成
- D1 schema の適用
- Worker の作成・更新
- D1/R2 bindings の設定
- scheduled cleanup 用 cron の設定

修復処理は同じ installation ID を基準に繰り返し実行できるようにし、既存資源が正常なら再利用します。別の Cloudflare 資源は対象にしません。

## 配布者側

Cloudflare OAuth の Public Client を登録し、Authorization Code + PKCE の loopback redirect を許可します。公開 Client ID は GitHub repository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID` に設定します。Client Secret はデスクトップ配布物へ入れません。

GitHub Actions は次を検証・生成します。

- Worker: typecheck、test、bundle、Wrangler deploy dry-run
- Windows: Release build、Core tests
- Android: debug APK build
- Windows package: installer PowerShell 構文確認、Service/Tray self-contained publish、Worker bundle 取得、必須ファイル確認、ZIP 作成

Windows package の assemble step では少なくとも次を確認してから ZIP を作成します。

- `service/CloudflareBox.Service.exe`
- `service/coreclr.dll`
- `tray/CloudflareBox.Tray.exe`
- `tray/coreclr.dll`
- `worker/cloudflarebox-worker.mjs`
- `install.ps1`
- `uninstall.ps1`
- `Install-CloudflareBOX.cmd`
- `START-HERE.txt`

repository variable が設定されている場合は `oauth-client-id.txt` も同梱します。未設定でも CI 自体は成功できますが、その ZIP では一般利用者が Cloudflare OAuth 連携を完了できないため公開配布には使用しません。

## 開発用の手動経路

`CloudflareBox.Service --init <api-base> <setup-token> [destination]` は開発・移行用に残しています。通常利用者向けセットアップでは使用しません。

## 修復と解除

「修復」は同じ installation ID の専用資源を再検査し、不足している R2/D1/Worker を作成し、schema・binding・cron・workers.dev 有効化を再適用します。

「Cloudflare連携解除」は未完了 transfer がある場合に拒否します。安全条件を満たす場合だけ installation 専用 Worker、D1、R2 を削除して OAuth token を失効します。アカウント共通 workers.dev サブドメインは削除しません。

`uninstall.ps1` は現在、管理者 PowerShell から実行する運用です。

## 現時点のリリース境界

CI ではソース、暗号処理、Range resume、防御的な package assembly まで検証できます。実ユーザーの Cloudflare アカウントを使う OAuth 自動構築 E2E は別途必要です。

MSI、Windows コード署名、Android 正式署名・Play 配布も未実施です。公開配布可否は CI 成功、本番 OAuth E2E、署名・配布経路を分けて判定します。
