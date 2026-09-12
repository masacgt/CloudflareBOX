# CloudflareBOX

Android から非公開 Cloudflare R2 を一時中継にして、自宅の Windows 11 PC へ暗号化ファイルを自動転送する個人用システムです。

- 方向: Android -> R2 -> Windows 11
- 1ファイル上限: 10,000,000,000 bytes
- R2: 非公開、WindowsでSHA-256一致を確認して正式保存後、5分後に削除
- E2E暗号化: AES-256-GCM、ファイル鍵はWindows公開鍵でRSA-OAEP-SHA256ラップ
- 大容量: R2 Multipart Upload + 署名付きURL、途中再開対応
- Cloudflare: Workers + D1 + R2、workers.dev
- Windows: 常駐サービス + タスクトレイUI
- Android: Kotlin + Jetpack Compose + WorkManager

詳細仕様は `SPEC.md`、構成・API・試験・導入は `docs/` を参照してください。

## 構成

- `cloudflare/`: Workers API、D1 migration、管理画面、R2 cleanup
- `android/`: Androidアプリ本体
- `win11/`: Windowsサービス、トレイUI、インストーラー
- `dist/`: 現在のローカル配布物
- `docs/`: API、構成、試験、導入手順

## 現在のローカル配布物

- `dist/android/CloudflareBOX-Android-debug.apk`
- `dist/windows/service/`
- `dist/windows/tray/`
- `dist/windows/install.ps1`
- `dist/windows/uninstall.ps1`

Windowsの正式配布仕様はMSIです。現在のワークスペースではサービス登録まで行うPowerShellインストーラーを生成済みですが、MSIバイナリの生成環境は入っていないため、MSI化はリリース包装工程として残っています。

## 秘密情報

秘密値はソースへ保存しません。CloudflareのR2 S3 APIキーとペアリング用セットアップトークンはWorker secretとして設定してください。`cloudflare/wrangler.jsonc` のアカウントID、D1 ID、管理メールも本番値へ置き換えてからデプロイします。
