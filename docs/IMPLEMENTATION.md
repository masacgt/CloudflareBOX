# Implementation / deployment order

ローカル実装は `cloudflare/`、`android/`、`win11/` に分かれています。本番デプロイ前にCloudflareアカウント固有値と秘密値を設定する必要があります。

## Cloudflare

以下を同じPowerShellセッションで実行します。値は実際のCloudflareアカウントに合わせて設定してください。

```powershell
cd C:\Codex\CloudflareBOX\cloudflare
wrangler login
wrangler d1 create cloudflarebox-db
wrangler r2 bucket create cloudflarebox-transfer
wrangler secret put R2_ACCESS_KEY_ID
wrangler secret put R2_SECRET_ACCESS_KEY
wrangler secret put PAIRING_SETUP_TOKEN
wrangler d1 migrations apply cloudflarebox-db --remote
wrangler deploy
```

`wrangler.jsonc` の `R2_ACCOUNT_ID`、D1 `database_id`、`ADMIN_EMAIL` は本番値へ置き換えてから実行します。`/admin*` はCloudflare Accessで保護してから運用します。

## Windows 11

現在のローカル配布物はPowerShellインストーラーです。管理者PowerShellで以下をまとめて実行します。

```powershell
cd C:\Codex\CloudflareBOX\dist\windows
.\install.ps1 `
  -ApiBase 'https://<worker>.workers.dev/api/v1/' `
  -SetupToken '<PAIRING_SETUP_TOKEN>' `
  -Destination "$env:USERPROFILE\CloudflareBOX"
```

この処理はWindows鍵生成、初期ペアリング開始、サービス登録、自動起動設定、トレイ起動まで行います。正式配布仕様はMSIですが、現在の環境にはMSI生成ツールが無いため、MSIバイナリ化は未実施です。

## Android

現在のデバッグAPKは次です。

```text
C:\Codex\CloudflareBOX\dist\android\CloudflareBOX-Android-debug.apk
```

Windows側のトレイ画面からペアリングJSONをコピーし、Androidアプリへ入力してペアリングします。

## 完成判定

本番Cloudflare資源を作成した後、小容量ファイルでE2Eを確認し、その後に通信断、Android再起動、Windows再起動、PCオフライン、再開、SHA-256一致、R2削除、無料枠90%停止、端末失効、約10GBファイルを順に検証します。ローカルビルド成功と本番E2E成功は別に扱います。
