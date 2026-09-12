# Implementation / distribution

## 利用者側

公開配布版の通常手順は次の通りです。

1. Windows ZIP を展開して `Install-CloudflareBOX.cmd` を実行し、Windows のユーザーアカウント制御を承認します。配布 ZIP は win-x64 自己完結型なので、.NET Runtime の別途導入は不要です。
2. 起動したタスクトレイ UI で「Cloudflareと連携」を押します。
3. ブラウザで利用者自身の Cloudflare アカウントへログインして許可します。
4. CloudflareBOX が workers.dev、R2、D1、Worker、schema、binding、cron を自動作成または修復します。
5. Windows が初回 Android pairing 情報を発行します。Android アプリへ取り込み、登録を完了します。
6. 2台目以降は Windows の「Androidを追加」から pairing を発行します。

利用者は `ApiBase`、R2 Access Key / Secret、R2 bucket、D1 ID、Worker 名、setup token を入力しません。Windows service はインストール直後から自動起動し、Cloudflare 未連携の間は待機します。Cloudflare 連携後は同じ service が設定を読み直して受信を開始します。

インストーラーは管理者権限が必要な処理だけ UAC で昇格し、`ProgramData\CloudflareBOX` をサービスとインストールした利用者の双方が扱えるようにします。ZIP には `START-HERE.txt` も同梱します。

## 配布者側

Cloudflare OAuth の Public Client を登録し、Authorization Code + PKCE の loopback redirect を許可します。公開 Client ID は GitHub repository variable `CLOUDFLAREBOX_OAUTH_CLIENT_ID` に設定します。Client Secret はデスクトップ配布物へ入れません。

GitHub Actions は次を検証・生成します。

- Worker: typecheck、実テスト、bundle、Wrangler dry-run
- Windows: Release build、Core tests
- Android: debug APK build
- Windows package: installer PowerShell 構文確認、win-x64 self-contained publish、`coreclr.dll` を含む必須構成検査、ZIP 作成

Windows ZIP には `service`、`tray`、Worker bundle、`Install-CloudflareBOX.cmd`、`install.ps1`、`uninstall.ps1`、`START-HERE.txt` を含めます。repository variable が設定されている場合は `oauth-client-id.txt` も自動同梱します。未設定でも CI はビルドできますが、利用者は Cloudflare 連携できないため公開配布には使えません。

自己完結型 publish に必要な Microsoft runtime pack は `win11/NuGet.Config` で公式 `https://api.nuget.org/v3/index.json` から取得します。

## 開発用の手動経路

`CloudflareBox.Service --init <api-base> <setup-token> [destination]` は開発・移行用に残しています。通常利用者向けセットアップでは使用しません。

## 修復と解除

「修復」は同じ installation ID の専用資源を再検査し、不足している R2/D1/Worker を作成し、schema・binding・cron・workers.dev 有効化を再適用します。無関係な Cloudflare 資源は対象にしません。

「Cloudflare連携解除」は未完了 transfer がある場合に拒否します。安全条件を満たす場合だけ専用 Worker、D1、R2 を削除して OAuth token を失効します。アカウント共通 workers.dev サブドメインは削除しません。

Windows 側の `uninstall.ps1` は引き続き管理者 PowerShell から実行します。通常は受信履歴や復旧情報を残し、明示的に `-RemoveData` を指定した場合だけローカルデータも削除します。

## 現時点のリリース境界

ソースと CI の検証は実施できますが、新規の実 Cloudflare アカウントで OAuth -> 自動 provision -> Android upload -> Windows resume/download/decrypt/hash -> R2 cleanup まで通す本番 E2E は別途必要です。Windows コード署名、MSI、Android 正式署名・Play 配布も未実施です。公開配布時はこれらを CI 成功と混同しません。
